using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GameRes.Formats.NScripter
{
    [Export(typeof(ArchiveFormat))]
    public class Ns2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "NS2"; } }
        public override string Description { get { return Localization._T ("NSADescription"); } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public static Dictionary<string, string> KnownKeys = new Dictionary<string, string>();

        public override ResourceScheme Scheme
        {
            get { return new NsaScheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((NsaScheme)value).KnownKeys; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            List<Entry> dir = null;
            uint data_offset = file.View.ReadUInt32 (0);
            if (data_offset > 4 && data_offset < file.MaxOffset)
            {
                try
                {
                    using (var input = file.CreateStream())
                    {
                        dir = ReadIndex (input);
                        if (null != dir)
                            return new ArcFile (file, this, dir);
                    }
                }
                catch { /* ignore parse errors */ }
            }
            if (!file.Name.HasExtension (".ns2"))
                return null;

            var password = QueryPassword();
            if (string.IsNullOrEmpty (password))
                return null;
            var key = Encoding.ASCII.GetBytes (password);

            using (var input = OpenEncryptedStream (file, key))
            {
                dir = ReadIndex (input);
                if (null == dir)
                    return null;
                return new NsaEncryptedArchive (file, this, dir, key);
            }
        }

        protected List<Entry> ReadIndex (Stream file)
        {
            using (var input = new BinaryReader (file, Encodings.cp932, true))
            {
                uint base_offset = input.ReadUInt32();
                if (base_offset <= 4 || base_offset >= file.Length)
                    return null;

                var name_buffer = new char[0x100];
                long current_offset = base_offset;
                var dir = new List<Entry>();
                while (file.Position < base_offset)
                {
                    if (input.ReadChar() != '"')
                        break;
                    char c;
                    int i = 0;
                    while ((c = input.ReadChar()) != '"')
                    {
                        if (name_buffer.Length == i)
                            return null;
                        name_buffer[i++] = c;
                    }
                    if (0 == i)
                        return null;
                    var name = new string (name_buffer, 0, i);
                    var entry = new Entry {
                        Name   = name,
                        Offset = current_offset,
                        Size   = input.ReadUInt32(),
                        Type   = FormatCatalog.Instance.GetTypeFromName (name)
                    };
                    if (!entry.CheckPlacement (file.Length))
                        return null;

                    current_offset += entry.Size;
                    dir.Add (entry);
                }
                return dir.Count > 0 ? dir : null;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var nsa_arc = arc as NsaEncryptedArchive;
            if (null == nsa_arc)
            {
                return arc.File.CreateStream (entry.Offset, entry.Size);
            }
            var encrypted = OpenEncryptedStream (arc.File, nsa_arc.Key);
            return new StreamRegion (encrypted, entry.Offset, entry.Size);
        }

        Stream OpenEncryptedStream (ArcView file, byte[] key)
        {
            if (key.Length < 96)
                return new EncryptedViewStream (file, key);
            else
                return new Ns2Stream (file, key);
        }

        private string QueryPassword ()
        {
            var options = Query<NsaOptions> (Localization._T ("ArcEncryptedNotice"));
            return options.Password;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new NsaOptions { Password = Properties.Settings.Default.NSAPassword };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            var w = widget as GUI.WidgetNSA;
            if (null != w)
                Properties.Settings.Default.NSAPassword = w.Password.Text;
            return GetDefaultOptions();
        }

        public override void Create(Stream output, IEnumerable<Entry> list, 
                                                  ResourceOptions options, EntryCallback callback)
        {
            var ns2_options = GetOptions<NsaOptions>(options);
            var encoding = Encodings.cp932.WithFatalFallback();
            byte[] key = null;

            if (!string.IsNullOrEmpty(ns2_options.Password))
            {
                key = Encoding.ASCII.GetBytes(ns2_options.Password);
            }

            var entries = list.ToArray<Entry>();

            long header_size = 4; // base offset
            foreach (var entry in entries)
            {
                try
                {
                    var name_bytes = encoding.GetBytes(entry.Name);
                    header_size += 2 + name_bytes.Length + 4; // quotes + name + size
                }
                catch (EncoderFallbackException X)
                {
                    throw new InvalidFileName(entry.Name, Localization._T ("MsgIllegalCharacters"), X);
                }
            }

            // Align to 16 bytes if encrypted
            if (key != null && key.Length >= 96)
                header_size = (header_size + 15) & ~15;

            output.Position = header_size;
            long current_offset = (uint)header_size;

            foreach (var entry in entries)
            {
                if (null != callback)
                    callback(entries.Length, entry, Localization._T ("MsgAddingFile"));

                using (var input = File.OpenRead(entry.Name))
                {
                    var file_size = input.Length;
                    if (file_size > uint.MaxValue)
                        throw new FileSizeException();

                    entry.Offset = current_offset;
                    entry.Size = (uint)file_size;
                    current_offset += entry.Size;

                    if (key != null)
                        CopyEncrypted(input, output, key);
                    else
                        input.CopyTo(output);
                }
            }

            output.Position = 0;
            using (var header = new BinaryWriter(output, encoding, true))
            {
                header.Write((uint)header_size);

                foreach (var entry in entries)
                {
                    header.Write((byte)'"');
                    header.Write(encoding.GetBytes(entry.Name));
                    header.Write((byte)'"');
                    header.Write(entry.Size);
                }

                if (key != null && key.Length >= 96)
                {
                    long padding = header_size - output.Position;
                    if (padding > 0)
                        header.Write(new byte[padding]);
                }
            }

            if (key != null)
                EncryptArchive(output, key, header_size);
        }

        private void CopyEncrypted(Stream input, Stream output, byte[] key)
        {
            if (key.Length < 96)
            {
                var buffer = new byte[4096];
                int key_pos = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i++)
                        buffer[i] ^= key[key_pos++ % key.Length];
                    output.Write(buffer, 0, read);
                }
            }
            else
            {
                input.CopyTo(output);
            }
        }

        private void EncryptArchive(Stream output, byte[] key, long header_size)
        {
            if (key.Length < 96)
                return; // this encryption already applied during copy

            const int BlockSize = 32;
            var md5 = new Cryptography.MD5();
            var seed = new byte[64];
            var temp = new byte[32];
            var hash = new byte[16];
            var block = new byte[BlockSize];

            output.Position = 0;
            long remaining = output.Length;

            using (var temp_output = new MemoryStream())
            {
                while (remaining >= BlockSize)
                {
                    output.Read(block, 0, BlockSize);

                    // Encrypt block
                    int key1 = 0;
                    int key2 = 48;

                    // First half
                    Buffer.BlockCopy(block, 16, seed, 0, 16);
                    Buffer.BlockCopy(key, key2, seed, 16, 48);

                    md5.Initialize();
                    md5.Update(seed, 0, seed.Length);
                    Buffer.BlockCopy(md5.State, 0, hash, 0, 16);

                    for (int j = 0; j < 16; j++)
                    {
                        temp[16 + j] = (byte)(hash[j] ^ block[j]);
                    }

                    // Second half  
                    Buffer.BlockCopy(temp, 16, seed, 0, 16);
                    Buffer.BlockCopy(key, key1, seed, 16, 48);

                    md5.Initialize();
                    md5.Update(seed, 0, seed.Length);
                    Buffer.BlockCopy(md5.State, 0, hash, 0, 16);

                    for (int j = 0; j < 16; j++)
                    {
                        temp[j] = (byte)(hash[j] ^ block[16 + j]);
                    }

                    // Final round
                    Buffer.BlockCopy(temp, 0, seed, 0, 16);
                    Buffer.BlockCopy(key, key1, seed, 16, 48);

                    md5.Initialize();
                    md5.Update(seed, 0, seed.Length);
                    Buffer.BlockCopy(md5.State, 0, hash, 0, 16);

                    temp_output.Write(temp, 16, 16);
                    for (int j = 0; j < 16; j++)
                    {
                        temp_output.WriteByte((byte)(hash[j] ^ temp[j]));
                    }

                    remaining -= BlockSize;
                }

                // Copy any remaining bytes
                if (remaining > 0)
                {
                    var remainder = new byte[remaining];
                    output.Read(remainder, 0, (int)remaining);
                    temp_output.Write(remainder, 0, (int)remaining);
                }

                output.Position = 0;
                temp_output.Position = 0;
                temp_output.CopyTo(output);
            }
        }


        public override object GetAccessWidget ()
        {
            return new GUI.WidgetNSA (KnownKeys);
        }
    }

    internal class Ns2Stream : ViewStreamBase
    {
        byte[]          m_key;

        readonly Cryptography.MD5 MD5 = new Cryptography.MD5();

        const int BlockSize   = 32;

        public Ns2Stream (ArcView mmap, byte[] key) : base (mmap)
        {
            m_key = key;
        }

        byte[] m_seed = new byte[64];

        protected override void DecryptBlock ()
        {
            var temp = new byte[32];
            var hash = new byte[16];
            for (int src = 0; src < m_current_block_length; src += BlockSize)
            {
                int src2 = src + 16;
                int key1 = 0; // within m_key
                int key2 = 48;

                Buffer.BlockCopy (m_current_block, src2, m_seed, 0,  16);
                Buffer.BlockCopy (m_key,           key1, m_seed, 16, 48);

                MD5.Initialize();
                MD5.Update (m_seed, 0, m_seed.Length);
                Buffer.BlockCopy (MD5.State, 0, hash, 0, 16);

                for (int j = 0; j < 16; ++j)
                {
                    temp[j] = m_seed[j] = (byte)(hash[j] ^ m_current_block[src + j]);
                }

                Buffer.BlockCopy (m_key, key2, m_seed, 16, 48);

                MD5.Initialize();
                MD5.Update (m_seed, 0, m_seed.Length);
                Buffer.BlockCopy (MD5.State, 0, hash, 0, 16);

                for (int j = 0; j < 16; ++j)
                {
                    temp[16 + j] = m_seed[j] = (byte)(hash[j] ^ m_current_block[src2 + j]);
                }

                Buffer.BlockCopy (m_key, key1, m_seed, 16, 48);

                MD5.Initialize();
                MD5.Update (m_seed, 0, m_seed.Length);
                Buffer.BlockCopy (MD5.State, 0, hash, 0, 16);

                Buffer.BlockCopy (temp, 16, m_current_block, src, 16);
                for (int j = 0; j < 16; ++j)
                {
                    m_current_block[src2 + j] = (byte)(hash[j] ^ temp[j]);
                }
            }
        }
    }
}
