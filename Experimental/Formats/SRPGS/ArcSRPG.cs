using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Diagnostics;
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
        public static List<Section> InitializeSections()
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
                "Audio/music", "Audio/sound", "Fonts", "Video", "Script", "Materials"
            };

            return paths.Select (p => new Section { Path = p }).ToList();
        }

        public static string GenerateResourceName (string baseName, int index, int maxIndex)
        {
            baseName = baseName.Replace('\\', '/');
            if (maxIndex > 1)
            {
                int digits = (int)Math.Log10 (maxIndex) + 1;
                return $"{baseName}-{(index + 1).ToString().PadLeft (digits, '0')}";
            }
            return baseName;
        }

        public static Tuple<string, string> DetectFileType (byte[] header)
        {
            if (header.Length < 3) return new Tuple<string, string>("", "");

            // Check multi-byte offset signatures first

            // MP4/MOV (ftyp box at offset 4)
            if (header.Length >= 8 && header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70)
                return new Tuple<string, string>(".mp4", "video");

            // RIFF-based formats (need to check type at offset 8)
            if (header.Length >= 12 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46)
            {
                // WAVE
                if (header[8] == 0x57 && header[9] == 0x41 && header[10] == 0x56 && header[11] == 0x45)
                    return new Tuple<string, string>(".wav", "audio");
                // WEBP
                if (header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
                    return new Tuple<string, string>(".webp", "image");
                // AVI
                if (header[8] == 0x41 && header[9] == 0x56 && header[10] == 0x49 && header[11] == 0x20)
                    return new Tuple<string, string>(".avi", "video");
            }

            // MP3 frame sync (raw MP3 without ID3) - check before dictionary
            if (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
                return new Tuple<string, string>(".mp3", "audio");

            // Simple signatures - check from start of file
            var signatures = new Dictionary<byte[], Tuple<string, string>>
            {
                { new byte[] { 0xFF, 0xD8, 0xFF },             new Tuple<string, string>(".jpg",   "image") },
                { new byte[] { 0x89, 0x50, 0x4E, 0x47 },       new Tuple<string, string>(".png",   "image") },
                { new byte[] { 0x42, 0x4D },                   new Tuple<string, string>(".bmp",   "image") },
                { new byte[] { 0x47, 0x49, 0x46 },             new Tuple<string, string>(".gif",   "image") },
                { new byte[] { 0x49, 0x44, 0x33 },             new Tuple<string, string>(".mp3",   "audio") },
                { new byte[] { 0x4F, 0x67, 0x67, 0x53 },       new Tuple<string, string>(".ogg",   "audio") },
                { new byte[] { 0x4D, 0x54, 0x68, 0x64 },       new Tuple<string, string>(".mid",   "audio") },
                { new byte[] { 0x46, 0x4C, 0x56 },             new Tuple<string, string>(".flv",   "video") },
                { new byte[] { 0x77, 0x4F, 0x46, 0x46 },       new Tuple<string, string>(".woff",  "font")  },
                { new byte[] { 0x77, 0x4F, 0x46, 0x32 },       new Tuple<string, string>(".woff2", "font")  },
                { new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00 }, new Tuple<string, string>(".ttf",   "font")  },
                { new byte[] { 0x4F, 0x54, 0x54, 0x4F, 0x00 }, new Tuple<string, string>(".otf",   "font")  },
                { new byte[] { 0x1A, 0x45, 0xDF, 0xA3 },       new Tuple<string, string>(".mkv",   "video") }
            };

            foreach (var sig in signatures)
            {
                if (header.Length >= sig.Key.Length && header.Take (sig.Key.Length).SequenceEqual (sig.Key))
                    return sig.Value;
            }

            return new Tuple<string, string>("", "");
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class DtsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DTS/SRPG"; } }
        public override string Description { get { return "SRPG Studio data archive"; } }
        public override uint     Signature { get { return  0x53544453; } } // 'SDTS'
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        private static readonly string[] KnownKeys = { "keyset", "_dynamic" };

        public DtsOpener()
        {
            Extensions = new string[] { "dts" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "SDTS"))
                return null;

            var header = new DtsHeader();
            header.IsEncrypted = file.View.ReadUInt32 (4) == 1;
            header.Version     = file.View.ReadUInt32 (8);
            header.Format      = file.View.ReadUInt32 (12);
            header.Unknown     = file.View.ReadUInt32 (16);

            uint numSections = header.Version < 1140 ? 35u : 36u;
            uint headerSize = 24 + numSections * 4;

            header.ProjectOffset = file.View.ReadUInt32 (20) + headerSize;
            header.ProjectSize = (uint)(file.MaxOffset - header.ProjectOffset);

            //Debug.WriteLine ($"[SRPG] Archive size: {file.MaxOffset}, Version: {header.Version:X}, Sections: {numSections}, Project at: {header.ProjectOffset}, Project size: {header.ProjectSize}");

            SrpgCrypto assetCrypto = null;
            SrpgCrypto projectCrypto = null;

            if (header.IsEncrypted)
            {
                var keys = DetectKeys (file, header);
                assetCrypto = keys.Item1;
                projectCrypto = keys.Item2;

                if (assetCrypto == null)
                {
                    assetCrypto   = new SrpgCrypto (KnownKeys[0]);
                    projectCrypto = assetCrypto;
                }
            }

            var sections = SRPGUtils.InitializeSections();

            var sectionOffsets = new uint[numSections];
            for (int i = 0; i < numSections; i++)
                sectionOffsets[i] = file.View.ReadUInt32 (24 + i * 4);

            /*Debug.WriteLine ($"[SRPG] Section offsets (raw):");
            for (int i = 0; i < numSections; i++)
                //Debug.WriteLine ($"  Section {i}: offset {sectionOffsets[i]:X8}");
            */

            var dir = new List<Entry>();
            var usedNames = new Dictionary<string, int>();
            ulong totalExtractedSize = 0;

            for (int sectionIdx = 0; sectionIdx < numSections && sectionIdx < sections.Count; sectionIdx++)
            {
                var section = sections[sectionIdx];
                uint sectionBegin = sectionOffsets[sectionIdx] + headerSize;
                uint sectionEnd;

                if (sectionIdx + 1 < numSections)
                    sectionEnd = sectionOffsets[sectionIdx + 1] + headerSize - 1;
                else
                    sectionEnd = header.ProjectOffset - 1;

                if (sectionEnd < sectionBegin || sectionBegin >= file.MaxOffset)
                {
                    //Debug.WriteLine ($"[SRPG] Section {sectionIdx} ({section.Path}): SKIPPED - invalid range ({sectionBegin:X8} to {sectionEnd:X8})");
                    continue;
                }

                ulong sectionSize = sectionEnd - sectionBegin + 1;
                //Debug.WriteLine ($"[SRPG] Section {sectionIdx} ({section.Path}): offset {sectionBegin:X8}, size {sectionSize}");

                bool isScriptSection = section.Path == "Script";

                uint pos = sectionBegin;
                if (pos + 4 > file.MaxOffset)
                    continue;

                uint count = file.View.ReadUInt32 (pos);
                pos += 4;

                if (count > 10000)
                {
                    //Debug.WriteLine ($"[SRPG] Section {sectionIdx}: SKIPPED - count too large ({count})");
                    continue;
                }

                uint filesInSection = 0;
                ulong bytesInSection = 0;

                if (isScriptSection)
                {
                    //Debug.WriteLine ($"[SRPG] Script section: parsing {count} scripts from {sectionBegin:X8} to {sectionEnd:X8}");
                    uint scriptSectionEnd = pos;

                    for (uint i = 0; i < count && pos <= sectionEnd; i++)
                    {
                        if (pos + 4 > sectionEnd)
                            break;

                        uint nameLength = file.View.ReadUInt32 (pos);
                        if (nameLength > 1000 || pos + 4 + nameLength > sectionEnd)
                            break;

                        var nameBytes = file.View.ReadBytes (pos + 4, nameLength);
                        string scriptName = Encoding.Unicode.GetString (nameBytes).TrimEnd('\0');
                        scriptName = scriptName.Replace('\\', '/');
                        pos += 4 + nameLength;

                        if (pos + 4 > sectionEnd)
                            break;

                        uint scriptSize = file.View.ReadUInt32 (pos);
                        pos += 4;

                        if (scriptSize > 0 && pos + scriptSize <= sectionEnd + 1)
                        {
                            var entry = new SrpgEntry
                            {
                                Name     = GetUniqueName (usedNames, $"{section.Path}/{scriptName}"),
                                Type     = "script",
                                Offset   = pos,
                                Size     = scriptSize,
                                IsPacked = false,
                                Crypto   = null
                            };
                            dir.Add (entry);
                            filesInSection++;
                            bytesInSection += scriptSize;
                        }
                        pos += scriptSize;
                        scriptSectionEnd = pos;
                    }

                    //Debug.WriteLine ($"[SRPG] Scripts end at {scriptSectionEnd:X8}, section end at {sectionEnd:X8}");

                    if (scriptSectionEnd < sectionEnd - 4)
                    {
                        uint remainingSize = sectionEnd - scriptSectionEnd + 1;
                        //Debug.WriteLine ($"[SRPG] Found {remainingSize} bytes after scripts - checking for Materials section");

                        uint matSectionStart = scriptSectionEnd;
                        uint matCount = file.View.ReadUInt32 (matSectionStart);

                        //Debug.WriteLine ($"[SRPG] Materials section at {matSectionStart:X8}: {matCount} files");
                        //Debug.WriteLine ($"[SRPG] Archive encrypted: {header.IsEncrypted}, Asset crypto: {assetCrypto != null}");

                        if (matCount > 0 && matCount < 10000)
                        {
                            uint matPos = matSectionStart + 4;
                            uint materialsExtracted = 0;

                            // Materials are stored sequentially, not with offset table
                            for (uint i = 0; i < matCount && matPos < sectionEnd; i++)
                            {
                                if (matPos + 4 > sectionEnd) break;
                                uint nameLength = file.View.ReadUInt32 (matPos);
                                matPos += 4;

                                if (nameLength == 0 || nameLength > 1000 || matPos + nameLength > sectionEnd)
                                {
                                    //Debug.WriteLine ($"[SRPG] Material {i}: invalid name length {nameLength} at {matPos-4:X8}");
                                    break;
                                }

                                var nameBytes = file.View.ReadBytes (matPos, nameLength);
                                string fileName = Encoding.Unicode.GetString (nameBytes).TrimEnd('\0');
                                fileName = fileName.Replace('\\', '/');
                                matPos += nameLength;

                                // Read file size
                                if (matPos + 4 > sectionEnd) break;
                                uint fileSize = file.View.ReadUInt32 (matPos);
                                matPos += 4;

                                if (fileSize == 0 || matPos + fileSize > sectionEnd)
                                {
                                    //Debug.WriteLine ($"[SRPG] Material {i} '{fileName}': invalid size {fileSize}");
                                    break;
                                }

                                var header16 = file.View.ReadBytes (matPos, Math.Min (16, fileSize));
                                var ext = SRPGUtils.DetectFileType (header16);
                                if (string.IsNullOrEmpty (ext.Item1) && fileName.Contains('.'))
                                    ext = new Tuple<string, string>("", ext.Item2);

                                var entry = new SrpgEntry
                                {
                                    Name     = GetUniqueName (usedNames, $"Materials/{fileName}"),
                                    Type     = ext.Item2,
                                    Offset   = matPos,
                                    Size     = fileSize,
                                    IsPacked = false,  // they are not encrypted
                                    Crypto   = null
                                };

                                dir.Add (entry);
                                materialsExtracted++;
                                filesInSection++;
                                bytesInSection += fileSize;

                                matPos += fileSize;
                            }

                            //Debug.WriteLine ($"[SRPG] Extracted {materialsExtracted} material files");
                        }
                    }
                }
                else
                {
                    // Regular section with offset table
                    //Debug.WriteLine ($"[SRPG] Section {sectionIdx}: {count} groups");

                    var positions = new List<uint>();
                    for (uint i = 0; i < count; i++)
                    {
                        if (pos + i * 4 >= file.MaxOffset)
                            break;
                        positions.Add (file.View.ReadUInt32 (pos + i * 4) + sectionBegin);
                    }
                    positions.Add (sectionEnd + 1);

                    for (int i = 0; i < count && i < positions.Count - 1; i++)
                    {
                        uint groupBegin = positions[i];
                        uint groupEnd = positions[i + 1] - 1;

                        if (groupBegin >= file.MaxOffset || groupBegin + 16 > groupEnd)
                            continue;

                        uint nameLength = file.View.ReadUInt32 (groupBegin);
                        if (nameLength > 1000 || groupBegin + 4 + nameLength + 12 > groupEnd)
                            continue;

                        var nameBytes = file.View.ReadBytes (groupBegin + 4, nameLength);
                        string groupName = Encoding.Unicode.GetString (nameBytes).TrimEnd('\0');
                        groupName = groupName.Replace('\\', '/');

                        uint resourceCount = file.View.ReadUInt32 (groupBegin + 12 + nameLength);
                        if (resourceCount > 1000)
                            continue;

                        uint offset = groupBegin + 16 + nameLength;
                        var resources = new List<ResourceInfo>();

                        for (uint j = 0; j < resourceCount; j++)
                        {
                            if (offset + j * 4 >= file.MaxOffset)
                                break;
                            uint size = file.View.ReadUInt32 (offset + j * 4);
                            resources.Add (new ResourceInfo { Size = size });
                        }

                        uint dataOffset = offset + resourceCount * 4;
                        for (int j = 0; j < resources.Count; j++)
                        {
                            var res = resources[j];
                            res.Offset = dataOffset;

                            if (res.Size == 0 || dataOffset + res.Size > groupEnd + 1)
                            {
                                dataOffset += res.Size;
                                continue;
                            }

                            var header16 = file.View.ReadBytes (res.Offset, Math.Min (16, res.Size));
                            var decrypted = (header.IsEncrypted && assetCrypto != null) ? assetCrypto.Decrypt (header16) : header16;
                            var ext = SRPGUtils.DetectFileType (decrypted);
                            string resName = SRPGUtils.GenerateResourceName (groupName, j, (int)resourceCount);

                            var entry = new SrpgEntry
                            {
                                Name     = GetUniqueName (usedNames, $"{section.Path}/{resName}{ext.Item1}"),
                                Type     = ext.Item2,
                                Offset   = res.Offset,
                                Size     = res.Size,
                                IsPacked = header.IsEncrypted && assetCrypto != null,
                                Crypto   = (header.IsEncrypted && assetCrypto != null) ? assetCrypto : null
                            };

                            dir.Add (entry);
                            filesInSection++;
                            bytesInSection += res.Size;
                            dataOffset += res.Size;
                        }
                    }
                }

                //Debug.WriteLine ($"[SRPG] Section {sectionIdx}: extracted {filesInSection} files, {bytesInSection} bytes");
                totalExtractedSize += bytesInSection;
            }

            if (header.ProjectSize > 0 && header.ProjectOffset < file.MaxOffset)
            {
                dir.Add (new SrpgEntry {
                    Name     = "project.no-srpgs",
                    Type     = "",
                    Offset   = header.ProjectOffset,
                    Size     = Math.Min (header.ProjectSize, (uint)(file.MaxOffset - header.ProjectOffset)),
                    IsPacked = header.IsEncrypted && projectCrypto != null,
                    Crypto   = projectCrypto
                });
                totalExtractedSize += header.ProjectSize;
            }

            //Debug.WriteLine ($"[SRPG] Total extracted: {dir.Count} files, {totalExtractedSize} bytes from {file.MaxOffset} byte archive");
            //Debug.WriteLine ($"[SRPG] Missing: {file.MaxOffset - (long)totalExtractedSize} bytes");

            return new ArcFile (file, this, dir);
        }

        private string GetUniqueName (Dictionary<string, int> usedNames, string name)
        {
            if (!usedNames.ContainsKey (name))
            {
                usedNames[name] = 1;
                return name;
            }

            usedNames[name]++;

            string directory = Path.GetDirectoryName (name).Replace('\\', '/');
            string fileName = Path.GetFileNameWithoutExtension (name);
            string extension = Path.GetExtension (name);

            if (string.IsNullOrEmpty (directory))
                return $"{fileName}_{usedNames[name]}{extension}";
            else
                return $"{directory}/{fileName}_{usedNames[name]}{extension}";
        }

        private Tuple<SrpgCrypto, SrpgCrypto> DetectKeys (ArcView file, DtsHeader header)
        {
            if (header.ProjectSize < 32)
                return new Tuple<SrpgCrypto, SrpgCrypto>(null, null);

            var projectHeader = file.View.ReadBytes (header.ProjectOffset, Math.Min (32, header.ProjectSize));

            foreach (var key in KnownKeys)
            {
                var crypto = new SrpgCrypto (key);
                var testBuffer = new byte[projectHeader.Length];
                Array.Copy (projectHeader, testBuffer, projectHeader.Length);
                var testDecrypt = crypto.Decrypt (testBuffer);

                if (!IsEncryptedBuffer (testDecrypt))
                {
                    //Debug.WriteLine ($"[SRPG] Project decrypted successfully with key: {key}");
                    if (key == "_dynamic")
                    {
                        var uuidBuffer = file.View.ReadBytes (header.ProjectOffset, 32);
                        var projectCrypto = new SrpgCrypto (key);
                        var decryptedUuidBuffer = projectCrypto.Decrypt (uuidBuffer);

                        var creatorUuid = new byte[16];
                        Array.Copy (decryptedUuidBuffer, 0, creatorUuid, 0, 16);

                        //Debug.WriteLine ($"[SRPG] Decrypted project byte@16: {decryptedUuidBuffer[16]:X2} (should be EE)");

                        var assetKeyBytes = MD5.Create().ComputeHash (creatorUuid);
                        var assetCrypto = new SrpgCrypto (assetKeyBytes);

                        //Debug.WriteLine ($"[SRPG] Creator UUID: {BitConverter.ToString (creatorUuid).Replace ("-", "")}");
                        //Debug.WriteLine ($"[SRPG] Derived asset key: {BitConverter.ToString (assetKeyBytes).Replace ("-", "")}");

                        if (TestKeyOnAssets (file, header, assetCrypto))
                        {
                            //Debug.WriteLine ($"[SRPG] Derived asset key validated!");
                        }
                        Debug.WriteLine ($"[SRPG] Using \"{key}\" for project and derived key for assets");
                        return new Tuple<SrpgCrypto, SrpgCrypto> (assetCrypto, projectCrypto);
                    }

                    Debug.WriteLine ($"[SRPG] Using \"{key}\" for assets and project");
                    return new Tuple<SrpgCrypto, SrpgCrypto>(crypto, crypto);
                }
            }

            Debug.WriteLine ($"[SRPG] No known key worked for project");

            var detectedKey = TryDetectKeyFromContent (file, header);
            if (detectedKey != null)
            {
                Debug.WriteLine ($"[SRPG] Detected key from asset content");
                return new Tuple<SrpgCrypto, SrpgCrypto>(detectedKey, detectedKey);
            }

            Debug.WriteLine ($"[SRPG] No encryption key detected");
            return new Tuple<SrpgCrypto, SrpgCrypto>(null, null);
        }

        private bool TestKeyOnAssets (ArcView file, DtsHeader header, SrpgCrypto crypto)
        {
            uint numSections = header.Version < 1140 ? 35u : 36u;
            uint headerSize = 24 + numSections * 4;

            //Debug.WriteLine ($"[SRPG] Testing key on {numSections} sections");

            for (int sectionIdx = 0;  sectionIdx < 16 ; sectionIdx++)
            {
                uint sectionOffset = file.View.ReadUInt32 (24 + sectionIdx * 4);
                sectionOffset = sectionOffset + headerSize;

                if (sectionOffset >= file.MaxOffset - 4)
                    continue;

                uint count = file.View.ReadUInt32 (sectionOffset);
                if (count == 0 || count > 1000)
                {
                    //Debug.WriteLine ($"[SRPG] Section {sectionIdx}: invalid count {count}");
                    continue;
                }

                //Debug.WriteLine ($"[SRPG] Section {sectionIdx}: {count} groups");

                if (sectionOffset + 8 > file.MaxOffset)
                    continue;

                uint firstFileOffsetRel = file.View.ReadUInt32 (sectionOffset + 4);
                if (firstFileOffsetRel == 0 || sectionOffset + firstFileOffsetRel > file.MaxOffset)
                {
                    //Debug.WriteLine ($"[SRPG] Section {sectionIdx}: invalid first file offset");
                    continue;
                }

                uint firstFileOffset = sectionOffset + firstFileOffsetRel;
                if (firstFileOffset + 20 > file.MaxOffset)
                    continue;

                uint nameLen = file.View.ReadUInt32 (firstFileOffset);
                if (nameLen == 0 || nameLen > 1000 || firstFileOffset + 4 + nameLen + 16 > file.MaxOffset)
                {
                    //Debug.WriteLine ($"[SRPG] Section {sectionIdx}: invalid name length {nameLen}");
                    continue;
                }

                uint metadataOffset = firstFileOffset + 4 + nameLen + 8;
                if (metadataOffset + 8 > file.MaxOffset)
                    continue;

                uint fileCount = file.View.ReadUInt32 (metadataOffset);
                if (fileCount == 0 || fileCount > 30000)
                {
                    //Debug.WriteLine ($"[SRPG] Section {sectionIdx}: invalid file count {fileCount}");
                    continue;
                }

                uint sizesOffset = metadataOffset + 4;
                if (sizesOffset + fileCount * 4 > file.MaxOffset)
                    continue;

                uint fileSize = file.View.ReadUInt32 (sizesOffset);
                if (fileSize < 8 || fileSize > file.MaxOffset)
                {
                    //Debug.WriteLine ($"[SRPG] Section {sectionIdx}: invalid file size {fileSize}");
                    continue;
                }

                uint contentOffset = sizesOffset + fileCount * 4;
                if (contentOffset + Math.Min (32, fileSize) > file.MaxOffset)
                    continue;

                var testData = file.View.ReadBytes (contentOffset, Math.Min (32, fileSize));
                var originalHex = BitConverter.ToString (testData, 0, Math.Min (8, testData.Length)).Replace ("-", "");

                var decrypted = crypto.Decrypt (testData.ToArray());
                var decryptedHex = BitConverter.ToString (decrypted, 0, Math.Min (8, decrypted.Length)).Replace ("-", "");

                //Debug.WriteLine ($"[SRPG] Section {sectionIdx} test: {originalHex} -> {decryptedHex}");

                if (IsValidFileSignature (decrypted))
                {
                    //Debug.WriteLine ($"[SRPG] Key verified on section {sectionIdx}!");
                    return true;
                }
            }

            //Debug.WriteLine ($"[SRPG] Key verification failed - no valid signatures found");
            return false;
        }

        private SrpgCrypto TryDetectKeyFromContent (ArcView file, DtsHeader header)
        {
            foreach (var key in KnownKeys)
            {
                var crypto = new SrpgCrypto (key);
                if (TestKeyOnAssets (file, header, crypto))
                    return crypto;
            }

            return null;
        }

        private bool IsValidFileSignature (byte[] data)
        {
            if (data.Length < 4) return false;

            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                return true; // PNG

            if (data[0] == 0xFF && data[1] == 0xD8)
                return true; // JPEG

            if (data[0] == 0x42 && data[1] == 0x4D)
                return true; // BMP

            if (data[0] == 0x4F && data[1] == 0x67 && data[2] == 0x67 && data[3] == 0x53)
                return true;  // OGG

            if (data.Length >= 12 && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
                return true;

            if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46)
                return true;

            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46)
                return true;

            if (data[0] == 0x49 && data[1] == 0x44 && data[2] == 0x33)
                return true;

            if (data[0] == 0xFF && (data[1] & 0xE0) == 0xE0)
                return true;

            if (data[0] == 0x4D && data[1] == 0x54 && data[2] == 0x68 && data[3] == 0x64)
                return true;

            if (data.Length >= 5 && data[0] == 0x00 && data[1] == 0x01 && data[2] == 0x00 && data[3] == 0x00)
                return true;

            return false;
        }

        private bool IsEncryptedBuffer (byte[] buffer)
        {
            if (buffer.Length < 28) return false;

            uint a = BitConverter.ToUInt32 (buffer, 16);
            uint b = BitConverter.ToUInt32 (buffer, 20);
            uint c = BitConverter.ToUInt32 (buffer, 24);

            return a > 255 || b > 1023 || c > 1023;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var srpgEntry = entry as SrpgEntry;
            if (srpgEntry == null || srpgEntry.Crypto == null)
                return base.OpenEntry (arc, entry);

            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            var decrypted = srpgEntry.Crypto.Decrypt (data);
            return new BinMemoryStream (decrypted, entry.Name);
        }

        public override ResourceOptions GetDefaultOptions()
        {
            return new SrpgOptions { Password = KnownKeys[0] };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            return GetDefaultOptions();
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

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "SRTS"))
                return null;

            uint version            = file.View.ReadUInt32 (4);
            uint totalSize          = file.View.ReadUInt32 (8);
            uint firstSectionOffset = file.View.ReadUInt32 (12);

            var sectionTable = new List<uint>();
            for (uint offset = 16; offset < firstSectionOffset; offset += 4)
                sectionTable.Add (file.View.ReadUInt32 (offset));

            var sectionPaths = GetSectionPaths();
            var dir = new List<Entry>();
            for (int i = 0; i < sectionTable.Count && i < sectionPaths.Count; i++)
                ProcessSection (file, sectionTable[i], sectionPaths[i], dir);

            return new ArcFile (file, this, dir);
        }

        private void ProcessSection (ArcView file, uint sectionOffset, string sectionPath, List<Entry> dir)
        {
            uint groupCount = file.View.ReadUInt32 (sectionOffset);

            bool isScriptSection = sectionPath == "Script";

            if (isScriptSection)
            {
                uint pos = sectionOffset + 4;
                for (uint i = 0; i < groupCount; i++)
                {
                    uint nameLength = file.View.ReadUInt32 (pos);
                    var nameBytes = file.View.ReadBytes (pos + 4, nameLength);
                    string scriptName = Encoding.Unicode.GetString (nameBytes).TrimEnd('\0');
                    scriptName = scriptName.Replace('\\', '/');
                    pos += 4 + nameLength;

                    uint scriptSize = file.View.ReadUInt32 (pos);
                    pos += 4;

                    if (scriptSize > 0)
                    {
                        var entry = new Entry
                        {
                            Name = $"{sectionPath}/{scriptName}",
                            Type = "script",
                            Offset = pos,
                            Size = scriptSize
                        };
                        dir.Add (entry);
                    }
                    pos += scriptSize;
                }
            }
            else
            {
                var groupOffsets = new List<uint>();
                for (uint i = 0; i < groupCount; i++)
                {
                    uint relOffset = file.View.ReadUInt32 (sectionOffset + 4 + i * 4);
                    groupOffsets.Add (sectionOffset + relOffset);
                }

                foreach (var groupOffset in groupOffsets)
                    ProcessGroup (file, groupOffset, sectionPath, dir);
            }
        }

        private void ProcessGroup (ArcView file, uint groupOffset, string sectionPath, List<Entry> dir)
        {
            uint nameLength = file.View.ReadUInt32 (groupOffset);
            var nameBytes = file.View.ReadBytes (groupOffset+4, nameLength);
            string groupName = Encoding.Unicode.GetString (nameBytes).TrimEnd('\0');

            uint offset = groupOffset + 4 + nameLength;
            uint meta1 = file.View.ReadUInt32 (offset);
            uint meta2 = file.View.ReadUInt32 (offset + 4);
            uint resourceCount = file.View.ReadUInt32 (offset + 8);
            if (!IsSaneCount (resourceCount))
                throw new InvalidFormatException ("Group files number is impossibly big.");

            offset += 12;
            var resourceSizes = new uint[resourceCount];
            for (uint i = 0; i < resourceCount; i++)
                resourceSizes[i] = file.View.ReadUInt32 (offset + i * 4);

            uint dataOffset = offset + resourceCount * 4;
            for (int i = 0; i < resourceCount; i++)
            {
                if (resourceSizes[i] == 0)
                    continue;

                var header16 = file.View.ReadBytes (dataOffset, Math.Min (16, resourceSizes[i]));
                var fileInfo = SRPGUtils.DetectFileType (header16);

                string resourceName = SRPGUtils.GenerateResourceName (groupName, i, (int)resourceCount);

                var entry = new Entry
                {
                    Name = $"{sectionPath}/{resourceName}{fileInfo.Item1}",
                    Type = fileInfo.Item2,
                    Offset = dataOffset,
                    Size = resourceSizes[i]
                };

                dir.Add (entry);

                dataOffset += resourceSizes[i];
            }
        }

        private List<string> GetSectionPathsA()
        {
            return new List<string>
            {
                "Graphics/mapchip", "Graphics/face", "Graphics/icon", "Graphics/motion",
                "Graphics/effect", "Graphics/weapon", "Graphics/bow", "Graphics/thumbnail", "Graphics/battleback",
                "Graphics/eventback", "Graphics/screenback", "Graphics/worldmap", "Graphics/eventstill",
                "Graphics/charillust", "Graphics/picture", "Audio/music", "Audio/sound", "UI/menuwindow",
                "UI/textwindow", "UI/title", "UI/number", "UI/bignumber", "UI/gauge", "UI/line", "UI/risecursor",
                "UI/mapcursor", "UI/pagecursor", "UI/selectcursor", "UI/scrollcursor","UI/panel", "UI/faceframe",
                "UI/screenframe", "Fonts", "Video", "Script", "Materials"
            };
        }

        private List<string> GetSectionPaths()
        {
            return new List<string>
            {
                "Graphics/mapchip", "Graphics/charchip", "Graphics/face", "Graphics/icon",
                "Graphics/motion", "Graphics/effect", "Graphics/weapon", "Graphics/bow",
                "Graphics/thumbnail", "Graphics/battleback",
                "Graphics/eventback", "Graphics/screenback", "Graphics/worldmap", "Graphics/eventstill",
                "Graphics/charillust", "Graphics/picture", "Audio/music", "Audio/sound", "UI/menuwindow",
                "UI/textwindow", "UI/title", "UI/number", "UI/bignumber", "UI/gauge", "UI/line", "UI/risecursor",
                "UI/mapcursor", "UI/pagecursor", "UI/selectcursor", "UI/scrollcursor","UI/panel", "UI/faceframe",
                "UI/screenframe", "Fonts", "Video", "Script", "Materials"
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

        public SrpgCrypto (byte[] secret, int cryptMode = -1)
        {
            m_key = new byte[secret.Length];
            Array.Copy (secret, m_key, secret.Length);

            if (cryptMode == -1)
                m_transform = new RC4Transform (m_key);
            else
            {
                var rc2 = new RC2CryptoServiceProvider
                {
                    Mode = CipherMode.CBC,
                    Padding = PaddingMode.None,
                    IV = new byte[8],
                    EffectiveKeySize = 128
                };
                m_transform = rc2.CreateDecryptor (m_key, rc2.IV);
            }
        }

        public SrpgCrypto (string secret, int cryptMode = -1)
        {
            var secretBytes = Encoding.Unicode.GetBytes (secret);
            m_key = MD5.Create().ComputeHash (secretBytes, 0, secret.Length);

            if (cryptMode == -1)
                m_transform = new RC4Transform (m_key);
            else
            {
                var rc2 = new RC2CryptoServiceProvider
                {
                    Mode = CipherMode.CBC,
                    Padding = PaddingMode.None,
                    IV = new byte[8],
                    EffectiveKeySize = 128
                };
                m_transform = rc2.CreateDecryptor (m_key, rc2.IV);
            }
        }

        public byte[] Decrypt (byte[] data)
        {
            using (var transform = new RC4Transform (m_key))
            {
                return transform.TransformFinalBlock (data, 0, data.Length);
            }
        }

        public byte[] Encrypt (byte[] data)
        {
            using (var transform = new RC4Transform (m_key))
            {
                return transform.TransformFinalBlock (data, 0, data.Length);
            }
        }
    }

    internal class RC4Transform : ICryptoTransform
    {
        private readonly byte[] m_state;
        private byte m_x;
        private byte m_y;

        public RC4Transform (byte[] key)
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

        public int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
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

        public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] output = new byte[inputCount];
            TransformBlock (inputBuffer, inputOffset, inputCount, output, 0);
            return output;
        }

        public void Dispose()
        {
            Array.Clear (m_state, 0, m_state.Length);
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

    public class Section
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
        public override string         Tag { get { return "DAT/SRPGLANG"; } }
        public override string Description { get { return "SRPG Studio language data"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  false; } }

        private static readonly string[] KnownKeys = { "_dummy" };

        private static byte[] XorKey = new byte[] {
            0x54, 0x94, 0xC1, 0x58, 0xF4, 0x4C, 0x92, 0x1B,
            0xAD, 0xE0, 0x9E, 0x3A, 0x49, 0xD1, 0xC9, 0x92
        };

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith ("language.dat"))
                return null;

            try
            {
                SrpgCrypto crypto = null;
                uint count = 0;
                uint langId = 0;
                foreach (string key in KnownKeys)
                {
                    crypto = new SrpgCrypto (key, 0);

                    var headerSize = Math.Min (16, file.MaxOffset);
                    var header = file.View.ReadBytes (0, (uint)headerSize);
                    XorBuffer (header);

                    var decHeader = crypto.Decrypt (header);
                    count = BitConverter.ToUInt32 (decHeader, 0);
                    langId = BitConverter.ToUInt32 (decHeader, 4);

                    if (!IsSaneCount ((int)count, 100000))
                        continue;
                }
                if (crypto == null)
                    return null;

                var data = file.View.ReadBytes (0, (uint)file.MaxOffset);
                XorBuffer (data);

                var decrypted = crypto.Decrypt (data);
                var dir = new List<Entry>();
                uint pos = 8;

                for (int i = 0; i < count && pos < decrypted.Length - 4; i++)
                {
                    uint strLen = BitConverter.ToUInt32 (decrypted, (int)pos);
                    if (strLen > decrypted.Length - pos - 4)
                        break;

                    var entry = new Entry
                    {
                        Name = $"string_{i:D5}.txt",
                        Type = "text",
                        Offset = pos + 4,
                        Size = strLen
                    };
                    dir.Add (entry);
                    pos += 4 + strLen;
                }

                if (dir.Count > 0)
                    return new LanguageArchive (file, this, dir, decrypted);
            }
            catch
            {
                // Not a valid language file
            }

            return null;
        }

        private static void XorBuffer (byte[] buffer)
        {
            for (int i = 0; i < buffer.Length && i < XorKey.Length; i++)
                buffer[i] ^= XorKey[i % XorKey.Length];
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var langArc = arc as LanguageArchive;
            if (langArc == null)
                return base.OpenEntry (arc, entry);

            var text = Encoding.Unicode.GetString (langArc.DecryptedData, (int)entry.Offset, (int)entry.Size);
            text = text.TrimEnd('\0');
            var bytes = Encoding.UTF8.GetBytes (text);
            return new BinMemoryStream (bytes, entry.Name);
        }
    }

    internal class LanguageArchive : ArcFile
    {
        public byte[] DecryptedData { get; }

        public LanguageArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] decryptedData)
            : base (arc, impl, dir)
        {
            DecryptedData = decryptedData;
        }
    }

    public class SrpgScheme : ResourceScheme
    {
        public Dictionary<string, string> KnownKeys { get; set; }
    }
}