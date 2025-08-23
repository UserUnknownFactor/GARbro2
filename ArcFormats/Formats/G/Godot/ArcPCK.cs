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
    public class GodotAesStream : InputProxyStream
    {
        private readonly ICryptoTransform m_transform;
        private readonly           byte[] m_buffer;
        private                       int m_bufferPos;
        private                       int m_bufferLen;

        public GodotAesStream (Stream input, byte[] key, byte[] iv, bool leaveOpen = false)
            : base (input, leaveOpen)
        {
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CFB;
                aes.Padding = PaddingMode.None;
                aes.FeedbackSize = 128; // CFB128
                aes.Key = key;
                aes.IV = iv;
                m_transform = aes.CreateDecryptor();
            }
            m_buffer = new byte[4096];
            m_bufferPos = 0;
            m_bufferLen = 0;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int totalRead = 0;

            while (count > 0)
            {
                if (m_bufferPos >= m_bufferLen)
                {
                    // Refill buffer
                    m_bufferLen = BaseStream.Read (m_buffer, 0, m_buffer.Length);
                    if (m_bufferLen == 0)
                        break;

                    // Decrypt in-place
                    m_transform.TransformBlock (m_buffer, 0, m_bufferLen, m_buffer, 0);
                    m_bufferPos = 0;
                }

                int available = m_bufferLen - m_bufferPos;
                int toRead = Math.Min (available, count);
                Array.Copy (m_buffer, m_bufferPos, buffer, offset, toRead);

                m_bufferPos += toRead;
                offset      += toRead;
                count       -= toRead;
                totalRead   += toRead;
            }

            return totalRead;
        }

        protected override void Dispose (bool disposing)
        {
            if (disposing)
                m_transform?.Dispose();
            base.Dispose (disposing);
        }
    }

    /// <summary>
    /// Stream for reading Godot encrypted files.
    /// Format: 8 bytes size + 16 bytes IV + 16 bytes hash + encrypted data
    /// </summary>
    public class GodotEncryptedFileStream : Stream
    {
        private readonly         Stream m_baseStream;
        private readonly GodotAesStream m_cryptoStream;
        private readonly           long m_size;
        private                    long m_position;
        private readonly           bool m_leaveOpen;

        public GodotEncryptedFileStream (Stream input, byte[] key, bool leaveOpen = false)
        {
            m_baseStream = input;
            m_leaveOpen = leaveOpen;

            // Read encrypted file header
            var reader = new BinaryReader (input);
            m_size      = reader.ReadInt64 ();
            byte[] iv   = reader.ReadBytes (16);
            byte[] hash = reader.ReadBytes (16); // MD5 hash, not verified for now

            m_cryptoStream = new GodotAesStream (input, key, iv, true);
            m_position = 0;
        }

        public override bool CanRead  => true;
        public override bool CanSeek  => false;
        public override bool CanWrite => false;
        public override long   Length => m_size;
        public override long Position
        {
            get => m_position;
            set => throw new NotSupportedException();
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            if (m_position >= m_size)
                return 0;

            int toRead = (int)Math.Min (count, m_size - m_position);
            int read = m_cryptoStream.Read (buffer, offset, toRead);
            m_position += read;
            return read;
        }

        public override void Flush () { }
        public override long Seek (long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength (long value) => throw new NotSupportedException();
        public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose (bool disposing)
        {
            if (disposing)
            {
                m_cryptoStream?.Dispose();
                if (!m_leaveOpen)
                    m_baseStream?.Dispose();
            }
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
                // Read the first 16 bytes as IV
                byte[] iv = reader.ReadBytes (16);

                // Create decryption stream for the directory
                directoryStream = new GodotAesStream (reader.BaseStream, DefaultEncryptionKey, iv, true);
                directoryReader = new BinaryReader (directoryStream, Encoding.UTF8);
            }

            for (uint i = 0; i < header.FileCount; i++)
            {
                var entry = ReadPckEntry (directoryReader, header, baseOffset);
                if (entry != null)
                {
                    entry.EncryptionKey = DefaultEncryptionKey;
                    entries.Add (entry);
                }
            }

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

            // Calculate actual offset based on version and flags
            if (header.FormatVersion < 2)
            {
                // v0-1: offset is absolute from start of PCK
                offset += baseOffset;
            }
            else if ((header.Flags & PACK_REL_FILEBASE) != 0)
            {
                // v2+ with relative file base: offset is relative to file base
                offset += baseOffset + header.FileBase;
            }
            else
            {
                // v2+ without relative file base: offset is from pack start
                offset += baseOffset;
            }

            var entry = new PckEntry
            {
                Name = path,
                Type = FormatCatalog.Instance.GetTypeFromName (path),
                Offset = offset,
                Size = (uint)size,
                IsPacked = false,
                IsEncrypted = (fileFlags & PACK_FILE_ENCRYPTED) != 0
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
                    // Check if it's an image that needs conversion
                    if (lowerPath.Contains (".png") ||
                        lowerPath.Contains (".jpg") ||
                        lowerPath.Contains (".jpeg") ||
                        lowerPath.Contains (".webp"))
                    {
                        entry.ConvertToImage = true;
                    }
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

            Stream input;

            if (pckEntry.IsEncrypted)
            {
                if (pckEntry.EncryptionKey == null)
                    throw new InvalidOperationException ("File is encrypted but no encryption key was provided");

                // Create encrypted file stream
                var encStream = arc.File.CreateStream (entry.Offset, entry.Size);
                input = new GodotEncryptedFileStream (encStream, pckEntry.EncryptionKey);
            }
            else
            {
                input = arc.File.CreateStream (entry.Offset, entry.Size);
            }

            try
            {
                if (pckEntry.ConvertToImage && pckEntry.IsStex)
                {
                    return ExtractStexImage (input, entry);
                }
                else if (pckEntry.IsOggStr)
                {
                    return ExtractOggStream (input, entry);
                }
                else if (pckEntry.IsSample)
                {
                    return ExtractSample (input, entry);
                }

                return input;
            }
            catch
            {
                input?.Dispose();
                throw;
            }
        }

        private Stream ExtractStexImage (Stream input, Entry entry)
        {
            // STEX format: 32-byte header followed by image data
            if (entry.Size <= STEX_HEADER_SIZE)
                return input;

            input.Seek (STEX_HEADER_SIZE, SeekOrigin.Begin);

            var imageSize = entry.Size - STEX_HEADER_SIZE;
            var imageData = new byte[imageSize];
            input.Read (imageData, 0, (int)imageSize);

            return new BinMemoryStream (imageData, entry.Name);
        }

        private Stream ExtractOggStream (Stream input, Entry entry)
        {
            // OggStr format: 279-byte header, OGG data, 4-byte trailer
            if (entry.Size <= OGGSTR_HEADER_SIZE + OGGSTR_TRAILER_SIZE)
                return input;

            input.Seek (OGGSTR_HEADER_SIZE, SeekOrigin.Begin);

            var oggSize = entry.Size - OGGSTR_HEADER_SIZE - OGGSTR_TRAILER_SIZE;
            var oggData = new byte[oggSize];
            input.Read (oggData, 0, (int)oggSize);

            return new BinMemoryStream (oggData, entry.Name);
        }

        private Stream ExtractSample (Stream input, Entry entry)
        {
            // Sample format: 16-byte header followed by audio data
            const int SAMPLE_HEADER_SIZE = 16;
            if (entry.Size <= SAMPLE_HEADER_SIZE)
                return input;

            using (var reader = new BinaryReader (input, Encoding.UTF8, true))
            {
                // Read sample header
                uint format = reader.ReadUInt32();
                uint stereo = reader.ReadUInt32();
                uint length = reader.ReadUInt32();
                uint sampleRate = reader.ReadUInt32();

                // Read audio data
                var audioSize = entry.Size - SAMPLE_HEADER_SIZE;
                var audioData = new byte[audioSize];
                input.Read (audioData, 0, (int)audioSize);

                return new BinMemoryStream (audioData, entry.Name);
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
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "STEX")]
    [ExportMetadata("Target",    "PNG")]
    public class StexFormat : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "CTEX")]
    [ExportMetadata("Target",    "PNG")]
    public class CtexFormat : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "OGGSTR")]
    [ExportMetadata("Target",    "OGG")]
    public class OggStrFormat : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "SAMPLE")]
    [ExportMetadata("Target",    "WAV")]
    public class SampleFormat : ResourceAlias { }
}