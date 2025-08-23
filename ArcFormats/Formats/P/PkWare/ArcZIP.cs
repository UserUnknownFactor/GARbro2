using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using SharpZip = ICSharpCode.SharpZipLib.Zip;

namespace GameRes.Formats.PkWare
{
    public class ZipEntry : PackedEntry
    {
        public readonly SharpZip.ZipEntry NativeEntry;

        public ZipEntry (SharpZip.ZipEntry zip_entry)
        {
            NativeEntry = zip_entry;
            Name = zip_entry.Name;
            Type = FormatCatalog.Instance.GetTypeFromName (zip_entry.Name);
            IsPacked = true;
            // design decision of having 32bit entry sizes was made early during GameRes
            // library development. nevertheless, large files will be extracted correctly
            // despite the fact that size is reported as uint.MaxValue, because extraction is
            // performed by .Net framework based on real size value.
            Size = (uint)Math.Min (zip_entry.CompressedSize, uint.MaxValue);
            UnpackedSize = (uint)Math.Min (zip_entry.Size, uint.MaxValue);
            Offset = zip_entry.Offset;
        }
    }

    public class ZipPkEntry
    {
        public string           Name { get; set; }
        public long           Offset { get; set; }
        public uint   CompressedSize { get; set; }
        public uint UncompressedSize { get; set; }
        public int CompressionMethod { get; set; }
        public int   LocalHeaderSize { get; set; }
    }

    public class ZipPkStream : Stream
    {
        private readonly Stream m_stream;
        private readonly   bool m_leave_open;
        private            long m_position;

        public ZipPkStream (Stream stream, bool leaveOpen = true)
        {
            m_stream     = stream ?? throw new ArgumentNullException (nameof (stream));
            m_leave_open = leaveOpen;
            m_position   = stream.Position;
        }

        public override bool  CanRead => m_stream.CanRead;
        public override bool  CanSeek => m_stream.CanSeek;
        public override bool CanWrite => false;
        public override long   Length => m_stream.Length;
        public override long Position
        {
            get => m_stream.Position;
            set => m_stream.Position = value;
        }

        public List<ZipPkEntry> ReadCentralDirectory()
        {
            var entries = new List<ZipPkEntry>();

            var eocdPos = FindEndOfCentralDirectory();
            if (eocdPos < 0)
                return entries;

            m_stream.Position = eocdPos;
            var reader = new BinaryReader (m_stream, Encoding.UTF8, true);

            // Read EOCD
            uint signature = reader.ReadUInt32();
            if (signature != 0x06054b50)
                return entries;

            ushort diskNumber     = reader.ReadUInt16();
            ushort centralDirDisk = reader.ReadUInt16();
            ushort entriesOnDisk  = reader.ReadUInt16();
            ushort totalEntries   = reader.ReadUInt16();
            uint centralDirSize   = reader.ReadUInt32();
            uint centralDirOffset = reader.ReadUInt32();
            ushort commentLength  = reader.ReadUInt16();

            m_stream.Position = centralDirOffset;

            for (int i = 0; i < totalEntries; i++)
            {
                var entry = ReadCentralDirectoryEntry (reader);
                if (entry != null)
                    entries.Add (entry);
            }

            return entries;
        }

        private ZipPkEntry ReadCentralDirectoryEntry (BinaryReader reader)
        {
            uint signature = reader.ReadUInt32();
            if (signature != 0x02014b50)
                return null;

            reader.ReadUInt16(); // version made by
            reader.ReadUInt16(); // version needed
            ushort flags = reader.ReadUInt16();
            ushort compressionMethod = reader.ReadUInt16();
            reader.ReadUInt32(); // last mod time/date
            reader.ReadUInt32(); // crc32
            uint compressedSize = reader.ReadUInt32();
            uint uncompressedSize = reader.ReadUInt32();
            ushort fileNameLength = reader.ReadUInt16();
            ushort extraFieldLength = reader.ReadUInt16();
            ushort commentLength = reader.ReadUInt16();
            reader.ReadUInt16(); // disk number start
            reader.ReadUInt16(); // internal attributes
            reader.ReadUInt32(); // external attributes
            uint localHeaderOffset = reader.ReadUInt32();

            byte[] nameBytes = reader.ReadBytes (fileNameLength);
            string name = Encoding.UTF8.GetString (nameBytes);

            if (extraFieldLength > 0)
                reader.ReadBytes (extraFieldLength);
            if (commentLength > 0)
                reader.ReadBytes (commentLength);

            // Calculate local header size
            long savedPos = m_stream.Position;
            m_stream.Position = localHeaderOffset;
            int localHeaderSize = CalculateLocalHeaderSize (reader);
            m_stream.Position = savedPos;

            return new ZipPkEntry
            {
                Name = name,
                Offset = localHeaderOffset,
                CompressedSize = compressedSize,
                UncompressedSize = uncompressedSize,
                CompressionMethod = compressionMethod,
                LocalHeaderSize = localHeaderSize
            };
        }

        private int CalculateLocalHeaderSize (BinaryReader reader)
        {
            uint signature = reader.ReadUInt32();
            if (signature != 0x04034b50)
                return 30; // default size

            reader.ReadUInt16(); // version
            reader.ReadUInt16(); // flags
            reader.ReadUInt16(); // compression
            reader.ReadUInt32(); // time/date
            reader.ReadUInt32(); // crc32
            reader.ReadUInt32(); // compressed size
            reader.ReadUInt32(); // uncompressed size
            ushort fileNameLength = reader.ReadUInt16();
            ushort extraFieldLength = reader.ReadUInt16();

            return 30 + fileNameLength + extraFieldLength;
        }

        private long FindEndOfCentralDirectory()
        {
            const int maxCommentSize = 65535;
            const int eocdSize = 22;
            long searchStart = Math.Max (0, m_stream.Length - maxCommentSize - eocdSize);

            m_stream.Position = searchStart;
            byte[] buffer = new byte[m_stream.Length - searchStart];
            m_stream.Read (buffer, 0, buffer.Length);

            for (int i = buffer.Length - eocdSize; i >= 0; i--)
            {
                if (buffer[i] == 0x50 && buffer[i + 1] == 0x4b &&
                    buffer[i + 2] == 0x05 && buffer[i + 3] == 0x06)
                {
                    return searchStart + i;
                }
            }

            return -1;
        }

        public override void Flush() => m_stream.Flush();

        public override int Read (byte[] buffer, int offset, int count)
            => m_stream.Read (buffer, offset, count);

        public override long Seek (long offset, SeekOrigin origin)
            => m_stream.Seek (offset, origin);

        public override void SetLength (long value)
            => throw new NotSupportedException();

        public override void Write (byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        protected override void Dispose (bool disposing)
        {
            if (disposing && !m_leave_open)
            {
                m_stream?.Dispose();
            }
            base.Dispose (disposing);
        }
    }

    internal class PkZipArchive : ArcFile
    {
        readonly SharpZip.ZipFile m_zip;

        public   SharpZip.ZipFile Native { get { return m_zip; } }

        public PkZipArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, SharpZip.ZipFile native)
            : base (arc, impl, dir)
        {
            m_zip = native;
        }

        #region IDisposable implementation
        bool _zip_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!_zip_disposed)
            {
                if (disposing)
                    m_zip.Close();
                _zip_disposed = true;
            }
            base.Dispose (disposing);
        }
        #endregion
    }

    [Serializable]
    public class ZipScheme : ResourceScheme
    {
        public Dictionary<string, string> KnownKeys;
    }

    [Export(typeof(ArchiveFormat))]
    public class ZipOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ZIP"; } }
        public override string Description { get { return "PKWARE archive format"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  true; } }

        static readonly byte[] PkDirSignature = { (byte)'P', (byte)'K', 5, 6 };

        public ZipOpener ()
        {
            Settings = new[] { ZipEncoding };
            Extensions = new string[] { "zip" };
        }

        EncodingSetting ZipEncoding = new EncodingSetting ("ZIPEncodingCP", "DefaultEncoding");

        public override ArcFile TryOpen (ArcView file)
        {
            if (-1 == SearchForSignature (file, PkDirSignature))
                return null;
            var input = file.CreateStream();
            try
            {
                return OpenZipArchive (file, input);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        internal ArcFile OpenZipArchive (ArcView file, Stream input)
        {
            var zip = new SharpZip.ZipFile (input);
            zip.StringCodec = SharpZip.StringCodec.FromCodePage (Properties.Settings.Default.ZIPEncodingCP);
            try
            {
                var files = zip.Cast<SharpZip.ZipEntry>().Where (z => !z.IsDirectory);
                bool has_encrypted = files.Any (z => z.IsCrypted);
                if (has_encrypted)
                    zip.Password = QueryPassword (file);
                var dir = files.Select (z => new ZipEntry (z) as Entry).ToList();
                return new PkZipArchive (file, this, dir, zip);
            }
            catch
            {
                zip.Close();
                throw;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var zarc = (PkZipArchive)arc;
            var zent = (ZipEntry)entry;
            return zarc.Native.GetInputStream (zent.NativeEntry);
        }

        /// <summary>
        /// Search for ZIP 'End of central directory record' near the end of file.
        /// Returns offset of 'PK' signature or -1 if no signature was found.
        /// </summary>
        internal unsafe long SearchForSignature (ArcView file, byte[] signature)
        {
            if (signature.Length < 4)
                throw new ArgumentException ("Invalid ZIP file signature", "signature");

            uint tail_size = (uint)Math.Min (file.MaxOffset, 0x10016L);
            if (tail_size < 0x16)
                return -1;
            var start_offset = file.MaxOffset - tail_size;
            using (var view = file.CreateViewAccessor (start_offset, tail_size))
            using (var pointer = new ViewPointer (view, start_offset))
            {
                byte* ptr_end = pointer.Value;
                byte* ptr = ptr_end + tail_size - 0x16;
                for (; ptr >= ptr_end; --ptr)
                {
                    if (signature[3] == ptr[3] && signature[2] == ptr[2] &&
                        signature[1] == ptr[1] && signature[0] == ptr[0])
                        return start_offset + (ptr - ptr_end);
                }
                return -1;
            }
        }

        string QueryPassword (ArcView file)
        {
            var options = Query<ZipOptions>(Localization._T ("ZIPEncryptedNotice"));
            return options.Password;
        }

        public override ResourceOptions GetDefaultOptions()
        {
            return new ZipOptions
            {
                CompressionLevel = Properties.Settings.Default.ZIPCompression,
                FileNameEncoding = ZipEncoding.Get<Encoding>(),
                Password = Properties.Settings.Default.ZIPPassword,
            };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            if (widget is GUI.WidgetZIP)
                Properties.Settings.Default.ZIPPassword = ((GUI.WidgetZIP)widget).Password.Text;
            return GetDefaultOptions();
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetZIP (DefaultScheme.KnownKeys);
        }

        public override void Create(
            Stream output, IEnumerable<Entry> list, 
            ResourceOptions options,EntryCallback callback
        )
        {
            var zip_options = GetOptions<ZipOptions> (options);
            int callback_count = 0;
            using (var zip = new ZipArchive (output, ZipArchiveMode.Create, true, zip_options.FileNameEncoding))
            {
                foreach (var entry in list)
                {
                    var zip_entry = zip.CreateEntry (entry.Name, zip_options.CompressionLevel);
                    using (var input = File.OpenRead (entry.Name))
                    using (var zip_file = zip_entry.Open())
                    {
                        if (null != callback)
                            callback (++callback_count, entry, Localization._T ("MsgAddingFile"));
                        input.CopyTo (zip_file);
                    }
                }
            }
        }

        ZipScheme DefaultScheme = new ZipScheme { 
            KnownKeys = new Dictionary<string, string>() 
        };

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (ZipScheme)value; }
        }
    }

    public class ZipOptions : ResourceOptions
    {
        public CompressionLevel CompressionLevel { get; set; }
        public         Encoding FileNameEncoding { get; set; }
        public           string         Password { get; set; }
    }
}
