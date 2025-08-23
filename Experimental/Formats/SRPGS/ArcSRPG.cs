using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.SRPGStudio
{
    internal class SrpgOptions : ResourceOptions
    {
        public string Password { get; set; }
    }

    internal class SRPGUtils
    {
        public static List<Fragment> InitializeFragments()
        {
            var paths = new[]
            {
                "Graphics/mapchip", "Graphics/charchip", "Graphics/face", "Graphics/icon",
                "Graphics/motion", "Graphics/effect", "Graphics/weapon", "Graphics/bow",
                "Graphics/thumbnail", "Graphics/battleback", "Graphics/eventback", "Graphics/screenback",
                "Graphics/worldmap", "Graphics/eventstill", "Graphics/charillust", "Graphics/picture",
                "UI/menuwindow", "UI/textwindow", "UI/title", "UI/number",
                "UI/bignumber", "UI/gauge", "UI/line", "UI/risecursor",
                "UI/mapcursor", "UI/pagecursor", "UI/selectcursor", "UI/scrollcursor",
                "UI/panel", "UI/faceframe", "UI/screenframe",
                "Audio/music", "Audio/sound", "Fonts", "Video"
            };

            return paths.Select(p => new Fragment { Path = p }).ToList();
        }

        public static string GenerateResourceName(string baseName, int index, int maxIndex)
        {
            baseName = baseName.Replace('\\', '/');
            if (maxIndex > 1)
            {
                int digits = (int)Math.Log10(maxIndex) + 1;
                return $"{baseName}-{(index + 1).ToString().PadLeft(digits, '0')}";
            }
            return baseName;
        }

        public static Tuple<string, string> DetectFileType(byte[] header)
        {
            if (header.Length < 3) return new Tuple<string, string>("data", "data");

            var signatures = new Dictionary<byte[], Tuple<string, string>>
            {
                { new byte[] { 0xFF, 0xD8, 0xFF },             new Tuple<string, string>("jpg",   "image") },
                { new byte[] { 0x89, 0x50, 0x4E, 0x47 },       new Tuple<string, string>("png",   "image") },
                { new byte[] { 0x42, 0x4D },                   new Tuple<string, string>("bmp",   "image") },
                { new byte[] { 0x49, 0x44, 0x33 },             new Tuple<string, string>("mp3",   "audio") },
                { new byte[] { 0x52, 0x49, 0x46, 0x46 },       new Tuple<string, string>("wav",   "audio") },
                { new byte[] { 0x4F, 0x67, 0x67 },             new Tuple<string, string>("ogg",   "audio") },
                { new byte[] { 0x4D, 0x54, 0x68, 0x64 },       new Tuple<string, string>("mid",   "audio") },
                { new byte[] { 0x77, 0x4F, 0x46, 0x46 },       new Tuple<string, string>("woff",  "data")  },
                { new byte[] { 0x77, 0x4F, 0x46, 0x32 },       new Tuple<string, string>("woff2", "data")  },
                { new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00 }, new Tuple<string, string>("ttf",   "data")  },
                { new byte[] { 0x4F, 0x54, 0x54, 0x4F, 0x00 }, new Tuple<string, string>("otf",   "data")  }
            };

            foreach (var sig in signatures)
            {
                if (header.Take(sig.Key.Length).SequenceEqual(sig.Key))
                    return sig.Value;
            }

            return new Tuple<string, string>("data", "data");
        }
    }
    [Export(typeof(ArchiveFormat))]
    public class DtsOpener : ArchiveFormat
    {
        private static readonly string[] KnownKeys = { "keyset", "_dynamic" };

        public override string         Tag { get { return "DTS/SRPG"; } }
        public override string Description { get { return "SRPG Studio data archive"; } }
        public override uint     Signature { get { return  0x53544453; } } // 'SDTS'
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        public DtsOpener()
        {
            Extensions = new string[] { "dts" };
        }

        public override ArcFile TryOpen(ArcView file)
        {
            if (!file.View.AsciiEqual(0, "SDTS"))
                return null;

            var header = new DtsHeader();
            header.IsEncrypted = file.View.ReadUInt32(4) == 1;
            header.Version     = file.View.ReadUInt32(8);
            header.Format      = file.View.ReadUInt32(12);
            header.Unknown     = file.View.ReadUInt32(16);

            uint numSections = header.Version < 1140 ? 35u : 36u;
            uint headerSize = 24 + numSections * 4;

            header.ProjectOffset = file.View.ReadUInt32(20) + headerSize;
            header.ProjectSize = (uint)(file.MaxOffset - header.ProjectOffset);

            SrpgCrypto assetCrypto = null;
            SrpgCrypto projectCrypto = null;

            if (header.IsEncrypted)
            {
                var keys = DetectKeys(file, header);
                assetCrypto = keys.Item1;
                projectCrypto = keys.Item2;

                if (assetCrypto == null)
                {
                    assetCrypto   = new SrpgCrypto(KnownKeys[0]);
                    projectCrypto = assetCrypto;
                }
            }

            var fragments = SRPGUtils.InitializeFragments();
            var fragmentOffsets = new uint[fragments.Count + 1];
            for (int i = 0; i < fragmentOffsets.Length; i++)
            {
                fragmentOffsets[i] = file.View.ReadUInt32(24 + i * 4) + headerSize;
            }

            for (int i = 0; i < fragments.Count; i++)
            {
                fragments[i].Begin = fragmentOffsets[i];
                fragments[i].End   = fragmentOffsets[i + 1] - 1;
            }

            var dir = new List<Entry>();

            foreach (var fragment in fragments)
            {
                if (fragment.Size == 0)
                    continue;

                uint pos = fragment.Begin;
                uint count = file.View.ReadUInt32(pos);
                pos += 4;

                var positions = new List<uint>();
                for (uint i = 0; i < count; i++)
                {
                    positions.Add(file.View.ReadUInt32(pos + i * 4) + fragment.Begin);
                }
                positions.Add(fragment.End + 1);

                for (int i = 0; i < count; i++)
                {
                    uint groupBegin = positions[i];
                    uint groupEnd = positions[i + 1] - 1;

                    uint nameLength = file.View.ReadUInt32(groupBegin);
                    var nameBytes = file.View.ReadBytes(groupBegin + 4, nameLength);
                    string groupName = Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');

                    ulong groupId = file.View.ReadUInt64(groupBegin + 4 + nameLength);
                    uint resourceCount = file.View.ReadUInt32(groupBegin + 12 + nameLength);

                    uint offset = groupBegin + 16 + nameLength;
                    var resources = new List<ResourceInfo>();

                    for (uint j = 0; j < resourceCount; j++)
                    {
                        uint size = file.View.ReadUInt32(offset + j * 4);
                        resources.Add(new ResourceInfo { Size = size });
                    }

                    uint dataOffset = offset + resourceCount * 4;
                    for (int j = 0; j < resources.Count; j++)
                    {
                        var res = resources[j];
                        res.Offset = dataOffset;
                        dataOffset += res.Size;
                        if (res.Size == 0)
                            continue;

                        var header8 = file.View.ReadBytes(res.Offset, Math.Min(8, res.Size));
                        var decrypted = header.IsEncrypted ? assetCrypto.Decrypt(header8) : header8;
                        var ext = SRPGUtils.DetectFileType(decrypted);
                        string resName = SRPGUtils.GenerateResourceName(groupName, j, (int)resourceCount);

                        var entry = new SrpgEntry
                        {
                            Name     = $"{fragment.Path}/{resName}.{ext.Item1}",
                            Type     = ext.Item2,
                            Offset   = res.Offset,
                            Size     = res.Size,
                            IsPacked = header.IsEncrypted,
                            Crypto   = header.IsEncrypted ? assetCrypto : null
                        };

                        dir.Add(entry);
                    }
                }
            }

            if (header.ProjectSize > 0)
            {
                dir.Add(new SrpgEntry
                {
                    Name =     "project.no-srpgs",
                    Type =     "data",
                    Offset =   header.ProjectOffset,
                    Size =     header.ProjectSize,
                    IsPacked = header.IsEncrypted,
                    Crypto =   projectCrypto
                });
            }

            return new ArcFile(file, this, dir);
        }

        private Tuple<SrpgCrypto, SrpgCrypto> DetectKeys(ArcView file, DtsHeader header)
        {
            if (header.ProjectSize < 32)
                return new Tuple<SrpgCrypto, SrpgCrypto>(null, null);

            var projectHeader = file.View.ReadBytes(header.ProjectOffset, Math.Min(32, header.ProjectSize));

            if (!IsEncryptedBuffer(projectHeader))
                return new Tuple<SrpgCrypto, SrpgCrypto>(new SrpgCrypto(KnownKeys[0]), null);

            foreach (var key in KnownKeys)
            {
                var crypto = new SrpgCrypto(key);
                var testDecrypt = crypto.Decrypt(projectHeader);

                if (!IsEncryptedBuffer(testDecrypt))
                {
                    var assetKey = TryDetectAssetKey(file, header, testDecrypt);
                    if (assetKey != null)
                        return new Tuple<SrpgCrypto, SrpgCrypto>(assetKey, crypto);
                    else
                        return new Tuple<SrpgCrypto, SrpgCrypto>(crypto, crypto);
                }
            }

            // Project key not found, try detecting from file content
            var detectedKey = TryDetectKeyFromContent(file, header);
            if (detectedKey != null)
                return new Tuple<SrpgCrypto, SrpgCrypto>(detectedKey, detectedKey);

            return new Tuple<SrpgCrypto, SrpgCrypto>(null, null);
        }

        private SrpgCrypto TryDetectAssetKey(ArcView file, DtsHeader header, byte[] decryptedProjectHeader)
        {
            var assetKeyBytes = MD5.Create().ComputeHash(decryptedProjectHeader, 0, 16);
            var assetCrypto = new SrpgCrypto(assetKeyBytes);

            if (TestKeyOnAssets(file, header, assetCrypto))
                return assetCrypto;

            return null;
        }

        private bool TestKeyOnAssets(ArcView file, DtsHeader header, SrpgCrypto crypto)
        {
            uint headerSize = 24 + (header.Version < 0x474 ? 35u : 36u) * 4;

            for (int fragIdx = 0; fragIdx < Math.Min(5, 35); fragIdx++)
            {
                uint fragOffset = file.View.ReadUInt32(24 + fragIdx * 4) + headerSize;
                if (fragOffset >= file.MaxOffset - 4)
                    continue;

                uint count = file.View.ReadUInt32(fragOffset);
                if (count == 0 || count > 1000)
                    continue;

                uint firstFileOffset = file.View.ReadUInt32(fragOffset + 4) + fragOffset;
                if (firstFileOffset >= file.MaxOffset - 20)
                    continue;

                uint nameLen = file.View.ReadUInt32(firstFileOffset);
                if (nameLen > 1000)
                    continue;

                uint dataStart = firstFileOffset + 4 + nameLen + 12;
                if (dataStart >= file.MaxOffset - 8)
                    continue;

                uint fileCount = file.View.ReadUInt32(dataStart);
                if (fileCount == 0 || fileCount > 100)
                    continue;

                uint fileSize = file.View.ReadUInt32(dataStart + 4);
                if (fileSize < 8 || fileSize > file.MaxOffset)
                    continue;

                uint contentOffset = dataStart + 4 + fileCount * 4;
                if (contentOffset + 8 > file.MaxOffset)
                    continue;

                var testData = file.View.ReadBytes(contentOffset, 8);
                var decrypted = crypto.Decrypt(testData);

                if (IsValidFileSignature(decrypted))
                    return true;
            }

            return false;
        }

        private SrpgCrypto TryDetectKeyFromContent(ArcView file, DtsHeader header)
        {
            foreach (var key in KnownKeys)
            {
                var crypto = new SrpgCrypto(key);
                if (TestKeyOnAssets(file, header, crypto))
                    return crypto;
            }

            return null;
        }

        private bool IsValidFileSignature(byte[] data)
        {
            if (data.Length < 4) return false;

            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                return true; // PNG

            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return true; // JPEG

            if (data[0] == 0x42 && data[1] == 0x4D)
                return true; // BMP

            if (data[0] == 0x4F && data[1] == 0x67 && data[2] == 0x67)
                return true; // OGG

            return false;
        }

        private bool IsEncryptedBuffer(byte[] buffer)
        {
            if (buffer.Length < 28) return false;

            uint a = BitConverter.ToUInt32(buffer, 16);
            uint b = BitConverter.ToUInt32(buffer, 20);
            uint c = BitConverter.ToUInt32(buffer, 24);

            return a > 255 || b > 1023 || c > 1023;
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var srpgEntry = entry as SrpgEntry;
            if (srpgEntry == null || srpgEntry.Crypto == null)
                return base.OpenEntry(arc, entry);

            var data = arc.File.View.ReadBytes(entry.Offset, entry.Size);
            var decrypted = srpgEntry.Crypto.Decrypt(data);
            return new BinMemoryStream(decrypted, entry.Name);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class RtsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "RTS/SRPG"; } }
        public override string Description { get { return "SRPG Studio RTP archive"; } }
        public override uint     Signature { get { return  0x53545253; } } // 'SRTS'
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        public RtsOpener()
        {
            Extensions = new string[] { "rts" };
        }

        public override ArcFile TryOpen(ArcView file)
        {
            if (!file.View.AsciiEqual(0, "SRTS"))
                return null;

            uint version            = file.View.ReadUInt32(4);
            uint totalSize          = file.View.ReadUInt32(8);
            uint firstSectionOffset = file.View.ReadUInt32(12);

            var sectionTable = new List<uint>();
            for (uint offset = 16; offset < firstSectionOffset; offset += 4)
                sectionTable.Add(file.View.ReadUInt32(offset));

            var sectionPaths = GetSectionPaths();
            var dir = new List<Entry>();
            for (int i = 0; i < sectionTable.Count && i < sectionPaths.Count; i++)
                ProcessSection(file, sectionTable[i], sectionPaths[i], dir);

            return new ArcFile(file, this, dir);
        }

        private void ProcessSection(ArcView file, uint sectionOffset, string sectionPath, List<Entry> dir)
        {
            uint groupCount = file.View.ReadUInt32(sectionOffset);
            var groupOffsets = new List<uint>();
            for (uint i = 0; i < groupCount; i++)
            {
                uint relOffset = file.View.ReadUInt32(sectionOffset + 4 + i * 4);
                groupOffsets.Add(sectionOffset + relOffset);
            }

            foreach (var groupOffset in groupOffsets)
                ProcessGroup(file, groupOffset, sectionPath, dir);
        }

        private void ProcessGroup(ArcView file, uint groupOffset, string sectionPath, List<Entry> dir)
        {
            uint nameLength = file.View.ReadUInt32(groupOffset);
            var nameBytes = file.View.ReadBytes(groupOffset+4, nameLength);
            string groupName = Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');

            uint offset = groupOffset + 4 + nameLength;
            uint meta1 = file.View.ReadUInt32(offset);
            uint meta2 = file.View.ReadUInt32(offset + 4);
            uint resourceCount = file.View.ReadUInt32(offset + 8);
            if (!IsSaneCount(resourceCount))
                throw new InvalidFormatException("Group files number is impossibly big.");

            offset += 12;
            var resourceSizes = new uint[resourceCount];
            for (uint i = 0; i < resourceCount; i++)
                resourceSizes[i] = file.View.ReadUInt32(offset + i * 4);

            uint dataOffset = offset + resourceCount * 4;
            for (int i = 0; i < resourceCount; i++)
            {
                if (resourceSizes[i] == 0)
                    continue;

                var header = file.View.ReadBytes(dataOffset, Math.Min(8, resourceSizes[i]));
                var fileInfo = SRPGUtils.DetectFileType(header);

                string resourceName = SRPGUtils.GenerateResourceName(groupName, i, (int)resourceCount);

                var entry = new Entry
                {
                    Name = $"{sectionPath}/{resourceName}.{fileInfo.Item1}",
                    Type = fileInfo.Item2,
                    Offset = dataOffset,
                    Size = resourceSizes[i]
                };

                dir.Add(entry);

                dataOffset += resourceSizes[i];
            }
        }

        private List<string> GetSectionPaths()
        {
            return new List<string>
            {
                "Graphics/mapchip", "Graphics/face", "Graphics/icon", "Graphics/motion",
                "Graphics/effect", "Graphics/weapon", "Graphics/bow", "Graphics/thumbnail", "Graphics/battleback",
                "Graphics/eventback", "Graphics/screenback", "Graphics/worldmap", "Graphics/eventstill",
                "Graphics/charillust", "Graphics/picture", "Audio/music", "Audio/sound", "UI/menuwindow",
                "UI/textwindow", "UI/title", "UI/number", "UI/bignumber", "UI/gauge", "UI/line", "UI/risecursor",
                "UI/mapcursor", "UI/pagecursor", "UI/selectcursor", "UI/scrollcursor","UI/panel", "UI/faceframe",
                "UI/screenframe", "Fonts", "Videos", "Script", "Materials"
            };
        }
    }



    #region SRPG Cryptography and Commons
    internal class SrpgEntry : PackedEntry
    {
        public SrpgCrypto Crypto { get; set; }
    }

    internal class SrpgCrypto
    {
        private readonly ICryptoTransform m_transform;
        private readonly byte[]           m_key;

        public SrpgCrypto(byte[] secret, int cryptMode = -1)
        {
            m_key = secret;

            if (cryptMode == -1)
                m_transform = new RC4Transform(m_key);
            else
            {
                var rc2 = new RC2CryptoServiceProvider
                {
                    Mode = CipherMode.CBC,
                    Padding = PaddingMode.None,
                    IV = new byte[8],
                    EffectiveKeySize = 128
                };
                m_transform = rc2.CreateDecryptor(m_key, rc2.IV);
            }
        }

        public SrpgCrypto(string secret, int cryptMode = -1)
        {
            var secretBytes = Encoding.Unicode.GetBytes(secret);
            m_key = MD5.Create().ComputeHash(secretBytes, 0, secret.Length);

            if (cryptMode == -1)
                m_transform = new RC4Transform(m_key);
            else
            {
                var rc2 = new RC2CryptoServiceProvider
                {
                    Mode = CipherMode.CBC,
                    Padding = PaddingMode.None,
                    IV = new byte[8],
                    EffectiveKeySize = 128
                };
                m_transform = rc2.CreateDecryptor(m_key, rc2.IV);
            }
        }

        public byte[] Decrypt(byte[] data)
        {
            using (var transform = new RC4Transform(m_key))
            {
                return transform.TransformFinalBlock(data, 0, data.Length);
            }
        }

        public byte[] Encrypt(byte[] data)
        {
            using (var transform = new RC4Transform(m_key))
            {
                return transform.TransformFinalBlock(data, 0, data.Length);
            }
        }
    }

    internal class RC4Transform : ICryptoTransform
    {
        private readonly byte[] m_state;
        private byte m_x;
        private byte m_y;

        public RC4Transform(byte[] key)
        {
            m_state = new byte[256];
            m_x = 0;
            m_y = 0;

            for (int i = 0; i < 256; i++)
                m_state[i] = (byte)i;

            // Key scheduling
            byte j = 0;
            for (int i = 0; i < 256; i++)
            {
                j = (byte)(j + m_state[i] + key[i % key.Length]);
                byte temp = m_state[i];
                m_state[i] = m_state[j];
                m_state[j] = temp;
            }
        }

        public bool CanReuseTransform => false;
        public bool CanTransformMultipleBlocks => true;
        public int InputBlockSize => 1;
        public int OutputBlockSize => 1;

        public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; i++)
            {
                m_x = (byte)(m_x + 1);
                m_y = (byte)(m_y + m_state[m_x]);

                byte temp = m_state[m_x];
                m_state[m_x] = m_state[m_y];
                m_state[m_y] = temp;

                byte k = m_state[(byte)(m_state[m_x] + m_state[m_y])];
                outputBuffer[outputOffset + i] = (byte)(inputBuffer[inputOffset + i] ^ k);
            }
            return inputCount;
        }

        public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] output = new byte[inputCount];
            TransformBlock(inputBuffer, inputOffset, inputCount, output, 0);
            return output;
        }

        public void Dispose()
        {
            Array.Clear(m_state, 0, m_state.Length);
        }
    }

    internal class DtsHeader
    {
        public bool IsEncrypted { get; set; }
        public uint Version { get; set; }
        public uint Format { get; set; }
        public uint Unknown { get; set; }
        public uint ProjectOffset { get; set; }
        public uint ProjectSize { get; set; }
    }

    public class Fragment
    {
        public string Path { get; set; }
        public uint Begin { get; set; }
        public uint End { get; set; }
        public uint Size => End >= Begin ? End - Begin + 1 : 0;
    }

    internal class ResourceInfo
    {
        public uint Offset { get; set; }
        public uint Size { get; set; }
    }
    #endregion

    [Export(typeof(ArchiveFormat))]
    public class LanguageDatOpener : ArchiveFormat
    {
        SrpgOptions m_options = new SrpgOptions();

        public override string         Tag { get { return "DAT/SRPGLANG"; } }
        public override string Description { get { return "SRPG Studio language data"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  false; } }

        public LanguageDatOpener()
        {
            Extensions = new string[] { "dat" };
            m_options.Password = "_dummy";
        }

        public override ArcFile TryOpen(ArcView file)
        {
            if (!file.Name.HasExtension(".dat") || !file.Name.Contains("language"))
                return null;

            try
            {
                var crypto = new SrpgCrypto(m_options.Password, 0);
                var xorKey = new byte[] 
                { 
                    0x54, 0x94, 0xC1, 0x58, 0xF4, 0x4C, 0x92, 0x1B, 
                    0xAD, 0xE0, 0x9E, 0x3A, 0x49, 0xD1, 0xC9, 0x92 
                };

                var headerSize = Math.Min(16, file.MaxOffset);
                var header = file.View.ReadBytes(0, (uint)headerSize);

                for (int i = 0; i < header.Length && i < xorKey.Length; i++)
                    header[i] ^= xorKey[i % xorKey.Length];

                var decHeader = crypto.Decrypt(header);
                uint count = BitConverter.ToUInt32(decHeader, 0);
                uint langId = BitConverter.ToUInt32(decHeader, 4);

                if (!IsSaneCount((int)count, 100000))
                    return null;

                // Read full file
                var data = file.View.ReadBytes(0, (uint)file.MaxOffset);
                for (int i = 0; i < Math.Min(data.Length, xorKey.Length); i++)
                    data[i] ^= xorKey[i % xorKey.Length];

                var decrypted = crypto.Decrypt(data);
                var dir = new List<Entry>();
                uint pos = 8;

                for (int i = 0; i < count && pos < decrypted.Length - 4; i++)
                {
                    uint strLen = BitConverter.ToUInt32(decrypted, (int)pos);
                    if (strLen > decrypted.Length - pos - 4)
                        break;

                    var entry = new Entry
                    {
                        Name = $"string_{i:D5}.txt",
                        Type = "text",
                        Offset = pos + 4,
                        Size = strLen
                    };
                    dir.Add(entry);
                    pos += 4 + strLen;
                }

                if (dir.Count > 0)
                    return new LanguageArchive(file, this, dir, decrypted);
            }
            catch
            {
                // Not a valid language file
            }

            return null;
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var langArc = arc as LanguageArchive;
            if (langArc == null)
                return base.OpenEntry(arc, entry);

            var text = Encoding.Unicode.GetString(langArc.DecryptedData, (int)entry.Offset, (int)entry.Size);
            text = text.TrimEnd('\0');
            var bytes = Encoding.UTF8.GetBytes(text);
            return new BinMemoryStream(bytes, entry.Name);
        }
    }

    internal class LanguageArchive : ArcFile
    {
        public byte[] DecryptedData { get; }

        public LanguageArchive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] decryptedData)
            : base(arc, impl, dir)
        {
            DecryptedData = decryptedData;
        }
    }

    public class SrpgScheme : ResourceScheme
    {
        public Dictionary<string, string> KnownKeys { get; set; }
    }
}