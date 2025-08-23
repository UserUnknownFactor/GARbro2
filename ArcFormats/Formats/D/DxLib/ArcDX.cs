using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.DxLib
{
    internal class DxArchive : ArcFile
    {
        public readonly IDxKey Encryption;
        public readonly int Version;

        public DxArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IDxKey enc, int version)
            : base (arc, impl, dir)
        {
            Encryption = enc;
            Version = version;
        }
    }

    [Serializable]
    public class DxScheme : ResourceScheme
    {
        public IList<IDxKey> KnownKeys;
    }

    public class DxOptions : ResourceOptions
    {
        public string Password { get; set; }
    }

    [Export (typeof (ArchiveFormat))]
    public class DxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DXA"; } }
        public override string Description { get { return "DxLib engine resource archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        public DxOpener ()
        {
            Extensions = new string[] { "dxa", "hud", "usi", "med", "dat", "bin", "bcx", "wolf" };
            Signatures = new uint[] {
                0x19EF8ED4, 0xA9FCCEDD, 0x0AEE0FD3, 0x5523F211, 0x5524F211,
                0x69FC5FE4, 0x09E19ED9, 0x7DCC5D83, 0xC55D4473, 0
            };
        }

        public DxScheme DefaultScheme = new DxScheme { KnownKeys = new List<IDxKey>() };

        public IList<IDxKey> KnownKeys { get { return DefaultScheme.KnownKeys; } }

        public override object GetAccessWidget()
        {
            return new WidgetPassword 
            { 
                FormatTag = this.Tag,
                Scheme = this.Scheme
            };
        }

        public override ResourceOptions GetOptions(object widget)
        {
            var passwordWidget = widget as WidgetPassword;
            if (passwordWidget != null)
            {
                return new DxOptions
                {
                    Password = passwordWidget.Password
                };
            }
            return GetDefaultOptions();
        }

        public override ResourceOptions GetDefaultOptions()
        {
            return new DxOptions { Password = string.Empty };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset < 0x1C)
                return null;

            foreach (var enc in KnownKeys)
            {
                var arc = TryKeyWithFile(file, enc);
                if (arc != null)
                {
                    if (KnownKeys[0] != enc)
                    {
                        // move last used key to the top of the known keys list
                        KnownKeys.Remove (enc);
                        KnownKeys.Insert (0, enc);
                    }

                    Comment = $"Version {arc.Version}";
                    return arc;
                }
            }

            var guessedArc = GuessKey (file);
            if (guessedArc != null)
            {
                var encryption = guessedArc.Encryption;
                KnownKeys.Insert (0, encryption);
                Trace.WriteLine (string.Format ("Restored key '{0}'", encryption.Password), $"[{Tag}]");
                return guessedArc;
            }

            if (!HasMatchingExtension(file))
                return null;

            var options = Query<DxOptions>(Localization._T ("ArcEncryptedNotice"));
            if (options == null || string.IsNullOrWhiteSpace(options.Password))
                return null;

            var passwordArc = TryPassword(file, options.Password);
            if (passwordArc != null)
            {
                var encryption = passwordArc.Encryption;
                KnownKeys.Insert (0, encryption);
                Trace.WriteLine (string.Format ("Password '{0}' successful", encryption.Password), $"[{Tag}]");

                WidgetPassword.MarkPasswordAsSuccessful(this.Tag, options.Password);
            }

            return passwordArc;
        }

        DxArchive TryPassword (ArcView file, string password)
        {
            if (string.IsNullOrEmpty (password))
                return null;

            var dxKey = new DxKey (password);
            var arc = TryKeyWithFile (file, dxKey);
            if (arc != null)
                return arc;

            var dxKey7 = new DxKey7 (password);
            arc = TryKeyWithFile (file, dxKey7);
            if (arc != null)
                return arc;

            try
            {
                if (password.Length % 2 == 0 && password.Length >= 24)
                {
                    var hexKey = new byte[password.Length / 2];
                    for (int i = 0; i < hexKey.Length; i++)
                    {
                        hexKey[i] = Convert.ToByte (password.Substring (i * 2, 2), 16);
                    }

                    var hexDxKey = new DxKey (hexKey);
                    arc = TryKeyWithFile (file, hexDxKey);
                    if (arc != null)
                        return arc;
                }
            }
            catch { }

            return null;
        }

        DxArchive TryKeyWithFile (ArcView file, IDxKey dxKey)
        {
            try
            {
                var key = dxKey.Key;
                if (key == null || key.Length < 12)
                    return null;

                uint signature = file.View.ReadUInt32 (0);
                uint sig_key = LittleEndian.ToUInt32 (key, 0);
                uint sig_test = signature ^ sig_key;
                int version = (int)(sig_test >> 16);

                if (0x5844 == (sig_test & 0xFFFF) && version <= 7) // 'DX'
                {
                    var dir = ReadIndex (file, version, key);
                    if (dir != null)
                        return new DxArchive (file, this, dir, dxKey, version);
                }
            }
            catch { }

            return null;
        }

        DxArchive GuessKey (ArcView file)
        {
            if (file.MaxOffset > uint.MaxValue)
                return null;
            var key = GuessKeyV6 (file);
            if (key != null)
            {
                var dir = ReadIndex (file, 6, key);
                if (dir != null)
                    return new DxArchive (file, this, dir, new DxKey (key), 6);
            }
            key = new byte[12];
            for (short version = 4; version >= 1; --version)
            {
                file.View.Read (0, key, 0, 12);
                key[0] ^= (byte)'D';
                key[1] ^= (byte)'X';
                key[2] ^= (byte)version;
                int base_offset = version > 3 ? 0x1C : 0x18;
                key[8] ^= (byte)base_offset;
                uint key0 = LittleEndian.ToUInt32 (key, 0);
                uint index_offset = file.View.ReadUInt32 (12) ^ key0;
                if (index_offset <= base_offset || index_offset >= file.MaxOffset)
                    continue;
                uint index_size = (uint)(file.MaxOffset - index_offset);
                if (index_size > 0xFFFFFF)
                    continue;
                key[4] ^= (byte)index_size;
                key[5] ^= (byte)(index_size >> 8);
                key[6] ^= (byte)(index_size >> 16);
                try
                {
                    var dir = ReadIndex (file, version, key);
                    if (null != dir)
                        return new DxArchive (file, this, dir, new DxKey (key), version);
                }
                catch { /* ignore parse errors */ }
            }
            return null;
        }

        byte[] GuessKeyV6 (ArcView file)
        {
            var header = file.View.ReadBytes (0, 0x30);
            header[0] ^= (byte)'D';
            header[1] ^= (byte)'X';
            header[2] ^= 6;
            uint key0 = header.ToUInt32 (0);
            header[8] ^= (byte)0x30;
            uint data_offset_hi = header.ToUInt32 (12) ^ key0;
            if (data_offset_hi != 0)
                return null;
            uint key2 = header.ToUInt32 (8);
            uint key1 = header.ToUInt32 (0x1C);
            long index_offset = header.ToInt64 (0x10) ^ key1 ^ ((long)key2 << 32);
            if (index_offset <= 0x30 || index_offset >= file.MaxOffset)
                return null;
            var key = new byte[12];
            LittleEndian.Pack (key0, key, 0);
            LittleEndian.Pack (key1, key, 4);
            LittleEndian.Pack (key2, key, 8);
            return key;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            var dx_arc = arc as DxArchive;
            if (null == dx_arc)
                return input;
            var dx_ent = (PackedEntry)entry;
            long dec_offset = entry.Offset;
            if (dx_arc.Version > 5)
            {
                dec_offset = dx_ent.UnpackedSize;
            }
            var key = dx_arc.Encryption.GetEntryKey (dx_ent.Name);
            input = new EncryptedStream (input, dec_offset, key);
            if (!dx_ent.IsPacked)
                return input;
            using (input)
            {
                var data = Unpack (input);
                return new BinMemoryStream (data, entry.Name);
            }
        }

        protected byte[] Unpack (Stream stream)
        {
            using (var input = new ArcView.Reader (stream))
            {
                uint unpacked_size = input.ReadUInt32();
                int remaining = input.ReadInt32() - 9;

                if (unpacked_size > int.MaxValue || remaining < 0)
                    throw new InvalidFormatException ("Invalid compressed data header");

                var output = new byte[unpacked_size];
                byte control_code = input.ReadByte();
                int dst = 0;

                while (remaining > 0 && dst < output.Length)
                {
                    if (remaining < 1)
                        throw new InvalidFormatException ("Unexpected end of compressed data");

                    byte b = input.ReadByte();
                    --remaining;

                    if (b != control_code)
                    {
                        if (dst >= output.Length)
                            break; // buffer full
                        output[dst++] = b;
                        continue;
                    }

                    if (remaining < 1)
                        throw new InvalidFormatException ("Unexpected end of compressed data");

                    b = input.ReadByte();
                    --remaining;

                    if (b == control_code)
                    {
                        if (dst >= output.Length)
                            break; // buffer full
                        output[dst++] = b;
                        continue;
                    }

                    if (b > control_code)
                        --b;

                    int count = b >> 3;
                    if (0 != (b & 4))
                    {
                        if (remaining < 1)
                            throw new InvalidFormatException ("Unexpected end of compressed data");
                        count |= input.ReadByte() << 5;
                        --remaining;
                    }
                    count += 4;

                    if (dst + count > output.Length)
                        count = output.Length - dst;

                    if (count <= 0)
                        break; // nothing left to copy

                    int offset;
                    switch (b & 3)
                    {
                        case 0:
                            if (remaining < 1)
                                throw new InvalidFormatException ("Unexpected end of compressed data");
                            offset = input.ReadByte();
                            --remaining;
                            break;

                        case 1:
                            if (remaining < 2)
                                throw new InvalidFormatException ("Unexpected end of compressed data");
                            offset = input.ReadUInt16();
                            remaining -= 2;
                            break;

                        case 2:
                            if (remaining < 3)
                                throw new InvalidFormatException ("Unexpected end of compressed data");
                            offset = input.ReadUInt16();
                            offset |= input.ReadByte() << 16;
                            remaining -= 3;
                            break;

                        default:
                            throw new InvalidFormatException ("Invalid compression flag");
                    }

                    ++offset;

                    // validate offset before using it
                    if (offset > 0 && offset <= dst)
                    {
                        Binary.CopyOverlapped (output, dst - offset, dst, count);
                    }
                    else
                    {
                        Trace.WriteLine($"Invalid copy offset {offset} at position {dst}", $"[{Tag}]");
                        for (int i = 0; i < count && dst < output.Length; i++)
                            output[dst++] = 0;
                        continue;
                    }
                    dst += count;
                }

                if (dst != unpacked_size)
                    Trace.WriteLine($"Decompression size mismatch: expected {unpacked_size}, got {dst}", $"[{Tag}]");

                return output;
            }
        }

        protected List<Entry> ReadIndex (ArcView file, int version, byte[] key)
        {
            DxHeader dx = null;
            if (version <= 4)
                dx = ReadArcHeaderV4 (file, version, key);
            else if (version >= 6)
                dx = ReadArcHeaderV6 (file, version, key);
            if (null == dx || dx.DirTable >= dx.IndexSize || dx.FileTable >= dx.IndexSize)
                return null;
            using (var encrypted = file.CreateStream (dx.IndexOffset, (uint)dx.IndexSize))
            using (var index = new EncryptedStream (encrypted, version >= 6 ? 0 : dx.IndexOffset, key))
            using (var reader = IndexReader.Create (dx, version, index))
            {
                if (reader != null)
                    return reader.Read();
            }
            return null;
        }

        DxHeader ReadArcHeaderV4 (ArcView file, int version, byte[] key)
        {
            var header = file.View.ReadBytes (4, 0x18);
            if (0x18 != header.Length)
                return null;
            Decrypt (header, 0, header.Length, 4, key);
            return new DxHeader {
                IndexSize   = LittleEndian.ToUInt32 (header, 0),
                BaseOffset  = LittleEndian.ToUInt32 (header, 4),
                IndexOffset = LittleEndian.ToUInt32 (header, 8),
                FileTable   = LittleEndian.ToUInt32 (header, 0x0C),
                DirTable    = LittleEndian.ToUInt32 (header, 0x10),
                CodePage    = 932,
            };
        }

        DxHeader ReadArcHeaderV6 (ArcView file, int version, byte[] key)
        {
            var header = file.View.ReadBytes (4, 0x2C);
            if (0x2C != header.Length)
                return null;
            Decrypt (header, 0, header.Length, 4, key);
            return new DxHeader {
                IndexSize   = LittleEndian.ToUInt32 (header, 0),
                BaseOffset  = LittleEndian.ToInt64  (header, 4),
                IndexOffset = LittleEndian.ToInt64  (header, 0x0C),
                FileTable   = LittleEndian.ToInt64  (header, 0x14),
                DirTable    = LittleEndian.ToInt64  (header, 0x1C),
                CodePage    = LittleEndian.ToInt32  (header, 0x24),
            };
        }

        internal static void Decrypt (byte[] data, int index, int count, long offset, byte[] key)
        {
            if (key.Length == 0)
                return;

            int key_pos = (int)(offset % key.Length);
            for (int i = 0; i < count; ++i)
            {
                data[index + i] ^= key[key_pos++];
                if (key.Length == key_pos)
                    key_pos = 0;
            }
        }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (DxScheme)value; }
        }
    }

    internal class DxHeader
    {
        public long BaseOffset;
        public long IndexOffset;
        public long IndexSize;
        public long FileTable;
        public long DirTable;
        public int  CodePage;
    }

    internal abstract class IndexReader : IDisposable
    {
        protected readonly int  m_version;
        protected DxHeader      m_header;
        protected BinaryStream  m_input;
        protected Encoding      m_encoding;
        protected List<Entry>   m_dir = new List<Entry>();

        internal int Version { get { return m_version; } }

        protected IndexReader (DxHeader header, int version, Stream input)
        {
            m_header = header;
            m_version = version;
            m_input = new BinaryStream (input, "");
            m_encoding = Encoding.GetEncoding (header.CodePage);
        }

        public static IndexReader Create (DxHeader header, int version, Stream input)
        {
            if (version <= 4)
                return new IndexReaderV2 (header, version, input);
            else if (version >= 6 && version < 8)
                return new IndexReaderV6 (header, version, input);
            else if (version >= 8)
                return new IndexReaderV8 (header, version, input);
            else
                throw new InvalidFormatException($"Not supported DX archive version {version}.");
        }

        public List<Entry> Read ()
        {
            ReadFileTable ("", 0);
            return m_dir;
        }

        protected abstract void ReadFileTable (string root, long table_offset);

        protected string ExtractFileName (long table_offset)
        {
            m_input.Position = table_offset;
            int name_offset = m_input.ReadUInt16() * 4 + 4;
            m_input.Position = table_offset + name_offset;
            return m_input.ReadCString (m_encoding);
        }

        #region IDisposable Members
        bool disposed = false;
        public void Dispose ()
        {
            if (!disposed)
            {
                m_input.Dispose();
                disposed = true;
            }
        }
        #endregion
    }

    internal sealed class IndexReaderV2 : IndexReader
    {
        readonly int    m_entry_size;

        public IndexReaderV2 (DxHeader header, int version, Stream input) : base (header, version, input)
        {
            m_entry_size = Version >= 2 ? 0x2C : 0x28;
        }

        private class DxDirectory
        {
            public int DirOffset;
            public int ParentDirOffset;
            public int FileCount;
            public int FileTable;
        }

        DxDirectory ReadDirEntry ()
        {
            var dir = new DxDirectory();
            dir.DirOffset       = m_input.ReadInt32();
            dir.ParentDirOffset = m_input.ReadInt32();
            dir.FileCount       = m_input.ReadInt32();
            dir.FileTable       = m_input.ReadInt32();
            return dir;
        }

        protected override void ReadFileTable (string root, long table_offset)
        {
            m_input.Position = m_header.DirTable + table_offset;
            var dir = ReadDirEntry();
            if (dir.DirOffset != -1 && dir.ParentDirOffset != -1)
            {
                m_input.Position = m_header.FileTable + dir.DirOffset;
                root = Path.Combine (root, ExtractFileName (m_input.ReadUInt32()));
            }
            long current_pos = m_header.FileTable + dir.FileTable;
            for (int i = 0; i < dir.FileCount; ++i)
            {
                m_input.Position = current_pos;
                uint name_offset = m_input.ReadUInt32();
                uint attr        = m_input.ReadUInt32();
                m_input.Seek (0x18, SeekOrigin.Current);
                uint offset      = m_input.ReadUInt32();
                if (0 != (attr & 0x10)) // FILE_ATTRIBUTE_DIRECTORY
                {
                    if (0 == offset || table_offset == offset)
                        throw new InvalidFormatException ("Infinite recursion in DXA directory index");
                    ReadFileTable (root, offset);
                }
                else
                {
                    uint size       = m_input.ReadUInt32();
                    int packed_size = -1;
                    if (Version >= 2)
                        packed_size = m_input.ReadInt32();
                    var entry = FormatCatalog.Instance.Create<PackedEntry>(Path.Combine (root, ExtractFileName (name_offset)));
                    entry.Offset = m_header.BaseOffset + offset;
                    entry.UnpackedSize = size;
                    entry.IsPacked = -1 != packed_size;
                    if (entry.IsPacked)
                        entry.Size = (uint)packed_size;
                    else
                        entry.Size = size;
                    m_dir.Add (entry);
                }
                current_pos += m_entry_size;
            }
        }
    }

    internal sealed class IndexReaderV6 : IndexReader
    {
        readonly int    m_entry_size;

        public IndexReaderV6 (DxHeader header, int version, Stream input) : base (header, version, input)
        {
            m_entry_size = 0x40;
        }

        private class DxDirectory
        {
            public long DirOffset;
            public long ParentDirOffset;
            public int  FileCount;
            public long FileTable;
        }

        DxDirectory ReadDirEntry ()
        {
            var dir             = new DxDirectory();
            dir.DirOffset       = m_input.ReadInt64();
            dir.ParentDirOffset = m_input.ReadInt64();
            dir.FileCount       = (int)m_input.ReadInt64();
            dir.FileTable       = m_input.ReadInt64();
            return dir;
        }

        protected override void ReadFileTable (string root, long table_offset)
        {
            m_input.Position = m_header.DirTable + table_offset;
            var dir = ReadDirEntry();
            if (dir.DirOffset != -1 && dir.ParentDirOffset != -1)
            {
                m_input.Position = m_header.FileTable + dir.DirOffset;
                root = Path.Combine (root, ExtractFileName (m_input.ReadInt64()));
            }
            long current_pos = m_header.FileTable + dir.FileTable;
            for (int i = 0; i < dir.FileCount; ++i)
            {
                m_input.Position = current_pos;
                var name_offset = m_input.ReadInt64();
                uint attr = (uint)m_input.ReadInt64();
                m_input.Seek (0x18, SeekOrigin.Current);
                var offset = m_input.ReadInt64();
                if (0 != (attr & 0x10)) // FILE_ATTRIBUTE_DIRECTORY
                {
                    if (0 == offset || table_offset == offset)
                        throw new InvalidFormatException ("Infinite recursion in DXA directory index");
                    ReadFileTable (root, offset);
                }
                else
                {
                    var size = m_input.ReadInt64();
                    var packed_size = m_input.ReadInt64();
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (Path.Combine (root, ExtractFileName (name_offset)));
                    entry.Offset = m_header.BaseOffset + offset;
                    entry.UnpackedSize = (uint)size;
                    entry.IsPacked = -1 != packed_size;
                    if (entry.IsPacked)
                        entry.Size = (uint)packed_size;
                    else
                        entry.Size = (uint)size;
                    m_dir.Add (entry);
                }
                current_pos += m_entry_size;
            }
        }
    }

    internal class EncryptedStream : ProxyStream
    {
        private int         m_base_pos;
        private byte[]      m_key;

        public EncryptedStream (Stream stream, long base_position, byte[] key, bool leave_open = false)
            : base (stream, leave_open)
        {
            m_key = key;
            m_base_pos = m_key.Length != 0 ? (int)(base_position % m_key.Length) : 0;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            var key_pos = m_base_pos + Position;
            int read = BaseStream.Read (buffer, offset, count);
            if (read > 0 && m_key.Length != 0)
                DxOpener.Decrypt (buffer, offset, read, key_pos, m_key);
            return read;
        }

        public override int ReadByte ()
        {
            int b = BaseStream.ReadByte();
            if (m_key.Length != 0 && b != -1)
            {
                int key_pos = (int)((m_base_pos + Position - 1) % m_key.Length);
                b ^= m_key[key_pos];
            }
            return b;
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException (nameof (buffer));
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException (nameof (offset));
            if (count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException (nameof (count));

            if (m_key.Length != 0)
            {
                byte[] write_buf = new byte[count];
                int key_pos = (int)((m_base_pos + Position) % m_key.Length);
                for (int i = 0; i < count; ++i)
                {
                    write_buf[i] = (byte)(buffer[offset + i] ^ m_key[key_pos++]);
                    if (m_key.Length == key_pos)
                        key_pos = 0;
                }
                BaseStream.Write (write_buf, 0, count);
            }
            else
            {
                BaseStream.Write (buffer, offset, count);
            }
        }

        public override void WriteByte (byte value)
        {
            if (m_key.Length != 0)
            {
                int key_pos = (int)((m_base_pos + Position) % m_key.Length);
                BaseStream.WriteByte((byte)(value ^ m_key[key_pos]));
            }
            else
            {
                BaseStream.WriteByte (value);
            }
        }
    }
}
