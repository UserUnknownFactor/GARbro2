using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;

namespace GameRes.Formats.UnrealEngine
{
    internal class UAssetEntry : PackedEntry
    {
        public     string ClassName { get; set; }
        public    long ExportOffset { get; set; }
        public      long ExportSize { get; set; }
        public      int ExportIndex { get; set; }
        public FObjectExport Export { get; set; }

        public     bool IsInUbulk { get; set; }
        public    long DataOffset { get; set; }
        public       int DataSize { get; set; }
        public   int TextureWidth { get; set; }
        public  int TextureHeight { get; set; }
        public string PixelFormat { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class UAssetOpener : ArchiveFormat
    {
        public override         string Tag { get { return "UEXP/UE"; } }
        public override string Description { get { return "Unreal Engine 4/5 Asset"; } }
        public override     uint Signature { get { return 0; } }
        public override  bool IsHierarchic { get { return false; } }
        public override      bool CanWrite { get { return false; } }

        public UAssetOpener()
        {
            Extensions = new string[] { "uasset" };
        }

        public override ArcFile TryOpen(ArcView file)
        {
            if (file.MaxOffset < 0x20)
                return null;

            uint magic = file.View.ReadUInt32(0);
            if (magic != 0x9E2A83C1)
                return null;

            var baseName  = VFS.GetFileName(file.Name).Replace(".uasset", "");
            var uexpName  = VFS.ChangeFileName(file.Name, baseName + ".uexp");
            var ubulkName = VFS.ChangeFileName(file.Name, baseName + ".ubulk");

            if (!VFS.FileExists(uexpName) && !VFS.FileExists(ubulkName))
                return null;

            var reader = new UAssetReader(file);
            if (!reader.ReadHeader())
                return null;

            var dir = new List<Entry>();

            ArcView uexpView = null;
            ArcView ubulkView = null;
            if (VFS.FileExists(uexpName))
                uexpView = VFS.OpenView(uexpName);
            if (VFS.FileExists(ubulkName))
                ubulkView = VFS.OpenView(ubulkName);

            long currentUexpOffset = 0;
            for (int i = 0; i < reader.Exports.Count; i++)
            {
                var export = reader.Exports[i];
                string className = reader.GetObjectClassName(export);

                if (IsTextureClass(className) || IsSoundClass(className))
                {
                    var entry = new UAssetEntry
                    {
                        Name = export.ObjectName + GetExtensionForClass(className),
                        Type = IsTextureClass(className) ? "image" : "audio",
                        ClassName = className,
                        Offset = currentUexpOffset,
                        Size = (uint)export.SerialSize,
                        ExportOffset = currentUexpOffset,
                        ExportSize = export.SerialSize,
                        ExportIndex = i,
                        Export = export
                    };

                    if (IsTextureClass(className) && uexpView != null)
                    {
                        if (PreCalculateTextureData(uexpView, ubulkView, entry, reader))
                        {
                            int ddsHeaderSize = 4 + 124; // Magic + Header
                            entry.Size = (uint)(ddsHeaderSize + entry.DataSize);
                        }
                    }

                    dir.Add(entry);
                }

                currentUexpOffset += export.SerialSize;
            }

            if (dir.Count == 0)
            {
                uexpView?.Dispose();
                ubulkView?.Dispose();
                return null;
            }

            return new UAssetArchive(file, this, dir, reader, uexpName, uexpView, ubulkName, ubulkView);
        }

        private bool PreCalculateTextureData(ArcView uexpView, ArcView ubulkView, UAssetEntry entry, UAssetReader reader)
        {
            try
            {
                var exportData = uexpView.View.ReadBytes(entry.ExportOffset, Math.Min((uint)entry.ExportSize, 0x400));

                int metadataOffset = FindTexturePlatformData(exportData);
                if (metadataOffset < 0)
                    return false;

                using (var stream = new BinaryReader(new MemoryStream(exportData)))
                {
                    stream.BaseStream.Position = metadataOffset;

                    entry.TextureWidth = stream.ReadInt32();
                    entry.TextureHeight = stream.ReadInt32();
                    uint packedData = stream.ReadUInt32();

                    int strLen = stream.ReadInt32();
                    if (strLen <= 0 || strLen > 100)
                        return false;

                    var formatBytes = stream.ReadBytes(strLen);
                    entry.PixelFormat = Encoding.ASCII.GetString(formatBytes, 0, strLen - 1);

                    int firstMipToSerialize = stream.ReadInt32();
                    int mipCount = stream.ReadInt32();

                    // Read bulk data info
                    long cookedPos = stream.BaseStream.Position;
                    byte possibleCooked = stream.ReadByte();
                    if (possibleCooked != 0x01 && possibleCooked != 0x00)
                        stream.BaseStream.Position = cookedPos;

                    uint bulkDataFlags = stream.ReadUInt32();
                    int elementCount = stream.ReadInt32();
                    int sizeOnDisk = stream.ReadInt32();

                    long offsetInFile;
                    if (reader.Summary.FileVersionUE4 >= 326)
                        offsetInFile = stream.ReadInt64();
                    else
                        offsetInFile = stream.ReadInt32();

                    entry.DataSize = elementCount;

                    // Determine where the data is located
                    if ((bulkDataFlags & 0x0100) != 0) // BULKDATA_PayloadInSeperateFile
                    {
                        if (ubulkView != null && offsetInFile >= 0)
                        {
                            entry.IsInUbulk = true;
                            entry.DataOffset = offsetInFile;
                        }
                        else
                            return false;
                    }
                    else if ((bulkDataFlags & 0x0020) != 0) // BULKDATA_PayloadAtEndOfFile
                    {
                        entry.IsInUbulk = false;
                        entry.DataOffset = uexpView.MaxOffset - elementCount;
                    }
                    else if ((bulkDataFlags & 0x40) != 0) // BULKDATA_Unused - inline data
                    {
                        entry.IsInUbulk = false;
                        entry.DataOffset = entry.ExportOffset + stream.BaseStream.Position;
                    }
                    else if (offsetInFile > 0)
                    {
                        if (ubulkView != null && offsetInFile < ubulkView.MaxOffset)
                        {
                            entry.IsInUbulk = true;
                            entry.DataOffset = offsetInFile;
                        }
                        else
                        {
                            entry.IsInUbulk = false;
                            entry.DataOffset = offsetInFile;
                        }
                    }
                    else
                    {
                        entry.IsInUbulk = false;
                        entry.DataOffset = entry.ExportOffset + stream.BaseStream.Position;
                    }

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var uarc = arc as UAssetArchive;
            var uentry = entry as UAssetEntry;

            if (uarc == null || uentry == null)
                return base.OpenEntry(arc, entry);
            else if (uentry.Type == "image")
                return OpenTexture(uarc, uentry);
            else if (uentry.Type == "audio")
                return OpenSound(uarc, uentry);

            return OpenExportData(uarc, uentry);
        }

        private Stream OpenExportData(UAssetArchive arc, UAssetEntry entry)
        {
            if (entry.ExportSize > 0)
            {
                var data = arc.UexpView.View.ReadBytes(0, entry.ExportSize + entry.ExportOffset);
                return new BinMemoryStream(data);
            }
            return Stream.Null;
        }

        private Stream OpenTexture(UAssetArchive arc, UAssetEntry entry)
        {
            byte[] mipData;

            if (entry.IsInUbulk)
            {
                if (arc.UbulkView == null)
                    throw new Exception($"Texture data is in a missing {entry.Name.Replace(".uasset", ".ubulk")} file");
                mipData = arc.UbulkView.View.ReadBytes(entry.DataOffset, CalculateTextureSize(entry));
            }
            else
            {
                if (arc.UexpView == null)
                    throw new Exception($"Texture data is in a missing {entry.Name.Replace(".uasset", ".uexp")} file");
                mipData = arc.UexpView.View.ReadBytes(entry.DataOffset, CalculateTextureSize(entry));
            }

            if (mipData == null || mipData.Length == 0)
                throw new Exception("Failed to read texture data");

            return CreateProperDDS(entry.TextureWidth, entry.TextureHeight, entry.PixelFormat, mipData);
        }

        private long CalculateTextureSize(UAssetEntry entry)
        {
            switch (entry.PixelFormat)
            {
            case "PF_DXT1":
                return Math.Max(1, (entry.TextureWidth + 3) / 4) * Math.Max(1, (entry.TextureHeight + 3) / 4) * 8;
            case "PF_DXT3":
            case "PF_DXT5":
                return Math.Max(1, (entry.TextureWidth + 3) / 4) * Math.Max(1, (entry.TextureHeight + 3) / 4) * 16;
            case "PF_B8G8R8A8":
            case "PF_A8R8G8B8":
            case "PF_R8G8B8A8":
                return entry.TextureWidth * entry.TextureHeight * 4;
            case "PF_G8":
                return entry.TextureWidth * entry.TextureHeight;
            case "PF_FloatRGB":
                return entry.TextureWidth * entry.TextureHeight * 12;
            case "PF_FloatRGBA":
                return entry.TextureWidth * entry.TextureHeight * 16;
            default:
                return entry.TextureWidth * entry.TextureHeight * 4;
            }
        }

        private int FindTexturePlatformData(byte[] data)
        {
            // Look for the FTexturePlatformData structure
            // It starts with SizeX, SizeY, NumSlices(packed), then pixel format string
            for (int i = 0; i <= data.Length - 20; i++)
            {
                int width = BitConverter.ToInt32(data, i);
                int height = BitConverter.ToInt32(data, i + 4);
                if (width <= 0 || width > 8192 || height <= 0 || height > 8192)
                    continue;

                // Skip packed data (NumSlices) at i+8

                if (i + 12 >= data.Length - 4)
                    continue;

                int strLen = BitConverter.ToInt32(data, i + 12);

                if (strLen < 4 || strLen > 128 || i + 16 + strLen > data.Length)
                    continue;

                if (i + 16 + 3 <= data.Length)
                {
                    if (data[i + 16] == 'P' && data[i + 17] == 'F' && data[i + 18] == '_')
                        return i;
                }
            }

            return -1;
        }

        private Stream CreateProperDDS(int width, int height, string pixelFormat, byte[] pixelData)
        {
            var ddsData = new MemoryStream();
            var writer = new BinaryWriter(ddsData);

            // DDS Magic
            writer.Write(0x20534444); // "DDS "
            writer.Write(124); // dwSize

            // dwFlags
            uint flags = 0x00000001 | 0x00000002 | 0x00000004 | 0x00001000; // CAPS | HEIGHT | WIDTH | PIXELFORMAT

            bool isCompressed = pixelFormat.StartsWith("PF_DXT") || pixelFormat.StartsWith("PF_BC");
            if (!isCompressed)
                flags |= 0x00000008; // PITCH
            else
                flags |= 0x00080000; // LINEARSIZE

            writer.Write(flags);
            writer.Write((uint)height); // dwHeight  
            writer.Write((uint)width);  // dwWidth

            // dwPitchOrLinearSize
            if (!isCompressed)
            {
                int pitch = width * 4; // 4 bytes per pixel for BGRA
                writer.Write(pitch);
            }
            else
            {
                int blockSize = (pixelFormat == "PF_DXT1") ? 8 : 16;
                int linearSize = Math.Max(1, ((width + 3) / 4)) * Math.Max(1, ((height + 3) / 4)) * blockSize;
                writer.Write(linearSize);
            }

            writer.Write(0); // dwDepth
            writer.Write(1); // dwMipMapCount

            // dwReserved1[11]
            for (int i = 0; i < 11; i++)
                writer.Write(0);

            // DDS_PIXELFORMAT (32 bytes)
            writer.Write(32); // dwSize

            switch (pixelFormat)
            {
            case "PF_B8G8R8A8":
                writer.Write(0x00000041); // dwFlags = DDPF_RGB | DDPF_ALPHAPIXELS
                writer.Write(0); // dwFourCC
                writer.Write(32); // dwRGBBitCount
                writer.Write(0x00FF0000); // dwRBitMask
                writer.Write(0x0000FF00); // dwGBitMask
                writer.Write(0x000000FF); // dwBBitMask
                writer.Write(0xFF000000); // dwABitMask
                break;

            case "PF_DXT1":
                writer.Write(0x00000004); // dwFlags = DDPF_FOURCC
                writer.Write(0x31545844); // dwFourCC = "DXT1"
                for (int i = 0; i < 5; i++) writer.Write(0);
                break;

            case "PF_DXT3":
                writer.Write(0x00000004); // dwFlags = DDPF_FOURCC
                writer.Write(0x33545844); // dwFourCC = "DXT3"
                for (int i = 0; i < 5; i++) writer.Write(0);
                break;

            case "PF_DXT5":
                writer.Write(0x00000004); // dwFlags = DDPF_FOURCC
                writer.Write(0x35545844); // dwFourCC = "DXT5"
                for (int i = 0; i < 5; i++) writer.Write(0);
                break;

            case "PF_BC5":
                writer.Write(0x00000004); // dwFlags = DDPF_FOURCC
                writer.Write(0x32495441); // dwFourCC = "ATI2"
                for (int i = 0; i < 5; i++) writer.Write(0);
                break;

            case "PF_BC7":
                writer.Write(0x00000004); // dwFlags = DDPF_FOURCC
                writer.Write(0x20374342); // dwFourCC = "BC7 " (space at end)
                for (int i = 0; i < 5; i++) writer.Write(0);
                break;

            default:
                // Default to BGRA
                writer.Write(0x00000041); // dwFlags = DDPF_RGB | DDPF_ALPHAPIXELS
                writer.Write(0); // dwFourCC
                writer.Write(32); // dwRGBBitCount
                writer.Write(0x00FF0000); // dwRBitMask
                writer.Write(0x0000FF00); // dwGBitMask
                writer.Write(0x000000FF); // dwBBitMask
                writer.Write(0xFF000000); // dwABitMask
                break;
            }

            // dwCaps
            writer.Write(0x00001000); // DDSCAPS_TEXTURE
            writer.Write(0); // dwCaps2
            writer.Write(0); // dwCaps3
            writer.Write(0); // dwCaps4
            writer.Write(0); // dwReserved2

            // Write pixel data
            writer.Write(pixelData);

            ddsData.Position = 0;
            return ddsData;
        }

        private bool IsTextureClass(string className)
        {
            return className == "Texture2D" ||
                   className == "TextureCube" ||
                   className == "VolumeTexture" ||
                   className == "TextureRenderTarget2D";
        }

        private bool IsSoundClass(string className)
        {
            return className == "SoundWave" ||
                   className == "SoundCue" ||
                   className == "SoundNodeWave";
        }

        private string GetExtensionForClass(string className)
        {
            if (IsTextureClass(className))
                return ".dds";
            if (IsSoundClass(className))
                return ".ogg";
            return ".dat";
        }

        private Stream OpenSound(UAssetArchive arc, UAssetEntry entry)
        {
            var sound = new USoundWave();
            var stream = arc.UexpView.CreateStream(0, entry.ExportSize + entry.ExportOffset);
            sound.Deserialize(stream, arc.Reader, entry.Export);
            stream.Position = entry.ExportOffset;

            if (sound.RawData != null && sound.RawData.Length > 0)
                return CreateWavStream(sound.RawData, sound.SampleRate, sound.NumChannels);

            else if (sound.CompressedData != null && sound.CompressedData.Length > 0)
            {
                if (sound.CompressionFormat == "OGG" || sound.CompressionFormat == "OPUS")
                    return new BinMemoryStream(sound.CompressedData);
                else
                    return new BinMemoryStream(sound.CompressedData);
            }

            return Stream.Null;
        }

        private Stream CreateWavStream(byte[] pcmData, int sampleRate, int channels)
        {
            if (sampleRate == 0) sampleRate = 44100;
            if (channels == 0) channels = 2;

            var wavStream = new MemoryStream();
            var writer = new BinaryWriter(wavStream);

            // Write WAV header
            writer.Write(0x46464952); // "RIFF"
            writer.Write(36 + pcmData.Length);
            writer.Write(0x45564157); // "WAVE"
            writer.Write(0x20746D66); // "fmt "
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);
            writer.Write(0x61746164); // "data"
            writer.Write(pcmData.Length);
            writer.Write(pcmData);

            wavStream.Position = 0;
            return wavStream;
        }
    }

    internal class UAssetArchive : ArcFile
    {
        public UAssetReader Reader { get; }
        public string UexpFileName { get; }
        public ArcView UexpView { get; }
        public string UbulkFileName { get; }
        public ArcView UbulkView { get; }

        public UAssetArchive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir,
                            UAssetReader reader, string uexpName, ArcView uexpView,
                            string ubulkName = null, ArcView ubulkView = null)
            : base(arc, impl, dir)
        {
            Reader = reader;
            UexpFileName = uexpName;
            UexpView = uexpView;
            UbulkFileName = ubulkName;
            UbulkView = ubulkView;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UexpView?.Dispose();
                UbulkView?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    enum EPackageFlags : uint
    {
        PKG_None = 0x00000000,
        PKG_FilterEditorOnly = 0x80000000
    }

    class UAssetReader
    {
        private ArcView _file;

        public List<FObjectExport> Exports { get; private set; }
        public List<FObjectImport> Imports { get; private set; }
        public List<FNameEntry> NameTable { get; private set; }
        public FPackageFileSummary Summary { get; private set; }

        public UAssetReader(ArcView file)
        {
            _file = file;
            Exports = new List<FObjectExport>();
            Imports = new List<FObjectImport>();
            NameTable = new List<FNameEntry>();
        }

        public bool ReadHeader()
        {
            long offset = 0;
            try
            {
                Summary = new FPackageFileSummary();
                Summary.Tag = _file.View.ReadUInt32(offset);
                offset += 4;

                var legacyFileVersion = _file.View.ReadInt32(offset);
                offset += 4;

                if (legacyFileVersion >= 0)
                    return false;

                if (legacyFileVersion != -4)
                {
                    var legacyUE3Version = _file.View.ReadInt32(offset);
                    offset += 4;
                }

                int fileVersionUE4 = _file.View.ReadInt32(offset);
                offset += 4;

                int fileVersionUE5 = 0;
                if (legacyFileVersion <= -8)
                {
                    fileVersionUE5 = _file.View.ReadInt32(offset);
                    offset += 4;
                }

                int fileVersionLicenseeUE = _file.View.ReadInt32(offset);
                offset += 4;

                int customVersionCount = _file.View.ReadInt32(offset);
                offset += 4;

                for (int i = 0; i < customVersionCount; i++)
                {
                    offset += 20;
                }

                bool bUnversioned = (fileVersionUE4 == 0 && fileVersionUE5 == 0 && fileVersionLicenseeUE == 0);

                if (bUnversioned)
                {
                    Summary.FileVersionUE4 = 522; // VER_UE4_AUTOMATIC_VERSION
                    Summary.FileVersionUE5 = 0;
                    Summary.FileVersionLicenseeUE = 0;
                    Summary.bUnversioned = true;
                }
                else
                {
                    Summary.FileVersionUE4 = fileVersionUE4;
                    Summary.FileVersionUE5 = fileVersionUE5;
                    Summary.FileVersionLicenseeUE = fileVersionLicenseeUE;
                    Summary.bUnversioned = false;
                }

                Summary.TotalHeaderSize = _file.View.ReadInt32(offset);
                offset += 4;

                Summary.PackageName = ReadFString(offset, out int nameLen);
                offset += nameLen;

                Summary.PackageFlags = (EPackageFlags)_file.View.ReadUInt32(offset);
                offset += 4;

                Summary.NameCount = _file.View.ReadInt32(offset);
                offset += 4;
                Summary.NameOffset = _file.View.ReadInt32(offset);
                offset += 4;

                // Localization ID - only if NOT PKG_FilterEditorOnly
                if (!Summary.PackageFlags.HasFlag(EPackageFlags.PKG_FilterEditorOnly))
                {
                    if (Summary.FileVersionUE4 >= 516) // ADDED_PACKAGE_SUMMARY_LOCALIZATION_ID
                    {
                        string localizationId = ReadFString(offset, out int locLen);
                        offset += locLen;
                    }
                }

                if (Summary.FileVersionUE4 >= 459) // SERIALIZE_TEXT_IN_PACKAGES
                {
                    var gatherableTextDataCount = _file.View.ReadInt32(offset);
                    offset += 4;
                    var gatherableTextDataOffset = _file.View.ReadInt32(offset);
                    offset += 4;
                }

                Summary.ExportCount = _file.View.ReadInt32(offset);
                offset += 4;
                Summary.ExportOffset = _file.View.ReadInt32(offset);
                offset += 4;

                Summary.ImportCount = _file.View.ReadInt32(offset);
                offset += 4;
                Summary.ImportOffset = _file.View.ReadInt32(offset);
                offset += 4;

                ReadNameTable();
                ReadImports();
                ReadExports();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UAssetReader.ReadHeader error: {ex}");
                return false;
            }
        }

        private void ReadNameTable()
        {
            long offset = Summary.NameOffset;
            bool bUnversioned = (Summary.FileVersionUE4 == 0 && Summary.FileVersionUE5 == 0 && Summary.FileVersionLicenseeUE == 0);

            for (int i = 0; i < Summary.NameCount; i++)
            {
                var entry = new FNameEntry();
                entry.Name = ReadFString(offset, out int strLen);
                offset += strLen;

                // Hash fields - skip for unversioned
                if (!bUnversioned)
                {
                    if (Summary.FileVersionUE4 >= 504)
                    {
                        offset += 4; // Skip case-preserving hash
                    }
                    else if (Summary.FileVersionUE4 >= 125)
                    {
                        offset += 4; // Skip non-case-preserving hash
                    }
                }

                NameTable.Add(entry);
            }
        }

        private void ReadImports()
        {
            long offset = Summary.ImportOffset;

            System.Diagnostics.Debug.WriteLine($"Reading {Summary.ImportCount} imports from 0x{offset:X}");

            for (int i = 0; i < Summary.ImportCount; i++)
            {
                // Check if we have enough bytes for an import (28 bytes minimum)
                if (offset + 28 > _file.MaxOffset)
                {
                    System.Diagnostics.Debug.WriteLine($"Import {i}: Not enough data at offset 0x{offset:X}");
                    break;
                }

                var import = new FObjectImport();

                import.ClassPackage = ReadFName(offset);
                offset += 8;

                import.ClassName = ReadFName(offset);
                offset += 8;

                import.OuterIndex = _file.View.ReadInt32(offset);
                offset += 4;

                import.ObjectName = ReadFName(offset);
                offset += 8;

                System.Diagnostics.Debug.WriteLine($"Import {i}: {import.ObjectName} (Class: {import.ClassName})");

                Imports.Add(import);
            }
        }

        private void ReadExports()
        {
            long offset = Summary.ExportOffset;

            // Check if this is an unversioned file
            bool bUnversioned = (Summary.FileVersionUE4 == 0 && Summary.FileVersionUE5 == 0 && Summary.FileVersionLicenseeUE == 0);

            for (int i = 0; i < Summary.ExportCount; i++)
            {
                var export = new FObjectExport();

                export.ClassIndex = _file.View.ReadInt32(offset);
                offset += 4;

                export.SuperIndex = _file.View.ReadInt32(offset);
                offset += 4;

                // Template index - skip for unversioned
                if (!bUnversioned && Summary.FileVersionUE4 >= 475)
                {
                    export.TemplateIndex = _file.View.ReadInt32(offset);
                    offset += 4;
                }

                export.OuterIndex = _file.View.ReadInt32(offset);
                offset += 4;

                export.ObjectName = ReadFName(offset);
                offset += 8;

                export.Save = _file.View.ReadUInt32(offset);
                offset += 4;

                // Serial size and offset (64-bit in UE4+)
                export.SerialSize = _file.View.ReadInt64(offset);
                offset += 8;

                export.SerialOffset = _file.View.ReadInt64(offset);
                offset += 8;

                // Flags
                export.bForcedExport = _file.View.ReadInt32(offset) != 0;
                offset += 4;

                export.bNotForClient = _file.View.ReadInt32(offset) != 0;
                offset += 4;

                export.bNotForServer = _file.View.ReadInt32(offset) != 0;
                offset += 4;

                // Skip PackageGuid for unversioned or old versions
                if (!bUnversioned && Summary.FileVersionUE5 < 1005) // REMOVE_OBJECT_EXPORT_PACKAGE_GUID
                {
                    offset += 16; // Package GUID
                }
                else if (bUnversioned)
                {
                    offset += 16; // Unversioned files still have this
                }

                // Skip IsInheritedInstance for UE5.7+
                if (!bUnversioned && Summary.FileVersionUE5 >= 1006) // TRACK_OBJECT_EXPORT_IS_INHERITED
                {
                    offset += 1; // bool
                }

                export.PackageFlags = _file.View.ReadUInt32(offset);
                offset += 4;

                if (!bUnversioned)
                {
                    if (Summary.FileVersionUE4 >= 365)
                    {
                        export.bNotAlwaysLoadedForEditorGame = _file.View.ReadInt32(offset) != 0;
                        offset += 4;
                    }

                    if (Summary.FileVersionUE4 >= 485)
                    {
                        export.bIsAsset = _file.View.ReadInt32(offset) != 0;
                        offset += 4;
                    }

                    if (Summary.FileVersionUE4 >= 507)
                    {
                        // Skip preload dependencies
                        offset += 20; // 5 int32s
                    }
                }

                Exports.Add(export);
            }
        }

        private string ReadFString(long offset, out int totalLen)
        {
            if (offset + 4 > _file.MaxOffset)
            {
                totalLen = 0;
                return "";
            }

            int length = _file.View.ReadInt32(offset);
            totalLen = 4;

            if (length == 0)
                return "";

            if (length < 0) // Unicode string
            {
                length = -length;
                if (length > 0 && length < 10000)
                {
                    uint bytesToRead = (uint)(length * 2);
                    if (offset + 4 + bytesToRead <= _file.MaxOffset)
                    {
                        var bytes = _file.View.ReadBytes(offset + 4, bytesToRead);
                        totalLen += length * 2;
                        return Encoding.Unicode.GetString(bytes, 0, Math.Min(bytes.Length, (length - 1) * 2));
                    }
                }
                return "";
            }
            else // ASCII string
            {
                if (length > 0 && length < 10000)
                {
                    uint bytesToRead = (uint)length;
                    if (offset + 4 + bytesToRead <= _file.MaxOffset)
                    {
                        var bytes = _file.View.ReadBytes(offset + 4, bytesToRead);
                        totalLen += length;
                        return Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, length - 1));
                    }
                }
                return "";
            }
        }

        private string ReadFName(long offset)
        {
            int nameIndex = _file.View.ReadInt32(offset);
            int number = _file.View.ReadInt32(offset + 4);

            if (nameIndex >= 0 && nameIndex < NameTable.Count)
            {
                string name = NameTable[nameIndex].Name;
                if (number > 0)
                    name += "_" + (number - 1);
                return name;
            }
            return "";
        }

        public string GetObjectClassName(FObjectExport export)
        {
            if (export.ClassIndex < 0)
            {
                int importIndex = -export.ClassIndex - 1;
                if (importIndex >= 0 && importIndex < Imports.Count)
                {
                    var import = Imports[importIndex];
                    if (import.ClassName == "Class")
                    {
                        return import.ObjectName;
                    }
                    return import.ClassName;
                }
            }
            else if (export.ClassIndex > 0)
            {
                int exportIndex = export.ClassIndex - 1;
                if (exportIndex >= 0 && exportIndex < Exports.Count)
                    return Exports[exportIndex].ObjectName;
            }
            return "";
        }
    }

    #region Supporting classes

    internal class FPackageFileSummary
    {
        public uint Tag;
        public int FileVersionUE4;
        public int FileVersionUE5;
        public int FileVersionLicenseeUE;
        public bool bUnversioned;
        public int TotalHeaderSize;
        public string PackageName;
        public EPackageFlags PackageFlags;
        public int NameCount;
        public int NameOffset;
        public int ExportCount;
        public int ExportOffset;
        public int ImportCount;
        public int ImportOffset;
    }

    internal class FObjectExport
    {
        public int ClassIndex;
        public int SuperIndex;
        public int TemplateIndex;
        public int OuterIndex;
        public string ObjectName;
        public uint Save;
        public long SerialSize;
        public long SerialOffset;
        public bool bForcedExport;
        public bool bNotForClient;
        public bool bNotForServer;
        public uint PackageFlags;
        public bool bNotAlwaysLoadedForEditorGame;
        public bool bIsAsset;
    }

    internal class FObjectImport
    {
        public string ClassPackage;
        public string ClassName;
        public int OuterIndex;
        public string ObjectName;
    }

    internal class FNameEntry
    {
        public string Name;
    }

    #endregion


    internal class USoundWave
    {
        public byte[] RawData { get; set; }
        public byte[] CompressedData { get; set; }
        public string CompressionFormat { get; set; }
        public int SampleRate { get; set; }
        public int NumChannels { get; set; }
        public bool bStreaming { get; set; }
        public bool bCooked { get; set; }

        public void Deserialize(Stream stream, UAssetReader assetReader, FObjectExport export)
        {
            using (var reader = new BinaryReader(stream))
            {
                if (assetReader.Summary.bUnversioned)
                {
                    // For unversioned files, we need to skip unknown properties differently
                    // Try to find the cooked flag or sound data pattern

                    // Look for a reasonable pattern - this is heuristic
                    bool foundData = false;
                    while (reader.BaseStream.Position < reader.BaseStream.Length - 4)
                    {
                        long pos = reader.BaseStream.Position;
                        byte possibleCookedFlag = reader.ReadByte();

                        if (possibleCookedFlag == 0 || possibleCookedFlag == 1)
                        {
                            // Check if next looks like bulk data or format container
                            int nextInt = reader.ReadInt32();

                            // If it looks like a format count (usually 1-5)
                            if (nextInt >= 0 && nextInt <= 10)
                            {
                                reader.BaseStream.Position = pos;
                                bCooked = possibleCookedFlag != 0;
                                foundData = true;
                                break;
                            }
                        }

                        reader.BaseStream.Position = pos + 1;
                    }

                    if (!foundData)
                    {
                        // Assume cooked
                        bCooked = true;
                        reader.BaseStream.Position = 0;
                    }
                    else
                    {
                        // Skip the cooked flag we just read
                        reader.ReadByte();
                    }
                }
                else
                {
                    // Standard versioned processing
                    SkipUObjectProperties(reader, assetReader);

                    // Read cooked flag
                    bCooked = reader.ReadByte() != 0;

                    // Handle compression name (for certain versions)
                    if (assetReader.Summary.FileVersionUE4 >= 478) // VER_UE4_SOUND_COMPRESSION_TYPE_ADDED
                    {
                        if (assetReader.Summary.FileVersionUE4 < 508) // approximate RemoveSoundWaveCompressionName
                        {
                            // Read and skip compression name
                            ReadFName(reader, assetReader);
                        }
                    }
                }

                // Common processing for both versioned and unversioned
                if (!bStreaming)
                {
                    if (bCooked)
                    {
                        // Read compressed format data (FFormatContainer)
                        ReadCompressedFormatData(reader, assetReader);
                    }
                    else
                    {
                        // Read raw PCM data
                        ReadBulkData(reader, assetReader);
                    }
                }

                // Read CompressedDataGuid
                if (reader.BaseStream.Position + 16 <= reader.BaseStream.Length)
                {
                    reader.ReadBytes(16); // Skip GUID
                }

                if (bStreaming)
                {
                    // Read streaming chunks
                    ReadStreamingData(reader, assetReader);
                }
            }
        }

        private void SkipUObjectProperties(BinaryReader reader, UAssetReader assetReader)
        {
            // Read properties until we hit "None"
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                var propertyName = ReadFName(reader, assetReader);
                if (propertyName == "None" || string.IsNullOrEmpty(propertyName))
                    break;

                var propertyType = ReadFName(reader, assetReader);

                long propertySize = reader.ReadInt64();

                // Read array index
                reader.ReadInt32();

                // Store specific properties we're interested in
                long dataStart = reader.BaseStream.Position;

                if (propertyName == "NumChannels" && propertyType == "IntProperty")
                    NumChannels = reader.ReadInt32();
                else if (propertyName == "SampleRate" && propertyType == "IntProperty")
                    SampleRate = reader.ReadInt32();
                else if (propertyName == "bStreaming" && propertyType == "BoolProperty")
                    bStreaming = reader.ReadByte() != 0;
                else // Skip property data
                    reader.BaseStream.Position = dataStart + propertySize;

                // Skip property guid if present
                if (assetReader.Summary.FileVersionUE4 >= 282) // VER_UE4_PROPERTY_GUID_IN_PROPERTY_TAG
                {
                    var hasPropertyGuid = reader.ReadByte();
                    if (hasPropertyGuid != 0)
                        reader.ReadBytes(16); // Property GUID
                }
            }
        }

        private void ReadCompressedFormatData(BinaryReader reader, UAssetReader assetReader)
        {
            // Read array of FSoundFormatData
            int count = reader.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                var formatName = ReadFName(reader, assetReader);
                CompressionFormat = formatName;

                var bulkData = new FByteBulkData();
                bulkData.Deserialize(reader, assetReader);

                // Use the first valid format
                if (CompressedData == null && bulkData.BulkData != null && bulkData.BulkData.Length > 0)
                    CompressedData = bulkData.BulkData;
            }
        }

        private void ReadBulkData(BinaryReader reader, UAssetReader assetReader)
        {
            var bulkData = new FByteBulkData();
            bulkData.Deserialize(reader, assetReader);
            RawData = bulkData.BulkData;
        }

        private void ReadStreamingData(BinaryReader reader, UAssetReader assetReader)
        {
            int numChunks = reader.ReadInt32();
            var streamedFormat = ReadFName(reader, assetReader);
            CompressionFormat = streamedFormat;

            var chunks = new List<byte>();
            for (int i = 0; i < numChunks; i++)
            {
                var chunk = ReadStreamedAudioChunk(reader, assetReader);
                if (chunk != null)
                    chunks.AddRange(chunk);
            }

            if (chunks.Count > 0)
                CompressedData = chunks.ToArray();
        }

        private byte[] ReadStreamedAudioChunk(BinaryReader reader, UAssetReader assetReader)
        {
            bool cooked = reader.ReadByte() != 0;

            var bulkData = new FByteBulkData();
            bulkData.Deserialize(reader, assetReader);

            if (assetReader.Summary.FileVersionUE4 >= 4)
            {
                int dataSize = reader.ReadInt32();

                if (assetReader.Summary.FileVersionUE4 >= 19)
                {
                    int audioDataSize = reader.ReadInt32();
                }
            }

            return bulkData.BulkData;
        }

        private string ReadFName(BinaryReader reader, UAssetReader assetReader)
        {
            var nameIndex = reader.ReadInt32();
            var number = reader.ReadInt32();

            if (nameIndex >= 0 && nameIndex < assetReader.NameTable.Count)
            {
                var name = assetReader.NameTable[nameIndex].Name;
                if (number > 0)
                    name += "_" + (number - 1);
                return name;
            }
            return "";
        }
    }

    internal class FByteBulkData
    {
        public uint BulkDataFlags { get; set; }
        public long ElementCount { get; set; }
        public long BulkDataSizeOnDisk { get; set; }
        public long BulkDataOffsetInFile { get; set; }
        public byte[] BulkData { get; set; }

        // Bulk data flags
        private const uint BULKDATA_PayloadAtEndOfFile = 0x0020;
        private const uint BULKDATA_Unused = 0x0040;
        private const uint BULKDATA_PayloadInSeperateFile = 0x0100;
        private const uint BULKDATA_ForceInlinePayload = 0x0200;
        private const uint BULKDATA_OptionalPayload = 0x0800;

        public void Deserialize(BinaryReader reader, UAssetReader assetReader)
        {
            BulkDataFlags = reader.ReadUInt32();

            if (assetReader.Summary.FileVersionUE4 >= 198) // VER_UE4_64BIT_BULK_DATA_SIZE
            {
                ElementCount = reader.ReadInt64();
                BulkDataSizeOnDisk = reader.ReadInt64();
            }
            else
            {
                ElementCount = reader.ReadInt32();
                BulkDataSizeOnDisk = reader.ReadInt32();
            }

            if (assetReader.Summary.FileVersionUE4 >= 326) // VER_UE4_BULKDATA_AT_LARGE_OFFSETS
                BulkDataOffsetInFile = reader.ReadInt64();
            else
                BulkDataOffsetInFile = reader.ReadInt32();

            // Read inline data if present
            if ((BulkDataFlags & BULKDATA_ForceInlinePayload) != 0 && BulkDataSizeOnDisk > 0)
                BulkData = reader.ReadBytes((int)BulkDataSizeOnDisk);
        }
    }
}