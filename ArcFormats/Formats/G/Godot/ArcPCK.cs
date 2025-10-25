using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Godot
{
    public class GodotCFBStream : InputProxyStream
    {
        private readonly byte[] m_key;
        private readonly byte[] m_iv;
        private readonly byte[] m_keystream;
        private readonly ICryptoTransform m_transform;
        private int m_keystreamPos;

        public GodotCFBStream (Stream input, byte[] key, byte[] iv, bool leaveOpen = false)
            : base (input, leaveOpen)
        {
            m_key = key;
            m_iv = (byte[])iv.Clone();
            m_keystream = new byte[16];
            m_keystreamPos = 16;

            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Key = key;
                aes.Padding = PaddingMode.None;
                m_transform = aes.CreateEncryptor();
            }
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int totalRead = 0;

            while (count > 0)
            {
                if (m_keystreamPos >= 16)
                {
                    m_transform.TransformBlock (m_iv, 0, 16, m_keystream, 0);
                    m_keystreamPos = 0;
                }

                int b = BaseStream.ReadByte();
                if (b == -1)
                    break;

                byte cipherByte = (byte)b;
                byte plainByte = (byte)(cipherByte ^ m_keystream[m_keystreamPos]);
                buffer[offset] = plainByte;

                for (int i = 0; i < 15; i++)
                    m_iv[i] = m_iv[i + 1];
                m_iv[15] = cipherByte;

                m_keystreamPos++;
                offset++;
                count--;
                totalRead++;
            }

            return totalRead;
        }

        public override int ReadByte()
        {
            byte[] buffer = new byte[1];
            int read = Read (buffer, 0, 1);
            return read == 0 ? -1 : buffer[0];
        }

        protected override void Dispose (bool disposing)
        {
            if (disposing)
                m_transform?.Dispose();
            base.Dispose (disposing);
        }
    }

    public class GodotEncryptedStream : InputProxyStream
    {
        private readonly Stream m_decryptedStream;
        private readonly long m_size;
        private long m_position;

        public GodotEncryptedStream (Stream input, byte[] key, uint godotVersion = 4, bool leaveOpen = false)
            : base (input, leaveOpen)
        {
            var reader = new BinaryReader (input);

            if (godotVersion >= 4)
            {
                // Godot 4.x format: [optional magic] + md5 + size + iv + encrypted_data (CFB mode)
                long startPos = input.Position;
                uint firstDword = reader.ReadUInt32();
                bool hasMagic = (firstDword == 0x43454447); // "GDEC"

                if (!hasMagic)
                    input.Position = startPos;

                // MD5 hash (16 bytes)
                byte[] hash = reader.ReadBytes (16);
                m_size = reader.ReadInt64();
                byte[] iv = reader.ReadBytes (16);

                long encryptedSize = m_size;
                if (encryptedSize % 16 != 0)
                    encryptedSize += 16 - (encryptedSize % 16);

                byte[] encryptedData = new byte[encryptedSize];
                reader.Read (encryptedData, 0, (int)encryptedSize);

                byte[] decryptedData = DecryptCFB (encryptedData, key, iv);

                Array.Resize (ref decryptedData, (int)m_size);

                m_decryptedStream = new MemoryStream (decryptedData);
            }
            else
            {
                // Godot 3.x format: magic + mode + md5 + size + encrypted_data (ECB mode)
                uint magic = reader.ReadUInt32();
                if (magic != 0x43454447)
                    throw new InvalidOperationException ("Invalid encrypted file magic");

                uint mode = reader.ReadUInt32();

                // MD5 hash (16 bytes)
                byte[] hash = reader.ReadBytes (16);

                m_size = reader.ReadInt64();

                long encryptedSize = m_size;
                if (encryptedSize % 16 != 0)
                    encryptedSize += 16 - (encryptedSize % 16);

                byte[] encryptedData = new byte[encryptedSize];
                reader.Read (encryptedData, 0, (int)encryptedSize);

                byte[] decryptedData = DecryptECB (encryptedData, key);

                Array.Resize (ref decryptedData, (int)m_size);

                m_decryptedStream = new MemoryStream (decryptedData);
            }

            m_position = 0;
        }

        private byte[] DecryptECB (byte[] data, byte[] key)
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Key = key;
                aes.Padding = PaddingMode.None;

                using (var decryptor = aes.CreateDecryptor())
                {
                    byte[] result = new byte[data.Length];

                    // Decrypt block by block
                    for (int i = 0; i < data.Length; i += 16)
                        decryptor.TransformBlock (data, i, 16, result, i);

                    return result;
                }
            }
        }

        private byte[] DecryptCFB (byte[] data, byte[] key, byte[] iv)
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB; // Use ECB to implement CFB
                aes.Key = key;
                aes.Padding = PaddingMode.None;

                using (var encryptor = aes.CreateEncryptor()) // CFB uses encryptor for decryption
                {
                    byte[] result = new byte[data.Length];
                    byte[] currentIV = (byte[])iv.Clone();

                    for (int i = 0; i < data.Length; i += 16)
                    {
                        int blockSize = Math.Min (16, data.Length - i);

                        // Encrypt IV to get keystream
                        byte[] keystream = new byte[16];
                        encryptor.TransformBlock (currentIV, 0, 16, keystream, 0);

                        // XOR with ciphertext to get plaintext
                        for (int j = 0; j < blockSize; j++)
                            result[i + j] = (byte)(data[i + j] ^ keystream[j]);

                        // Update IV with ciphertext for next block
                        Buffer.BlockCopy (data, i, currentIV, 0, Math.Min (blockSize, 16));
                    }

                    return result;
                }
            }
        }

        public override long Length => m_size;

        public override long Position
        {
            get => m_position;
            set => throw new NotSupportedException();
        }

        public override bool CanSeek => false;

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = m_decryptedStream.Read (buffer, offset, count);
            m_position += read;
            return read;
        }

        protected override void Dispose (bool disposing)
        {
            if (disposing)
                m_decryptedStream?.Dispose();
            base.Dispose (disposing);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PckOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PCK/GODOT"; } }
        public override string Description { get { return "Godot Engine package"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        // PCK format constants
        private const uint PCK_MAGIC           = 0x43504447;  // 'GDPC'
        private const int HEADER_RESERVED_SIZE = 15 * 4;      // 60 bytes
        private const int MD5_SIZE             = 16;
        private const int STEX_HEADER_SIZE     = 32;
        private const int OGGSTR_HEADER_SIZE   = 279;
        private const int OGGSTR_TRAILER_SIZE  = 4;

        // Format version flags (v2+)
        private const uint PACK_DIR_ENCRYPTED  = 1;
        private const uint PACK_REL_FILEBASE   = 2;
        private const uint PACK_FILE_ENCRYPTED = 1;

        static readonly byte[] EMBEDDED_PCK_SIGNATURE = { 0x47, 0x44, 0x50, 0x43 }; // 'GDPC'

        public PckOpener()
        {
            Extensions = new string[] { "pck", "exe" };
            Signatures = new uint[] { PCK_MAGIC, 0x00905A4D, 0x464C457F, 0 }; // GDPC, MZ, ELF
        }

        // Default encryption key (can be overridden)
        private static byte[] DefaultEncryptionKey = null;

        /// <summary>
        /// Sets the encryption key for encrypted PCK files.
        /// The key should be 32 bytes for AES-256.
        /// </summary>
        public static void SetEncryptionKey (byte[] key)
        {
            if (key != null && key.Length != 32)
                throw new ArgumentException ("Encryption key must be 32 bytes for AES-256");
            DefaultEncryptionKey = key;
        }

        /// <summary>
        /// Sets the encryption key from a hexadecimal string.
        /// </summary>
        public static void SetEncryptionKeyFromHex (string hexKey)
        {
            if (string.IsNullOrEmpty (hexKey))
            {
                DefaultEncryptionKey = null;
                return;
            }

            if (hexKey.Length != 64)
                throw new ArgumentException ("Hex key must be 64 characters (32 bytes)");

            DefaultEncryptionKey = new byte[32];
            for (int i = 0; i < 32; i++)
                DefaultEncryptionKey[i] = Convert.ToByte (hexKey.Substring (i * 2, 2), 16);
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint signature = file.View.ReadUInt32 (0);
            if (signature == PCK_MAGIC)
                return ReadPckArchive (file, 0);

            long offset = ExeFile.FindSignatureInOverlay (file, EMBEDDED_PCK_SIGNATURE, validator: SignatureValidation.AlwaysValid);
            if (offset > 0)
                return ReadPckArchive (file, offset);

            offset = ExeFile.FindSignatureInSection (file, EMBEDDED_PCK_SIGNATURE, "pck", validator: SignatureValidation.AlwaysValid);
            if (offset > 0)
                return ReadPckArchive (file, offset);


            //TODO: this allows finding it in more files but also loads the system, must be sure to use it
            //offset = ExeFile.AutoFindSignature (file, EMBEDDED_PCK_SIGNATURE, validator: SignatureValidation.AlwaysValid);
            //if (offset > 0)
                //return ReadPckArchive (file, offset);

            if (signature == 0x464C457F) // ELF
            {
                var section = new ExeFile.Section { Offset = 0, Size = (uint)file.MaxOffset };
                offset = ExeFile.FindStringReversed (file, section, EMBEDDED_PCK_SIGNATURE, 1);
                if (offset > 0)
                    return ReadPckArchive (file, offset);
            }

            return null;
        }

        private ArcFile ReadPckArchive (ArcView file, long baseOffset)
        {
            using (var input = file.CreateStream (baseOffset))
            using (var reader = new BinaryReader (input, Encoding.UTF8))
            {
                var header = ReadPckHeader (reader);
                if (header == null)
                    return null;

                if ((header.Flags & PACK_DIR_ENCRYPTED) != 0 && DefaultEncryptionKey == null)
                    throw new InvalidOperationException ("PCK directory is encrypted but no encryption key was provided");

                var entries = ReadPckEntries (reader, header, baseOffset);
                return new ArcFile (file, this, entries);
            }
        }

        private PckHeader ReadPckHeader (BinaryReader reader)
        {
            var header = new PckHeader
            {
                Magic         = reader.ReadUInt32(),
                FormatVersion = reader.ReadUInt32(),
                VersionMajor  = reader.ReadUInt32(),
                VersionMinor  = reader.ReadUInt32(),
                VersionPatch  = reader.ReadUInt32()
            };

            if (header.Magic != PCK_MAGIC)
                return null;

            if (header.FormatVersion >= 2)
            {
                header.Flags = reader.ReadUInt32();
                header.FileBase = reader.ReadInt64();

                if (header.FormatVersion >= 3)
                    header.DirectoryOffset = reader.ReadInt64();

                // skip 16 reserved dwords
                reader.BaseStream.Seek (16 * 4, SeekOrigin.Current);
            }
            else
            {
                // Version 0-1: Skip 16 reserved dwords (64 bytes)
                reader.BaseStream.Seek (16 * 4, SeekOrigin.Current);
                header.Flags = 0;
                header.FileBase = 0;
                header.DirectoryOffset = 0;
            }

            // File count comes after everything
            header.FileCount = reader.ReadUInt32();

            return header;
        }

        private List<Entry> ReadPckEntries (BinaryReader reader, PckHeader header, long baseOffset)
        {
            var entries = new List<Entry>((int)header.FileCount);

            // For v3+, we need to seek to the directory offset first
            if (header.FormatVersion >= 3 && header.DirectoryOffset > 0)
            {
                reader.BaseStream.Seek (baseOffset + header.DirectoryOffset, SeekOrigin.Begin);
                // Re-read file count at directory position
                header.FileCount = reader.ReadUInt32();
            }

            // Handle encrypted directory
            Stream directoryStream = reader.BaseStream;
            BinaryReader directoryReader = reader;

            if ((header.Flags & PACK_DIR_ENCRYPTED) != 0 && DefaultEncryptionKey != null)
            {
                // Read the first 16 bytes as IV for directory encryption
                byte[] iv = reader.ReadBytes (16);

                // Directory encryption with CFB is only in Godot 4.x (PCK format v2+)
                if (header.VersionMajor >= 4 || (header.VersionMajor == 0 && header.FormatVersion >= 2))
                    directoryStream = new GodotCFBStream (reader.BaseStream, DefaultEncryptionKey, iv, true);

                directoryReader = new BinaryReader (directoryStream, Encoding.UTF8);
            }

            for (uint i = 0; i < header.FileCount; i++)
            {
                var entry = ReadPckEntry (directoryReader, header, baseOffset);
                if (entry != null)
                {
                    entry.EncryptionKey = DefaultEncryptionKey;
                    entry.GodotVersion = header.VersionMajor > 0 ? header.VersionMajor : 4;
                    entries.Add (entry);
                }
            }

            if (directoryStream != reader.BaseStream)
                directoryStream.Dispose();

            return entries;
        }

        private PckEntry ReadPckEntry (BinaryReader reader, PckHeader header, long baseOffset)
        {
            // Read path length
            uint pathLength = reader.ReadUInt32();
            if (pathLength == 0 || pathLength > 1024) // Sanity check
                return null;

            // Path length includes padding to 4-byte boundary
            uint paddedLength = (pathLength + 3) & ~3u;

            byte[] pathBytes = reader.ReadBytes((int)paddedLength);
            string path = Encoding.UTF8.GetString (pathBytes, 0, (int)pathLength).TrimEnd('\0');

            // Remove "res://" prefix if present
            if (path.StartsWith ("res://"))
                path = path.Substring (6);

            // Read file offset and size
            long offset = reader.ReadInt64();
            long size = reader.ReadInt64();

            // Skip MD5 hash (16 bytes)
            reader.BaseStream.Seek (MD5_SIZE, SeekOrigin.Current);

            // Read flags for v2+ only
            uint fileFlags = 0;
            if (header.FormatVersion >= 2)
            {
                fileFlags = reader.ReadUInt32();
            }

            // Calculate actual offset based on version
            if (header.FormatVersion <= 1)
            {
                // Godot 3.x and earlier: direct offset + baseOffset
                offset = offset + baseOffset;
            }
            else
            {
                // Godot 4.x: file_base + offset + baseOffset
                offset = header.FileBase + offset + baseOffset;
            }

            var entry = new PckEntry
            {
                Name = path,
                Type = FormatCatalog.Instance.GetTypeFromName (path),
                Offset = offset,
                Size = (uint)size,
                IsPacked = false,
                IsEncrypted = (fileFlags & PACK_FILE_ENCRYPTED) != 0,
                GodotVersion = header.VersionMajor > 0 ? header.VersionMajor : 4
            };

            // Detect special file types
            DetectSpecialFileType (entry);

            return entry;
        }

        private void DetectSpecialFileType (PckEntry entry)
        {
            string lowerPath = entry.Name.ToLowerInvariant();
            string extension = Path.GetExtension (lowerPath);

            switch (extension)
            {
                case ".stex":
                case ".ctex":
                    entry.IsStex = true;
                    entry.ConvertToImage = true;
                    break;

                case ".oggstr":
                    entry.IsOggStr = true;
                    break;

                case ".sample":
                    entry.IsSample = true;
                    break;
            }
        }


        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pckEntry = entry as PckEntry;
            if (pckEntry == null)
                return base.OpenEntry (arc, entry);

            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);

            if (pckEntry.IsEncrypted)
            {
                if (pckEntry.EncryptionKey == null)
                    throw new InvalidOperationException ("File is encrypted but no encryption key was provided");

                uint godotVersion = pckEntry.GodotVersion > 0 ? pckEntry.GodotVersion : 4;
                input = new GodotEncryptedStream (input, pckEntry.EncryptionKey, godotVersion, false);
            }

            try
            {
                string extension = Path.GetExtension (entry.Name).ToLowerInvariant();

                switch (extension)
                {
                case ".stex":
                case ".ctex":
                    return ExtractStexImage (input, entry);

                case ".oggstr":
                    return ExtractOggStream (input, entry);

                case ".mp3str":
                    return ExtractMp3Stream (input, entry);

                case ".sample":
                    return ExtractSample (input, entry);
                default:
                    return input;
            }
            }
            catch
            {
                input?.Dispose();
                throw;
                }

        }

       private Stream ExtractStexImage (Stream input, Entry entry)
        {
            var pckEntry = entry as PckEntry;
            uint godotVersion = pckEntry?.GodotVersion ?? 3;

            var reader = new BinaryReader (input);

            if (godotVersion >= 4)
                return ExtractCtexImageV4 (reader, entry);
            else
                return ExtractStexImageV3 (reader, entry);
        }

        private Stream ExtractCtexImageV4 (BinaryReader reader, Entry entry)
        {
            // Format: GST2 + version + width + height + flags + mipmap_limit + reserved (3) + data_format + ...

            byte[] magic = reader.ReadBytes (4);
            if (!(magic[0] == 'G' && magic[1] == 'S' && magic[2] == 'T' && magic[3] == '2'))
            {
                reader.BaseStream.Seek (0, SeekOrigin.Begin);
                return reader.BaseStream;
            }

            uint version = reader.ReadUInt32();
            uint width = reader.ReadUInt32();
            uint height = reader.ReadUInt32();
            uint flags = reader.ReadUInt32();
            uint mipmapLimit = reader.ReadUInt32();

            // Skip 3 reserved dwords (12 bytes)
            reader.ReadUInt32();
            reader.ReadUInt32();
            reader.ReadUInt32();

            // Read data format
            uint dataFormat = reader.ReadUInt32();

            // CompressedTexture2D::DataFormat enum
            const uint DATA_FORMAT_IMAGE = 0;
            const uint DATA_FORMAT_PNG = 1;
            const uint DATA_FORMAT_WEBP = 2;
            //const uint DATA_FORMAT_BASIS_UNIVERSAL = 3;

            if (dataFormat == DATA_FORMAT_PNG || dataFormat == DATA_FORMAT_WEBP)
            {
                // These formats store: width (2) + height (2) + mipmap_count (4) + format (4) + data_size (4) + data
                ushort imgWidth = reader.ReadUInt16();
                ushort imgHeight = reader.ReadUInt16();
                uint mipmapCount = reader.ReadUInt32();
                uint imgFormat = reader.ReadUInt32();

                // Read first mipmap (the full image)
                uint dataSize = reader.ReadUInt32();
                byte[] imageData = reader.ReadBytes((int)dataSize);

                reader.BaseStream.Dispose();

                var stream = new BinMemoryStream (imageData, entry.Name);
                stream.Position = 0;
                return stream;
            }
            else if (dataFormat == DATA_FORMAT_IMAGE)
            {
                // Raw image data format
                ushort imgWidth = reader.ReadUInt16();
                ushort imgHeight = reader.ReadUInt16();
                uint mipmapCount = reader.ReadUInt32();
                uint imgFormat = reader.ReadUInt32();

                // Read all the image data (this is compressed texture data, not extractable)
                var remainingSize = entry.Size - reader.BaseStream.Position;
                byte[] imageData = reader.ReadBytes((int)remainingSize);

                reader.BaseStream.Dispose();

                var stream = new BinMemoryStream (imageData, entry.Name);
                stream.Position = 0;
                return stream;
            }

            // Other formats - return as-is
            reader.BaseStream.Seek (0, SeekOrigin.Begin);
            return reader.BaseStream;
        }

        private Stream ExtractStexImageV3 (BinaryReader reader, Entry entry)
        {
            // Godot 3 STEX format: GDST magic + header
            byte[] magic = reader.ReadBytes (4);
            if (!(magic[0] == 'G' && magic[1] == 'D' && magic[2] == 'S' && magic[3] == 'T'))
            {
                reader.BaseStream.Seek (0, SeekOrigin.Begin);
                return reader.BaseStream;
            }

            ushort width = reader.ReadUInt16();
            ushort widthPo2 = reader.ReadUInt16();
            ushort height = reader.ReadUInt16();
            ushort heightPo2 = reader.ReadUInt16();
            uint flags = reader.ReadUInt32();
            uint format = reader.ReadUInt32();

            // Format bits for Godot 3.6
            const uint FORMAT_BIT_PNG = 1 << 20;
            const uint FORMAT_BIT_WEBP = 1 << 21;
            const uint FORMAT_BIT_LOSSLESS = 1 << 20;  // Godot 3.2
            const uint FORMAT_BIT_LOSSY = 1 << 21;      // Godot 3.2

            bool isPNG = (format & FORMAT_BIT_PNG) != 0;
            bool isWebP = (format & FORMAT_BIT_WEBP) != 0;
            bool isLossless = (format & FORMAT_BIT_LOSSLESS) != 0;
            bool isLossy = (format & FORMAT_BIT_LOSSY) != 0;

            if (isPNG || isWebP || isLossless || isLossy)
            {
                // Compressed format
                uint mipmapCount = reader.ReadUInt32();

                // Read first mipmap (the full image)
                uint dataSize = reader.ReadUInt32();
                reader.ReadBytes (4); // format name
                byte[] imageData = reader.ReadBytes((int)dataSize-4);

                reader.BaseStream.Dispose();
                return new BinMemoryStream (imageData, entry.Name);
            }

            // VRAM compressed or uncompressed - return as-is
            reader.BaseStream.Seek (0, SeekOrigin.Begin);
            return reader.BaseStream;
        }

        private class RSRCParser
        {
            private BinaryReader reader;
            private bool bigEndian;
            private uint formatVersion;
            private uint versionMajor;
            private List<string> stringTable;

            // Godot 3 variant types
            private const uint VARIANT_NIL = 1;
            private const uint VARIANT_BOOL = 2;
            private const uint VARIANT_INT = 3;
            private const uint VARIANT_REAL = 4;  // Godot 3
            private const uint VARIANT_FLOAT = 4; // Godot 4
            private const uint VARIANT_STRING = 5;
            private const uint VARIANT_RAW_ARRAY = 31;        // Godot 3
            private const uint VARIANT_PACKED_BYTE_ARRAY = 31; // Godot 4
            private const uint VARIANT_INT64 = 40;
            private const uint VARIANT_DOUBLE = 41;

            public RSRCParser (Stream input)
            {
                reader = new BinaryReader (input, Encoding.UTF8);
                stringTable = new List<string>();
            }

            public Dictionary<string, object> Parse()
            {
                // Read header
                byte[] magic = reader.ReadBytes (4);
                if (!(magic[0] == 'R' && magic[1] == 'S' && magic[2] == 'R' && magic[3] == 'C'))
                    return null;

                bigEndian = reader.ReadUInt32() != 0;
                bool useReal64 = reader.ReadUInt32() != 0;
                versionMajor = reader.ReadUInt32();
                uint verMinor = reader.ReadUInt32();
                formatVersion = reader.ReadUInt32();

                string resourceType = ReadUnicodeString();
                long metadataOffset = reader.ReadInt64();

                if (versionMajor >= 4)
                {
                    // Godot 4 format
                    uint flags = reader.ReadUInt32();
                    reader.ReadInt64(); // UID

                    if (formatVersion >= 5 && (flags & 8) != 0)
                        ReadUnicodeString(); // script class

                    // 11 reserved fields
                    for (int i = 0; i < 11; i++)
                        reader.ReadUInt32();
                }
                else
                {
                    // Godot 3 format - 14 reserved fields
                    for (int i = 0; i < 14; i++)
                        reader.ReadUInt32();
                }

                // Read string table
                uint stringCount = reader.ReadUInt32();
                for (uint i = 0; i < stringCount; i++)
                {
                    stringTable.Add (ReadUnicodeString());
                }

                // Skip external resources
                uint extResourceCount = reader.ReadUInt32();
                for (uint i = 0; i < extResourceCount; i++)
                {
                    ReadUnicodeString(); // type
                    ReadUnicodeString(); // path
                }

                // Skip internal resources
                uint intResourceCount = reader.ReadUInt32();
                for (uint i = 0; i < intResourceCount; i++)
                {
                    ReadUnicodeString(); // path
                    reader.ReadInt64(); // offset
                }

                // Now at main resource data
                string resType = ReadUnicodeString();
                uint propertyCount = reader.ReadUInt32();

                var properties = new Dictionary<string, object>();
                properties["__type__"] = resourceType;
                properties["__version__"] = versionMajor;

                for (uint i = 0; i < propertyCount; i++)
                {
                    uint nameIdx = reader.ReadUInt32();
                    string propName = nameIdx < stringTable.Count ? stringTable[(int)nameIdx] : "";
                    object value = ParseVariant();

                    if (value != null)
                        properties[propName] = value;
                }

                return properties;
            }

            private object ParseVariant()
            {
                uint type = reader.ReadUInt32();

                switch (type)
                {
                    case VARIANT_NIL:
                        return null;

                    case VARIANT_BOOL:
                        return reader.ReadUInt32() != 0;

                    case VARIANT_INT:
                        return reader.ReadInt32();

                    case VARIANT_INT64:
                        return reader.ReadInt64();

                    case VARIANT_REAL: // VARIANT_FLOAT in Godot 4
                        return reader.ReadSingle();

                    case VARIANT_DOUBLE:
                        return reader.ReadDouble();

                    case VARIANT_STRING:
                        return ReadUnicodeString();

                    case VARIANT_RAW_ARRAY: // VARIANT_PACKED_BYTE_ARRAY in Godot 4
                        uint length = reader.ReadUInt32();
                        byte[] data = reader.ReadBytes((int)length);
                        // Align to 4 bytes
                        int padding = (4 - (int)(length % 4)) % 4;
                        if (padding > 0)
                            reader.ReadBytes (padding);
                        return data;

                    default:
                        // Skip unknown variant types
                        return null;
                }
            }

            private string ReadUnicodeString()
            {
                uint length = reader.ReadUInt32();
                if (length == 0) return string.Empty;

                if ((length & 0x80000000) != 0)
                {
                    // String from string table
                    length &= 0x7FFFFFFF;
                }

                byte[] bytes = reader.ReadBytes((int)length);
                int nullIndex = Array.IndexOf (bytes, (byte)0);
                if (nullIndex >= 0)
                    return Encoding.UTF8.GetString (bytes, 0, nullIndex);
                return Encoding.UTF8.GetString (bytes);
            }
        }

        // Updated ExtractSample to handle both versions
        private Stream ExtractSample (Stream input, Entry entry)
        {
            var parser = new RSRCParser (input);
            var properties = parser.Parse();

            if (properties != null && properties.ContainsKey ("data"))
            {
                byte[] audioData = properties["data"] as byte[];
                if (audioData != null)
                {
                    // Get version to handle differences
                    uint version = properties.ContainsKey ("__version__") ? 
                        Convert.ToUInt32 (properties["__version__"]) : 3;

                    // Get audio properties
                    int format = properties.ContainsKey ("format") ? 
                        Convert.ToInt32 (properties["format"]) : 1;
                    bool stereo = properties.ContainsKey ("stereo") ? 
                        (bool)properties["stereo"] : false;
                    uint sampleRate = properties.ContainsKey ("mix_rate") ? 
                        Convert.ToUInt32 (properties["mix_rate"]) : 44100;

                    input.Dispose();
                    return CreateWavStream (audioData, format, stereo, sampleRate, entry.Name);
                }
            }

            input.Position = 0;
            return input;
        }

        private Stream ExtractOggStream (Stream input, Entry entry)
        {
            // Check if it's Godot 3 format first
            const int OGGSTR_HEADER_SIZE = 279;
            const int OGGSTR_TRAILER_SIZE = 4;

            if (entry.Size > OGGSTR_HEADER_SIZE + OGGSTR_TRAILER_SIZE)
            {
                input.Seek (OGGSTR_HEADER_SIZE, SeekOrigin.Begin);
                byte[] check = new byte[4];
                input.Read (check, 0, 4);

                if (check[0] == 'O' && check[1] == 'g' && check[2] == 'g' && check[3] == 'S')
                {
                    // Godot 3 format
                    input.Seek (OGGSTR_HEADER_SIZE, SeekOrigin.Begin);
                    var oggSize = entry.Size - OGGSTR_HEADER_SIZE - OGGSTR_TRAILER_SIZE;
                    var oggData = new byte[oggSize];
                    input.Read (oggData, 0, (int)oggSize);
                    input.Dispose();
                    return new BinMemoryStream (oggData, entry.Name);
                }
            }

            // Try RSRC format (Godot 4)
            input.Position = 0;
            var parser = new RSRCParser (input);
            var properties = parser.Parse();

            if (properties != null && properties.ContainsKey ("data"))
            {
                byte[] oggData = properties["data"] as byte[];
                if (oggData != null)
                {
                    input.Dispose();
                    return new BinMemoryStream (oggData, entry.Name);
                }
            }

            input.Position = 0;
            return input;
        }

        private Stream ExtractMp3Stream (Stream input, Entry entry)
        {
            var parser = new RSRCParser (input);
            var properties = parser.Parse();

            if (properties != null && properties.ContainsKey ("data"))
            {
                byte[] mp3Data = properties["data"] as byte[];
                if (mp3Data != null)
                {
                    input.Dispose();
                    return new BinMemoryStream (mp3Data, entry.Name);
                }
            }

            input.Position = 0;
            return input;
        }

        private Stream CreateWavStream (byte[] audioData, int format, bool isStereo, uint sampleRate, string fileName)
        {
            // WAV file structure
            int channels = isStereo ? 2 : 1;
            int bitsPerSample = format == 0 ? 8 : 16; // FORMAT_8_BITS = 0, FORMAT_16_BITS = 1
            int bytesPerSample = bitsPerSample / 8;
            int blockAlign = channels * bytesPerSample;
            int byteRate = (int)sampleRate * blockAlign;

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter (ms))
            {
                // RIFF header
                writer.Write (Encoding.ASCII.GetBytes ("RIFF"));
                writer.Write ((uint)(36 + audioData.Length)); // File size - 8
                writer.Write (Encoding.ASCII.GetBytes ("WAVE"));

                // fmt chunk
                writer.Write (Encoding.ASCII.GetBytes ("fmt "));
                writer.Write (16u); // Chunk size
                writer.Write ((ushort)1); // PCM format
                writer.Write ((ushort)channels);
                writer.Write (sampleRate);
                writer.Write ((uint)byteRate);
                writer.Write ((ushort)blockAlign);
                writer.Write ((ushort)bitsPerSample);

                // data chunk
                writer.Write (Encoding.ASCII.GetBytes ("data"));
                writer.Write ((uint)audioData.Length);
                writer.Write (audioData);

                ms.Position = 0;
                var wavData = ms.ToArray();
                return new BinMemoryStream (wavData, fileName);
            }
        }
        private class PckHeader
        {
            public uint           Magic { get; set; }
            public uint   FormatVersion { get; set; }
            public uint    VersionMajor { get; set; }
            public uint    VersionMinor { get; set; }
            public uint    VersionPatch { get; set; }
            public uint           Flags { get; set; }
            public long        FileBase { get; set; }
            public long DirectoryOffset { get; set; }
            public uint       FileCount { get; set; }
        }
    }

    internal class PckEntry : PackedEntry
    {
        public bool          IsStex { get; set; }
        public bool        IsOggStr { get; set; }
        public bool        IsSample { get; set; }
        public bool  ConvertToImage { get; set; }
        public byte[] EncryptionKey { get; set; }
        public uint    GodotVersion { get; set; }
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "STEX")]
    [ExportMetadata("Target",    "PNG")]
    public class StexFormat : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "CTEX")]
    [ExportMetadata("Target",    "PNG")]
    public class CtexFormatP : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "CTEX")]
    [ExportMetadata("Target",    "WEBP")]
    public class CtexFormatW : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "OGGSTR")]
    [ExportMetadata("Target",    "OGG")]
    public class OggStrFormat : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "OGGVORBISSTR")]
    [ExportMetadata("Target", "OGG")]
    public class OggVorbisStrFormat : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "MP3STR")]
    [ExportMetadata("Target",    "MP3")]
    public class Mp3StrFormat : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "SAMPLE")]
    [ExportMetadata("Target",    "WAV")]
    public class SampleFormat : ResourceAlias { }
}