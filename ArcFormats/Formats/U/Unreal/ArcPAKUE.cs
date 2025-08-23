using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.UnrealEngine
{
    internal class PakEntry : PackedEntry
    {
        public       long CompressedSize { get; set; }
        public     uint? CompressionSlot { get; set; }
        public                byte Flags { get; set; }
        public uint CompressionBlockSize { get; set; }
        public        List<Block> Blocks { get; set; }
        public               byte[] Hash { get; set; }

        public new bool IsEncrypted => (Flags & 1) != 0;
        public       bool IsDeleted => ((Flags >> 1) & 1) != 0;

        public new long Size
        {
            get { return Math.Min(UnpackedSize, uint.MaxValue); }
            set { base.Size = value; }
        }
    }

    internal class Block
    {
        public long Start { get; set; }
        public long   End { get; set; }
    }

    internal enum CompressionType
    {
        None = 0,
        Zlib,
        Gzip,
        Oodle,
        Zstd,
        LZ4
    }

    internal enum VersionMajor : uint
    {
        Unknown               = 0,
        Initial               = 1,
        NoTimestamps          = 2,
        CompressionEncryption = 3,
        IndexEncryption       = 4,
        RelativeChunkOffsets  = 5,
        DeleteRecords         = 6,
        EncryptionKeyGuid     = 7,
        FNameBasedCompression = 8,
        FrozenIndex           = 9,
        PathHashIndex         = 10,
        Fnv64BugFix           = 11
    }

    internal enum UEVersion
    {
        V0, V1, V2, V3, V4, V5, V6, V7, V8A, V8B, V9, V10, V11
    }

    public class PakOptions : ResourceOptions
    {
        public string Password { get; set; }
    }

    [Serializable]
    public class PakScheme : ResourceScheme
    {
        public Dictionary<string, string> KnownKeys { get; set; }

        public PakScheme()
        {
            KnownKeys = new Dictionary<string, string>
            {
                // Add known AES keys here (base64 encoded or hex)
                // Example: { "GameName", "base64_encoded_256bit_key" }
            };
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/UE"; } }
        public override string Description { get { return "Unreal Engine PAK archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        const uint Magic = 0x5A6F12E1;
        
        static PakScheme DefaultScheme = new PakScheme();

        public PakOpener()
        {
            Extensions = new string[] { "pak", "dat", "bin" };
            ContainedFormats = new string[] { "UEXP/UE" };
        }

        public override ResourceScheme Scheme 
        { 
            get { return DefaultScheme; }
            set { DefaultScheme = (PakScheme)value; }
        }

        public override ArcFile TryOpen(ArcView file)
        {
            byte[] aesKey = null;
            foreach (var version in GetVersions())
            {
                try
                {
                    var pak = ReadPak(file, version, aesKey);
                    if (pak != null)
                        return pak;
                }
                catch (EncryptedException)
                {
                    if (aesKey == null)
                    {
                        aesKey = GetEncryptionKey(file);
                        if (aesKey == null)
                            return null;
                    }
                    
                    try
                    {
                        var pak = ReadPak(file, version, aesKey);
                        if (pak != null)
                        {
                            var keyString = Convert.ToBase64String(aesKey);
                            WidgetPassword.MarkPasswordAsSuccessful(this.Tag, keyString);
                            return pak;
                        }
                    }
                    catch { }
                }
                catch { }
            }
            return null;
        }

        private byte[] GetEncryptionKey(ArcView file)
        {
            var scheme = Scheme as PakScheme;
            if (scheme?.KnownKeys != null)
            {
                foreach (var kvp in scheme.KnownKeys)
                {
                    try
                    {
                        var keyBytes = Convert.FromBase64String(kvp.Value);
                        if (keyBytes.Length == 32) // 256-bit key
                        {
                            // Quick test if this key works
                            foreach (var version in GetVersions())
                            {
                                try
                                {
                                    var pak = ReadPak(file, version, keyBytes);
                                    if (pak != null)
                                        return keyBytes;
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }

            var options = Query<PakOptions>(Localization._T ("ArcEncryptedNotice"));
            if (options == null || string.IsNullOrEmpty(options.Password))
                return null;

            // Try to parse as base64 or hex
            return ParseKey(options.Password);
        }

        private byte[] ParseKey(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            // Try base64 first
            try
            {
                var bytes = Convert.FromBase64String(input);
                if (bytes.Length == 32) // 256-bit AES key
                    return bytes;
            }
            catch { }

            try
            {
                input = input.Replace(" ", "").Replace("-", "").Replace("0x", "");
                if (input.Length == 64) // 32 bytes * 2 hex chars
                {
                    var bytes = new byte[32];
                    for (int i = 0; i < 32; i++)
                        bytes[i] = Convert.ToByte(input.Substring(i * 2, 2), 16);
                    return bytes;
                }
            }
            catch { }

            return null;
        }

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
                return new PakOptions
                {
                    Password = passwordWidget.Password
                };
            }
            return GetDefaultOptions();
        }

        public override ResourceOptions GetDefaultOptions()
        {
            return new PakOptions 
            { 
                Password = Properties.Settings.Default.PakUE_Password 
            };
        }

        private IEnumerable<UEVersion> GetVersions()
        {
            return new[] { UEVersion.V11, UEVersion.V10, UEVersion.V9, UEVersion.V8B, UEVersion.V8A, 
                          UEVersion.V7, UEVersion.V6, UEVersion.V5, UEVersion.V4, UEVersion.V3 };
        }

        private ArcFile ReadPak(ArcView file, UEVersion version, byte[] aesKey)
        {
            var footerSize = GetVersionSize(version);

            var footer = ReadFooter(file, version);
            if (footer.Magic != Magic)
                return null;

            var indexData = file.View.ReadBytes(footer.IndexOffset, (uint)footer.IndexSize);

            if (footer.IsEncrypted)
            {
                if (aesKey == null)
                    throw new EncryptedException();

                DecryptData(indexData, aesKey);
            }

            using (var indexStream = new BinMemoryStream(indexData))
            {
                var mountPoint = ReadString(indexStream);
                var index = ReadIndex(indexStream, version, footer, aesKey, file); // Pass file here

                if (index.Count == 0)
                    return null;

                // Store key for later use in OpenEntry
                var arc = new PakArchive(file, this, index);
                arc.AesKey = aesKey;
                return arc;
            }
        }

        private void DecryptData(byte[] data, byte[] aesKey)
        {
            using (var aes = System.Security.Cryptography.Aes.Create())
            {
                aes.Key = aesKey;
                aes.Mode = System.Security.Cryptography.CipherMode.ECB;
                aes.Padding = System.Security.Cryptography.PaddingMode.None;
                
                using (var decryptor = aes.CreateDecryptor())
                {
                    for (int i = 0; i < data.Length; i += 16)
                    {
                        int blockSize = Math.Min(16, data.Length - i);
                        if (blockSize == 16)
                            decryptor.TransformBlock(data, i, 16, data, i);
                    }
                }
            }
        }

        private Footer ReadFooter(ArcView file, UEVersion version)
        {
            var footer = new Footer();
            var versionMajor = GetVersionMajor(version);
            var footerSize = GetVersionSize(version);

            long offset = file.MaxOffset - footerSize;

            if (versionMajor >= VersionMajor.EncryptionKeyGuid)
            {
                footer.EncryptionUuid = file.View.ReadBytes(offset, 16);
                offset += 16;
            }

            if (versionMajor >= VersionMajor.IndexEncryption)
            {
                footer.IsEncrypted = file.View.ReadByte(offset) != 0;
                offset += 1;
            }

            footer.Magic = file.View.ReadUInt32(offset);
            offset += 4;
            footer.VersionMajor = (VersionMajor)file.View.ReadUInt32(offset);
            offset += 4;
            footer.IndexOffset = file.View.ReadInt64(offset);
            offset += 8;
            footer.IndexSize = file.View.ReadInt64(offset);
            offset += 8;
            footer.Hash = file.View.ReadBytes(offset, 20);
            offset += 20;

            if (versionMajor == VersionMajor.FrozenIndex)
            {
                footer.IsFrozen = file.View.ReadByte(offset) != 0;
                offset += 1;
            }

            int compressionCount = GetCompressionCount(version);
            footer.CompressionMethods = new List<CompressionType?>();

            for (int i = 0; i < compressionCount; i++)
            {
                var nameBytes = file.View.ReadBytes(offset, 32);
                offset += 32;
                var name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

                if (!string.IsNullOrEmpty(name))
                {
                    if (Enum.TryParse<CompressionType>(name, true, out var compression))
                        footer.CompressionMethods.Add(compression);
                    else
                        footer.CompressionMethods.Add(null);
                }
                else
                {
                    footer.CompressionMethods.Add(null);
                }
            }

            // Add default compression methods for older versions
            if (versionMajor < VersionMajor.FNameBasedCompression)
            {
                footer.CompressionMethods.Add(CompressionType.Zlib);
                footer.CompressionMethods.Add(CompressionType.Gzip);
                footer.CompressionMethods.Add(CompressionType.Oodle);
            }

            return footer;
        }

        private List<Entry> ReadIndex(IBinaryStream stream, UEVersion version, Footer footer, byte[] aesKey, ArcView file)
        {
            var versionMajor = GetVersionMajor(version);
            var entries = new List<Entry>();

            if (versionMajor >= VersionMajor.PathHashIndex)
            {
                entries = ReadIndexV10(stream, version, footer, aesKey, file);
            }
            else
            {
                uint count = stream.ReadUInt32();
                for (uint i = 0; i < count; i++)
                {
                    var path = ReadString(stream);
                    var entry = ReadEntry(stream, version);
                    entry.Name = path;
                    entries.Add(entry);
                }
            }

            return entries;
        }

        private List<Entry> ReadIndexV10(IBinaryStream stream, UEVersion version, Footer footer, byte[] aesKey, ArcView file)
        {
            var entries = new List<Entry>();
            uint recordCount = stream.ReadUInt32();
            ulong pathHashSeed = stream.ReadUInt64();

            bool hasPathHashIndex = stream.ReadUInt32() != 0;
            long pathHashIndexOffset = 0;
            long pathHashIndexSize = 0;
            if (hasPathHashIndex)
            {
                pathHashIndexOffset = stream.ReadInt64();
                pathHashIndexSize = stream.ReadInt64();
                stream.ReadBytes(20); // hash
            }

            bool hasFullDirIndex = stream.ReadUInt32() != 0;
            long fullDirIndexOffset = 0;
            long fullDirIndexSize = 0;
            if (hasFullDirIndex)
            {
                fullDirIndexOffset = stream.ReadInt64();
                fullDirIndexSize = stream.ReadInt64();
                stream.ReadBytes(20); // hash
            }

            uint encodedSize = stream.ReadUInt32();
            var encodedEntries = stream.ReadBytes((int)encodedSize);

            uint nonEncodedCount = stream.ReadUInt32();
            var nonEncodedEntries = new List<PakEntry>();
            for (uint i = 0; i < nonEncodedCount; i++)
            {
                nonEncodedEntries.Add(ReadEntry(stream, version));
            }

            // Build entries from full directory index
            if (hasFullDirIndex && fullDirIndexSize > 0)
            {
                // Read from the file at the specified offset
                var fullDirIndexData = file.View.ReadBytes(fullDirIndexOffset, (uint)fullDirIndexSize);

                if (footer.IsEncrypted && aesKey != null)
                {
                    DecryptData(fullDirIndexData, aesKey);
                }

                using (var fdiStream = new BinMemoryStream(fullDirIndexData))
                {
                    var fullDirIndex = ReadFullDirectoryIndex(fdiStream);

                    using (var encodedStream = new BinMemoryStream(encodedEntries))
                    {
                        foreach (var dir in fullDirIndex)
                        {
                            foreach (var fileName in dir.Value)
                            {
                                PakEntry entry;
                                if (fileName.Value >= 0)
                                {
                                    encodedStream.Position = fileName.Value;
                                    entry = ReadEncodedEntry(encodedStream, version);
                                }
                                else
                                {
                                    int index = (-fileName.Value) - 1;
                                    if (index >= 0 && index < nonEncodedEntries.Count)
                                        entry = nonEncodedEntries[index];
                                    else
                                        continue; // Skip invalid index
                                }

                                var dirName = dir.Key.TrimStart('/');
                                var path = dirName + fileName.Key;
                                entry.Name = path;
                                entries.Add(entry);
                            }
                        }
                    }
                }
            }

            return entries;
        }

        private Dictionary<string, Dictionary<string, int>> ReadFullDirectoryIndex(IBinaryStream stream)
        {
            var result = new Dictionary<string, Dictionary<string, int>>();
            uint dirCount = stream.ReadUInt32();
            
            for (uint i = 0; i < dirCount; i++)
            {
                var dirName = ReadString(stream);
                uint fileCount = stream.ReadUInt32();
                var files = new Dictionary<string, int>();
                
                for (uint j = 0; j < fileCount; j++)
                {
                    var fileName = ReadString(stream);
                    int offset = stream.ReadInt32();
                    files[fileName] = offset;
                }
                
                result[dirName] = files;
            }
            
            return result;
        }

        private PakEntry ReadEntry(IBinaryStream stream, UEVersion version)
        {
            var entry = new PakEntry();
            var versionMajor = GetVersionMajor(version);

            entry.Offset = stream.ReadInt64();
            entry.CompressedSize = stream.ReadInt64();
            entry.UnpackedSize = (long)stream.ReadUInt64();

            // Make sure UnpackedSize is properly set
            if (entry.UnpackedSize == 0 && entry.CompressedSize > 0)
                entry.UnpackedSize = (long)entry.CompressedSize;

            uint compression = version == UEVersion.V8A ?
                (uint)stream.ReadByte() : stream.ReadUInt32();
            entry.CompressionSlot = compression > 0 ? compression - 1 : (uint?)null;

            if (versionMajor == VersionMajor.Initial)
            {
                stream.ReadInt64(); // timestamp
            }

            entry.Hash = stream.ReadBytes(20);

            if (versionMajor >= VersionMajor.CompressionEncryption && entry.CompressionSlot.HasValue)
            {
                uint blockCount = stream.ReadUInt32();
                entry.Blocks = new List<Block>();
                for (uint i = 0; i < blockCount; i++)
                {
                    entry.Blocks.Add(new Block
                    {
                        Start = stream.ReadInt64(),
                        End = stream.ReadInt64()
                    });
                }
            }

            if (versionMajor >= VersionMajor.CompressionEncryption)
            {
                entry.Flags = (byte)stream.ReadByte();
                entry.CompressionBlockSize = stream.ReadUInt32();
            }

            if (entry.CompressionSlot.HasValue)
                entry.IsPacked = true;

            entry.Size = Math.Min(entry.UnpackedSize, uint.MaxValue);

            return entry;
        }

        private PakEntry ReadEncodedEntry(IBinaryStream stream, UEVersion version)
        {
            var entry = new PakEntry();
            uint bits = stream.ReadUInt32();

            uint compression = (bits >> 23) & 0x3f;
            entry.CompressionSlot = compression > 0 ? compression - 1 : (uint?)null;

            bool encrypted = (bits & (1 << 22)) != 0;
            uint blockCount = (bits >> 6) & 0xffff;
            uint blockSizeBits = bits & 0x3f;

            if (blockSizeBits == 0x3f)
                entry.CompressionBlockSize = stream.ReadUInt32();
            else
                entry.CompressionBlockSize = blockSizeBits << 11;

            //System.Diagnostics.Trace.WriteLine($"ReadEncodedEntry: blockSizeBits={blockSizeBits:X}, CompressionBlockSize={entry.CompressionBlockSize:X}, blockCount={blockCount}");

            bool is32BitOffset = (bits & (1u << 31)) != 0;
            bool is32BitUncompressed = (bits & (1u << 30)) != 0;
            bool is32BitCompressed = (bits & (1u << 29)) != 0;

            entry.Offset = (long)(is32BitOffset ? stream.ReadUInt32() : stream.ReadInt64());
            entry.UnpackedSize = (long)(is32BitUncompressed ? stream.ReadUInt32() : stream.ReadUInt64());

            if (entry.CompressionSlot.HasValue)
                entry.CompressedSize = is32BitCompressed ? stream.ReadUInt32() : stream.ReadInt64();
            else
                entry.CompressedSize = entry.UnpackedSize;

            //System.Diagnostics.Trace.WriteLine($"ReadEncodedEntry: Offset={entry.Offset:X}, UnpackedSize={entry.UnpackedSize}, CompressedSize={entry.CompressedSize}");

            if (blockCount > 0 && (entry.CompressionSlot.HasValue || encrypted))
            {
                entry.Blocks = new List<Block>();

                if (blockCount == 1 && !encrypted)
                {
                    // Single unencrypted block
                    entry.Blocks.Add(new Block
                    {
                        Start = 0,
                        End = entry.CompressedSize
                    });
                }
                else
                {
                    // Multiple blocks or encrypted - read block sizes
                    long offset = 0;
                    for (uint i = 0; i < blockCount; i++)
                    {
                        long blockSize = stream.ReadUInt32();
                        entry.Blocks.Add(new Block
                        {
                            Start = offset,
                            End = offset + blockSize
                        });
                        //if (encrypted)
                            //blockSize = (blockSize + 15) & ~15; // align for encryption
                        offset += blockSize;
                    }
                }
            }

            entry.Flags = encrypted ? (byte)1 : (byte)0;
            entry.IsPacked = entry.CompressionSlot.HasValue;
            entry.Size = Math.Min(entry.UnpackedSize, uint.MaxValue);

            return entry;
        }

        private string ReadString(IBinaryStream stream)
        {
            int length = stream.ReadInt32();
            if (length < 0)
            {
                // UTF-16
                length = -length;
                var chars = new char[length];
                for (int i = 0; i < length; i++)
                {
                    chars[i] = (char)stream.ReadUInt16();
                }
                return new string(chars).TrimEnd('\0');
            }
            else if (length > 0)
            {
                // ASCII/UTF-8
                var bytes = stream.ReadBytes(length);
                return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
            }
            return string.Empty;
        }

        private long GetVersionSize(UEVersion version)
        {
            long size = 4 + 4 + 8 + 8 + 20; // magic + version + offset + size + hash
            var versionMajor = GetVersionMajor(version);
            
            if (versionMajor >= VersionMajor.EncryptionKeyGuid)
                size += 16;
            if (versionMajor >= VersionMajor.IndexEncryption)
                size += 1;
            if (versionMajor == VersionMajor.FrozenIndex)
                size += 1;
            if (version >= UEVersion.V8A)
                size += 32 * 4;
            if (version >= UEVersion.V8B)
                size += 32;
                
            return size;
        }

        private VersionMajor GetVersionMajor(UEVersion version)
        {
            switch (version)
            {
            case UEVersion.V0:  return VersionMajor.Unknown;
            case UEVersion.V1:  return VersionMajor.Initial;
            case UEVersion.V2:  return VersionMajor.NoTimestamps;
            case UEVersion.V3:  return VersionMajor.CompressionEncryption;
            case UEVersion.V4:  return VersionMajor.IndexEncryption;
            case UEVersion.V5:  return VersionMajor.RelativeChunkOffsets;
            case UEVersion.V6:  return VersionMajor.DeleteRecords;
            case UEVersion.V7:  return VersionMajor.EncryptionKeyGuid;
            case UEVersion.V8A:
            case UEVersion.V8B: return VersionMajor.FNameBasedCompression;
            case UEVersion.V9:  return VersionMajor.FrozenIndex;
            case UEVersion.V10: return VersionMajor.PathHashIndex;
            case UEVersion.V11: return VersionMajor.Fnv64BugFix;
            default:            return VersionMajor.Unknown;
            }
        }

        private int GetCompressionCount(UEVersion version)
        {
            if (version < UEVersion.V8A) return 0;
            if (version < UEVersion.V8B) return 4;
            return 5;
        }

        private long GetEntrySerializedSize(UEVersion version, uint? compression, uint blockCount)
        {
            long size = 8 + 8 + 8; // offset + compressed + uncompressed
            size += version == UEVersion.V8A ? 1 : 4; // compression
            if (GetVersionMajor(version) == VersionMajor.Initial)
                size += 8; // timestamp
            size += 20; // hash
            if (compression.HasValue)
                size += 4 + (8 + 8) * blockCount; // blocks
            size += 1; // flags
            if (GetVersionMajor(version) >= VersionMajor.CompressionEncryption)
                size += 4; // block size
            return size;
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var pakArchive = arc as PakArchive;
            var pakEntry = entry as PakEntry;
            if (pakEntry == null || pakArchive == null)
                return base.OpenEntry(arc, entry);

            if (pakEntry.UnpackedSize == 0)
                return Stream.Null;

            if (pakEntry.IsEncrypted && pakArchive.AesKey == null)
                throw new InvalidOperationException("Entry is encrypted but no key available");

            // Calculate data offset - skip entry header
            var version = GetVersionFromArc(arc);
            var dataOffset = pakEntry.Offset + GetEntrySerializedSize(
                version,
                pakEntry.CompressionSlot,
                (uint)(pakEntry.Blocks?.Count ?? 0));

            var dataSize = pakEntry.IsEncrypted ?
                ((pakEntry.CompressedSize + 15) & ~15) :
                pakEntry.CompressedSize;

            if (dataOffset < 0 || dataSize <= 0)
                return Stream.Null;

            var compressedData = arc.File.View.ReadBytes(dataOffset, (uint)dataSize);

            if (pakEntry.IsEncrypted)
            {
                DecryptData(compressedData, pakArchive.AesKey);
                // Trim to actual size after decryption
                if (compressedData.Length > pakEntry.CompressedSize)
                {
                    var trimmed = new byte[pakEntry.CompressedSize];
                    Array.Copy(compressedData, trimmed, pakEntry.CompressedSize);
                    compressedData = trimmed;
                }
            }

            if (!pakEntry.CompressionSlot.HasValue)
                return new BinMemoryStream(compressedData);

            var footer = ReadFooterForDecompression(arc.File);
            var compressionType = footer.CompressionMethods[(int)pakEntry.CompressionSlot.Value];

            if (!compressionType.HasValue)
                return new BinMemoryStream(compressedData);

            switch (compressionType.Value)
            {
                case CompressionType.Zlib:
                    return new ZLibStream(new BinMemoryStream(compressedData), CompressionMode.Decompress);

                case CompressionType.Gzip:
                    return new System.IO.Compression.GZipStream(
                        new BinMemoryStream(compressedData),
                        System.IO.Compression.CompressionMode.Decompress);

                case CompressionType.Oodle:
                    return DecompressOodleData(compressedData, pakEntry, version);

                default:
                    throw new NotImplementedException($"Compression type {compressionType} not supported");
            }
        }

        private Stream DecompressOodleData(byte[] compressed, PakEntry entry, UEVersion version)
        {
            var decompressed = new byte[entry.UnpackedSize];

            if (entry.Blocks != null && entry.Blocks.Count > 0)
            {
                int decompOffset = 0;
                int chunkSize = (int)entry.CompressionBlockSize;
                if (chunkSize == 0)
                {
                    // If no block size specified, assume even distribution
                    chunkSize = (int)((entry.UnpackedSize + entry.Blocks.Count - 1) / entry.Blocks.Count);
                }

                // For relative chunk offsets (v5+), blocks are relative to data start
                var versionMajor = GetVersionMajor(version);
                bool relativeOffsets = versionMajor >= VersionMajor.RelativeChunkOffsets;

                for (int i = 0; i < entry.Blocks.Count; i++)
                {
                    var block = entry.Blocks[i];
                    int blockStart, blockEnd;

                    if (relativeOffsets)
                    {
                        // Blocks are relative to the start of compressed data
                        blockStart = (int)block.Start;
                        blockEnd = (int)block.End;
                    }
                    else
                    {
                        // For older versions, calculate relative to first block
                        long baseOffset = entry.Blocks[0].Start;
                        blockStart = (int)(block.Start - baseOffset);
                        blockEnd = (int)(block.End - baseOffset);
                    }

                    // Validate block bounds
                    if (blockStart < 0 || blockEnd > compressed.Length || blockStart >= blockEnd)
                    {
                        System.Diagnostics.Trace.WriteLine(
                            $"Invalid block bounds: start={blockStart}, end={blockEnd}, compressed.Length={compressed.Length}");
                        throw new InvalidOperationException("Invalid block bounds");
                    }

                    int compSize = blockEnd - blockStart;

                    int decompSize;
                    if (i == entry.Blocks.Count - 1)
                    {
                        // Last block - use remaining size
                        decompSize = (int)entry.UnpackedSize - decompOffset;
                    }
                    else
                    {
                        // Not last block - use chunk size
                        decompSize = Math.Min(chunkSize, (int)entry.UnpackedSize - decompOffset);
                    }

                    if (decompSize <= 0)
                    {
                        System.Diagnostics.Trace.WriteLine(
                            $"Invalid decompSize at block {i}: {decompSize}. " +
                            $"DecompOffset: {decompOffset}, UnpackedSize: {entry.UnpackedSize}, ChunkSize: {chunkSize}");
                        throw new InvalidOperationException("Invalid decompression size");
                    }

                    var compData = new byte[compSize];
                    Array.Copy(compressed, blockStart, compData, 0, compSize);

                    var result = OodleNative.Decompress(compData, decompressed, decompOffset, decompSize);
                    if (result <= 0)
                    {
                        System.Diagnostics.Trace.WriteLine(
                            $"Oodle decompression failed at block {i}, offset {decompOffset}. " +
                            $"Block: {blockStart}-{blockEnd}, CompSize: {compSize}, DecompSize: {decompSize}, " +
                            $"ChunkSize: {chunkSize}, CompressionBlockSize: {entry.CompressionBlockSize}, " +
                            $"First bytes: {BitConverter.ToString(compData.Take(Math.Min(16, compData.Length)).ToArray())}");
                        throw new InvalidOperationException("Oodle decompression failed");
                    }

                    decompOffset += (int)result; // Use actual decompressed size
                }
            }
            else
            {
                var result = OodleNative.Decompress(compressed, decompressed, 0, (int)entry.UnpackedSize);
                if (result <= 0)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"Oodle decompression failed. CompressedSize: {compressed.Length}, " +
                        $"UnpackedSize: {entry.UnpackedSize}, " +
                        $"First bytes: {BitConverter.ToString(compressed.Take(Math.Min(16, compressed.Length)).ToArray()). Replace ('-', ' ')}");
                    throw new InvalidOperationException("Oodle decompression failed");
                }
            }

            return new BinMemoryStream(decompressed);
        }

        private UEVersion GetVersionFromArc(ArcFile arc)
        {
            foreach (var version in GetVersions())
            {
                try
                {
                    var footerSize = GetVersionSize(version);
                    long offset = arc.File.MaxOffset - footerSize;

                     var versionMajor = GetVersionMajor(version);
                    if (versionMajor >= VersionMajor.EncryptionKeyGuid)
                        offset += 16;
                    if (versionMajor >= VersionMajor.IndexEncryption)
                        offset += 1;

                    var magic = arc.File.View.ReadUInt32(offset);
                    if (magic == Magic)
                        return version;
                }
                catch { }
            }
            return UEVersion.V11;
        }



        private Footer ReadFooterForDecompression(ArcView file)
        {
            foreach (var version in GetVersions())
            {
                try
                {
                    var footer = ReadFooter(file, version);
                    if (footer.Magic == Magic)
                        return footer;
                }
                catch { }
            }
            throw new InvalidOperationException("Could not read PAK footer");
        }

        private class Footer
        {
            public byte[]     EncryptionUuid { get; set; }
            public bool          IsEncrypted { get; set; }
            public uint                Magic { get; set; }
            public VersionMajor VersionMajor { get; set; }
            public long          IndexOffset { get; set; }
            public long            IndexSize { get; set; }
            public byte[]               Hash { get; set; }
            public bool             IsFrozen { get; set; }
            public List<CompressionType?> CompressionMethods { get; set; }
        }

        private class EncryptedException : Exception { }
    }

    // Custom ArcFile to store AES key
    internal class PakArchive : ArcFile
    {
        public byte[] AesKey { get; set; }

        public PakArchive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base(arc, impl, dir) { }
    }

    // Oodle Native Interop (same as before)
    internal static class OodleNative
    {
        private static readonly object LoadLock = new object();
        private static bool _initialized;
        private static IntPtr _dllHandle;
        private static OodleDecompressFunc _decompressFunc;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long OodleDecompressFunc(
            IntPtr compBuf, ulong compBufSize, 
            IntPtr rawBuf, ulong rawLen,
            uint fuzzSafe, uint checkCRC, uint verbosity,
            ulong decBufBase, ulong decBufSize,
            ulong fpCallback, ulong callbackUserData,
            IntPtr decoderMemory, ulong decoderMemorySize,
            uint threadPhase);

        public static long Decompress(byte[] compressed, byte[] decompressed, int offset, int size)
        {
            Initialize();
            
            if (_decompressFunc == null)
                throw new InvalidOperationException("Oodle not initialized");

            unsafe
            {
                fixed (byte* compPtr = compressed)
                fixed (byte* decompPtr = &decompressed[offset])
                {
                    return _decompressFunc(
                        (IntPtr)compPtr, (ulong)compressed.Length,
                        (IntPtr)decompPtr, (ulong)size,
                        1, 1, 0, 0, 0, 0, 0, IntPtr.Zero, 0, 3);
                }
            }
        }

        private static void Initialize()
        {
            lock (LoadLock)
            {
                if (_initialized)
                    return;

                try
                {
                    var dllPath = GetOodleDllPath();
                    if (!File.Exists(dllPath))
                    {
                        throw new FileNotFoundException($"Oodle DLL not found at: {dllPath}");
                    }

                    _dllHandle = LoadLibrary(dllPath);
                    if (_dllHandle == IntPtr.Zero)
                    {
                        var error = Marshal.GetLastWin32Error();
                        throw new InvalidOperationException($"Failed to load Oodle library. Error code: {error}");
                    }

                    var decompressPtr = GetProcAddress(_dllHandle, "OodleLZ_Decompress");
                    if (decompressPtr == IntPtr.Zero)
                        throw new InvalidOperationException("Failed to find OodleLZ_Decompress");

                    _decompressFunc = Marshal.GetDelegateForFunctionPointer<OodleDecompressFunc>(decompressPtr);
                    
                    // Disable Oodle logging
                    var setPrintfPtr = GetProcAddress(_dllHandle, "OodleCore_Plugins_SetPrintf");
                    if (setPrintfPtr != IntPtr.Zero)
                    {
                        var setPrintf = Marshal.GetDelegateForFunctionPointer<SetPrintfFunc>(setPrintfPtr);
                        setPrintf(IntPtr.Zero);
                    }

                    _initialized = true;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Failed to initialize Oodle", ex);
                }
            }
        }

        private static string GetOodleDllPath()
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var dir = Path.GetDirectoryName(exePath);
            
            string archFolder = Environment.Is64BitProcess ? "x64" : "x86";
            string dllName = "oo2core_9_win64.dll";
            
            var archPath = Path.Combine(dir, archFolder, dllName);
            if (File.Exists(archPath))
                return archPath;
            
            var directPath = Path.Combine(dir, dllName);
            if (File.Exists(directPath))
                return directPath;
            
            dllName = "oo2core_win64.dll";
            archPath = Path.Combine(dir, archFolder, dllName);
            if (File.Exists(archPath))
                return archPath;
                
            directPath = Path.Combine(dir, dllName);
            if (File.Exists(directPath))
                return directPath;
            
            if (!Environment.Is64BitProcess)
            {
                dllName = "oo2core_9_win32.dll";
                archPath = Path.Combine(dir, archFolder, dllName);
                if (File.Exists(archPath))
                    return archPath;
                    
                directPath = Path.Combine(dir, dllName);
                if (File.Exists(directPath))
                    return directPath;
                    
                dllName = "oo2core_win32.dll";
                archPath = Path.Combine(dir, archFolder, dllName);
                if (File.Exists(archPath))
                    return archPath;
                    
                directPath = Path.Combine(dir, dllName);
                if (File.Exists(directPath))
                    return directPath;
            }
            
            return Path.Combine(dir, archFolder, "oo2core_9_win64.dll");
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string dllToLoad);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        private static IntPtr LoadLibrary(string path)
        {
            return LoadLibraryW(path);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetPrintfFunc(IntPtr printf);
    }
}