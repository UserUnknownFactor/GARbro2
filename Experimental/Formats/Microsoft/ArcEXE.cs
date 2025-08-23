using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Markup;

namespace GameRes.Formats.Microsoft
{
    [Export(typeof(ArchiveFormat))]
    [ExportMetadata("Priority", -1)]
    public class ExeOpener : ArchiveFormat
    {
        public override string         Tag => "EXE";
        public override string Description => "Windows executable resources";
        public override uint     Signature =>  0;
        public override bool  IsHierarchic =>  true;
        public override bool      CanWrite =>  false;

        public ExeOpener()
        {
            Extensions = new[] { "exe", "dll", "scr", "sys", "drv", "ocx", "cpl" };
        }

        // Comprehensive RT_ type mapping
        static readonly Dictionary<string, string> RuntimeTypeMap = new Dictionary<string, string>()
        {
            { "#1",  "RT_CURSOR" },
            { "#2",  "RT_BITMAP" },
            { "#3",  "RT_ICON" },
            { "#4",  "RT_MENU" },
            { "#5",  "RT_DIALOG" },
            { "#6",  "RT_STRING" },
            { "#7",  "RT_FONTDIR" },
            { "#8",  "RT_FONT" },
            { "#9",  "RT_ACCELERATOR" },
            { "#10", "RT_RCDATA" },
            { "#11", "RT_MESSAGETABLE" },
            { "#12", "RT_GROUP_CURSOR" },
            { "#14", "RT_GROUP_ICON" },
            { "#16", "RT_VERSION" },
            { "#17", "RT_DLGINCLUDE" },
            { "#19", "RT_PLUGPLAY" },
            { "#20", "RT_VXD" },
            { "#21", "RT_ANICURSOR" },
            { "#22", "RT_ANIICON" },
            { "#23", "RT_HTML" },
            { "#24", "RT_MANIFEST" },
        };

        // Enhanced extension mapping with more types
        static readonly Dictionary<string, ExtensionInfo> ExtensionTypeMap = new Dictionary<string, ExtensionInfo>()
        {
            { "PNG",   new ExtensionInfo(".PNG", "image/png") },
            { "JPG",   new ExtensionInfo(".JPG", "image/jpeg") },
            { "JPEG",  new ExtensionInfo(".JPG", "image/jpeg") },
            { "GIF",   new ExtensionInfo(".GIF", "image/gif") },
            { "BMP",   new ExtensionInfo(".BMP", "image/bmp") },
            { "WAVE",  new ExtensionInfo(".WAV", "audio/wav") },
            { "WAV",   new ExtensionInfo(".WAV", "audio/wav") },
            { "AVI",   new ExtensionInfo(".AVI", "video/avi") },
            { "MIDS",  new ExtensionInfo(".MID", "audio/midi") },
            { "MIDI",  new ExtensionInfo(".MID", "audio/midi") },
            { "MP3",   new ExtensionInfo(".MP3", "audio/mp3") },
            { "OGG",   new ExtensionInfo(".OGG", "audio/ogg") },
            { "XML",   new ExtensionInfo(".XML", "text/xml") },
            { "JSON",  new ExtensionInfo(".JSON", "application/json") },
            { "TXT",   new ExtensionInfo(".TXT", "text/plain") },
            { "HTML",  new ExtensionInfo(".HTML", "text/html") },
            { "HTM",   new ExtensionInfo(".HTML", "text/html") },
            { "JS",    new ExtensionInfo(".JS", "text/javascript") },
            { "CSS",   new ExtensionInfo(".CSS", "text/css") },
            { "SCR",   new ExtensionInfo(".BIN", "application/octet-stream") },
            { "#1",    new ExtensionInfo(".CUR", "image/x-cursor") },
            { "#2",    new ExtensionInfo(".BMP", "image/bmp") },
            { "#3",    new ExtensionInfo(".ICO", "image/x-icon") },
            { "#6",    new ExtensionInfo(".TXT", "text/plain") },
            { "#8",    new ExtensionInfo(".FNT", "font/font") },
            { "#10",   new ExtensionInfo(".BIN", "application/octet-stream") },
            { "#14",   new ExtensionInfo(".ICO", "image/x-icon") },
            { "#16",   new ExtensionInfo(".TXT", "text/plain") },
            { "#21",   new ExtensionInfo(".ANI", "image/x-ani-cursor") },
            { "#22",   new ExtensionInfo(".ANI", "image/x-ani-icon") },
            { "#23",   new ExtensionInfo(".HTML", "text/html") },
            { "#24",   new ExtensionInfo(".XML", "text/xml") },
        };

        bool OpenRtVersionAsText = true;
        bool ExtractStringTables = true;
        bool ExtractIcons = true;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "MZ") || VFS.IsVirtual)
                return null;

            var res = new ExeFile.ResourceAccessor (file.Name);
            try
            {
                var dir = new List<Entry>();
                var processedTypes = new HashSet<string>();

                foreach (var type in res.EnumTypes())
                {
                    if (processedTypes.Contains (type))
                        continue;
                    processedTypes.Add (type);

                    string dir_name = GetDirectoryName (type);
                    if (string.IsNullOrEmpty (dir_name))
                        continue;

                    // Handle special resource types
                    if (type == "#6" && ExtractStringTables)
                    {
                        ExtractStringTable (res, dir, dir_name);
                        continue;
                    }

                    if (type == "#14" && ExtractIcons)
                    {
                        ExtractGroupIcons (res, dir, dir_name);
                        continue;
                    }

                    var extInfo = GetExtensionInfo (type);
                    foreach (var name in res.EnumNames (type))
                    {
                        string full_name = FormatResourceName (name, type, dir_name, extInfo.Extension);
                        var entry = Create<ResourceEntry>(full_name);
                        entry.NativeName = name;
                        entry.NativeType = type;
                        entry.Offset = 0;
                        entry.Size = res.GetResourceSize (name, type);
                        entry.MimeType = extInfo.MimeType;
                        dir.Add (entry);
                    }
                }

                if (dir.Count == 0)
                {
                    res.Dispose();
                    return null;
                }

                dir.Sort((a, b) => string.Compare (a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                return new ResourcesArchive (file, this, dir, res);
            }
            catch (Exception ex)
            {
                res.Dispose();
                throw new InvalidFormatException($"Failed to read resources: {ex.Message}");
            }
        }

        string GetDirectoryName (string type)
        {
            if (RuntimeTypeMap.TryGetValue (type, out string dir_name))
                return dir_name;

            if (!type.StartsWith("#"))
                return type;

            if (type.StartsWith("#") && int.TryParse (type.Substring (1), out int typeId))
            {
                if (typeId > 24 && typeId < 256) // Common custom resource range
                    return $"RT_CUSTOM_{typeId}";
            }

            return null;
        }

        ExtensionInfo GetExtensionInfo (string type)
        {
            if (ExtensionTypeMap.TryGetValue (type, out ExtensionInfo info))
                return info;
            return new ExtensionInfo(".BIN", "application/octet-stream");
        }

        string FormatResourceName (string name, string type, string dirName, string extension)
        {
            string resourceName = name;
            if (name.StartsWith("#"))
                resourceName = IdToString (name);

            if (type.StartsWith("#") && RuntimeTypeMap.ContainsKey (type))
                resourceName = $"{resourceName}_{type.Substring (1)}";

            return string.Join("/", dirName, resourceName) + extension;
        }

        void ExtractStringTable (ExeFile.ResourceAccessor res, List<Entry> dir, string dirName)
        {
            foreach (var name in res.EnumNames("#6"))
            {
                var data = res.GetResource (name, "#6");
                if (data == null || data.Length == 0)
                    continue;

                int stringId = 0;
                if (name.StartsWith("#") && int.TryParse (name.Substring (1), out int blockId))
                    stringId = (blockId - 1) * 16;

                var strings = ParseStringTable (data, stringId);
                foreach (var str in strings)
                {
                    string entryName = $"{dirName}/String_{str.Key:D5}.txt";
                    var entry = Create<StringTableEntry>(entryName);
                    entry.StringId = str.Key;
                    entry.StringValue = str.Value;
                    entry.NativeName = name;
                    entry.NativeType = "#6";
                    entry.Size = (uint)Encoding.Unicode.GetByteCount (str.Value);
                    dir.Add (entry);
                }
            }
        }

        Dictionary<int, string> ParseStringTable (byte[] data, int baseId)
        {
            var strings = new Dictionary<int, string>();
            int offset = 0;

            for (int i = 0; i < 16 && offset < data.Length; i++)
            {
                if (offset + 2 > data.Length)
                    break;

                int length = BitConverter.ToUInt16 (data, offset) * 2;
                offset += 2;

                if (length > 0 && offset + length <= data.Length)
                {
                    string str = Encoding.Unicode.GetString (data, offset, length);
                    strings[baseId + i] = str;
                    offset += length;
                }
            }

            return strings;
        }

        void ExtractGroupIcons (ExeFile.ResourceAccessor res, List<Entry> dir, string dirName)
        {
            foreach (var name in res.EnumNames("#14"))
            {
                var groupData = res.GetResource (name, "#14");
                if (groupData == null || groupData.Length < 6)
                    continue;

                string groupName = name.StartsWith("#") ? IdToString (name) : name;
                string entryName = $"{dirName}/{groupName}.ico";

                var entry = Create<IconGroupEntry>(entryName);
                entry.NativeName = name;
                entry.NativeType = "#14";
                entry.GroupData = groupData;
                entry.Size = (uint)groupData.Length; // Will be updated when building icon
                dir.Add (entry);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var rarc = (ResourcesArchive)arc;
            var rent = (ResourceEntry)entry;

            if (rent is StringTableEntry strEntry)
            {
                var bytes = Encoding.UTF8.GetBytes (strEntry.StringValue);
                return new BinMemoryStream (bytes, rent.Name);
            }

            if (rent is IconGroupEntry iconEntry)
                return BuildIconFromGroup (rarc, iconEntry);

            var data = rarc.Accessor.GetResource (rent.NativeName, rent.NativeType);
            if (data == null)
                return Stream.Null;

            if (rent.NativeType == "#16" && OpenRtVersionAsText)
                return OpenVersion (data, rent.Name);

            if (rent.NativeType == "#24")
                return new BinMemoryStream (data, rent.Name);

            return new BinMemoryStream (data, rent.Name);
        }

        Stream BuildIconFromGroup (ResourcesArchive rarc, IconGroupEntry iconEntry)
        {
            var groupData = iconEntry.GroupData;
            if (groupData.Length < 6)
                return Stream.Null;

            int iconCount = BitConverter.ToUInt16 (groupData, 4);
            var iconDir = new List<IconDirEntry>();

            // Parse icon directory
            int offset = 6;
            for (int i = 0; i < iconCount && offset + 14 <= groupData.Length; i++)
            {
                var dirEntry = new IconDirEntry
                {
                    Width = groupData[offset],
                    Height = groupData[offset + 1],
                    ColorCount = groupData[offset + 2],
                    Planes = BitConverter.ToUInt16 (groupData, offset + 4),
                    BitCount = BitConverter.ToUInt16 (groupData, offset + 6),
                    BytesInRes = BitConverter.ToUInt32 (groupData, offset + 8),
                    ImageId = BitConverter.ToUInt16 (groupData, offset + 12)
                };
                iconDir.Add (dirEntry);
                offset += 14;
            }

            // Build ICO file
            using (var ms = new MemoryStream())
            {
                ms.WriteByte (0); ms.WriteByte (0); // Reserved
                ms.WriteByte (1); ms.WriteByte (0); // Type: 1 = ICO
                ms.Write (BitConverter.GetBytes((ushort)iconDir.Count), 0, 2);

                var imageDataList = new List<byte[]>();
                uint imageOffset = (uint)(6 + iconDir.Count * 16);

                foreach (var entry in iconDir)
                {
                    var imageData = rarc.Accessor.GetResource($"#{entry.ImageId}", "#3");
                    if (imageData == null)
                        continue;

                    imageDataList.Add (imageData);

                    ms.WriteByte (entry.Width);
                    ms.WriteByte (entry.Height);
                    ms.WriteByte (entry.ColorCount);
                    ms.WriteByte (0); // Reserved
                    ms.Write (BitConverter.GetBytes (entry.Planes), 0, 2);
                    ms.Write (BitConverter.GetBytes (entry.BitCount), 0, 2);
                    ms.Write (BitConverter.GetBytes((uint)imageData.Length), 0, 4);
                    ms.Write (BitConverter.GetBytes (imageOffset), 0, 4);

                    imageOffset += (uint)imageData.Length;
                }

                foreach (var imageData in imageDataList)
                    ms.Write (imageData, 0, imageData.Length);

                return new BinMemoryStream (ms.ToArray(), iconEntry.Name);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var rent = (ResourceEntry)entry;
            switch (rent.NativeType)
            {
                case "#2": // RT_BITMAP
                    return OpenBitmapResource (arc, rent);
                case "#3": // RT_ICON
                case "#14": // RT_GROUP_ICON
                    return base.OpenImage (arc, entry);
                case "PNG":
                case "JPG":
                case "JPEG":
                case "GIF":
                    // These should work with standard decoders
                    return base.OpenImage (arc, entry);
                default:
                    return base.OpenImage (arc, entry);
            }
        }

        IImageDecoder OpenBitmapResource (ArcFile arc, ResourceEntry entry)
        {
            var rarc = (ResourcesArchive)arc;
            var bitmap = new byte[14 + entry.Size];
            int length = rarc.Accessor.ReadResource (entry.NativeName, entry.NativeType, bitmap, 14);

            if (length < 40) // Minimum BITMAPINFOHEADER size
                throw new InvalidFormatException("Invalid bitmap resource - too small.");

            length += 14;

            bitmap[0] = (byte)'B';
            bitmap[1] = (byte)'M';
            LittleEndian.Pack (length, bitmap, 2);

            int headerSize = bitmap.ToInt32 (14); // biSize
            int bitsPerPixel = bitmap.ToUInt16 (14 + 14);
            int compression = bitmap.ToInt32 (14 + 16);
            int colorsUsed = bitmap.ToInt32 (14 + 32);

            int colorTableSize = 0;
            if (bitsPerPixel <= 8 && compression != 3) // BI_BITFIELDS
                colorTableSize = (colorsUsed > 0 ? colorsUsed : (1 << bitsPerPixel)) * 4;
            else if (compression == 3 && (bitsPerPixel == 16 || bitsPerPixel == 32))
                colorTableSize = 12; // 3 DWORD color masks

            int pixelDataOffset = 14 + headerSize + colorTableSize;
            LittleEndian.Pack (pixelDataOffset, bitmap, 10);

            var bm = new BinMemoryStream (bitmap, 0, length, entry.Name);
            var info = ImageFormat.Bmp.ReadMetaData (bm);
            if (info == null)
            {
                bm.Dispose();
                throw new InvalidFormatException("Invalid bitmap resource format.");
            }

            bm.Position = 0;
            return new ImageFormatDecoder (bm, ImageFormat.Bmp, info);
        }

        internal static string IdToString (string id)
        {
            if (id.Length > 1 && id[0] == '#' && char.IsDigit (id[1]))
                id = id.Substring (1).PadLeft (5, '0');
            return id;
        }

        Stream OpenVersion (byte[] data, string name)
        {
            var input = new BinMemoryStream (data, name);
            for (;;)
            {
                if (input.Position + 2 > input.Length || input.ReadUInt16() != input.Length - input.Position + 2)
                    break;
                int value_length = input.ReadUInt16();
                int type = input.ReadUInt16();
                if (0 == value_length || type != 0)
                    break;
                if (input.ReadCString (Encoding.Unicode) != "VS_VERSION_INFO")
                    break;
                long pos = (input.Position + 3) & -4L;
                input.Position = pos;
                if (input.ReadUInt32() != 0xFEEF04BDu)
                    break;
                int info_length = value_length;
                bool found_string_info = false;
                do
                {
                    pos += info_length;
                    input.Position = pos;
                    if (input.Position + 6 > input.Length)
                        break;

                    info_length  = input.ReadUInt16();
                    value_length = input.ReadUInt16();
                    type         = input.ReadUInt16();
                    found_string_info = input.ReadCString (Encoding.Unicode) == "StringFileInfo";
                }
                while (!found_string_info && input.PeekByte() != -1);
                if (!found_string_info)
                    break;
                pos = (input.Position + 3) & -4L;
                input.Position = pos;
                if (input.Position + 6 > input.Length)
                    break;
                info_length = input.ReadUInt16();
                long end_pos = pos + info_length;
                value_length = input.ReadUInt16();
                type = input.ReadUInt16();
                if (value_length != 0)
                    break;
                var output = new MemoryStream();
                using (var text = new StreamWriter (output, new UTF8Encoding (false), 512, true))
                {
                    string block_name = input.ReadCString (Encoding.Unicode);
                    text.WriteLine ("BLOCK \"{0}\"\r\n{{", block_name);
                    long next_pos = (input.Position + 3) & -4L;
                    while (next_pos < end_pos && next_pos < input.Length)
                    {
                        input.Position = next_pos;
                        if (input.Position + 6 > input.Length)
                            break;
                        info_length = input.ReadUInt16();
                        value_length = input.ReadUInt16();
                        type = input.ReadUInt16();
                        next_pos = (next_pos + info_length + 3) & -4L;
                        string key = input.ReadCString (Encoding.Unicode);
                        input.Position = (input.Position + 3) & -4L;
                        string value = value_length != 0 ? input.ReadCString (value_length * 2, Encoding.Unicode)
                                                         : String.Empty;
                        text.WriteLine ("\tVALUE \"{0}\", \"{1}\"", key, value);
                    }
                    text.WriteLine ("}");
                }
                input.Dispose();
                output.Position = 0;
                return output;
            }
            input.Position = 0;
            return input;
        }

        class ExtensionInfo
        {
            public string Extension { get; set; }
            public string MimeType { get; set; }

            public ExtensionInfo (string ext, string mime)
            {
                Extension = ext;
                MimeType = mime;
            }
        }

        class IconDirEntry
        {
            public byte Width;
            public byte Height;
            public byte ColorCount;
            public ushort Planes;
            public ushort BitCount;
            public uint BytesInRes;
            public ushort ImageId;
        }
    }

    internal class ResourceEntry : Entry
    {
        public string NativeName;
        public string NativeType;
        public string MimeType;
    }

    internal class StringTableEntry : ResourceEntry
    {
        public int StringId;
        public string StringValue;
    }

    internal class IconGroupEntry : ResourceEntry
    {
        public byte[] GroupData;
    }

    internal class ResourcesArchive : ArcFile
    {
        public readonly ExeFile.ResourceAccessor Accessor;

        public ResourcesArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ExeFile.ResourceAccessor acc)
            : base (arc, impl, dir)
        {
            Accessor = acc;
        }

        #region IDisposable Members
        bool _acc_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (_acc_disposed)
                return;
            if (disposing)
                Accessor.Dispose();
            _acc_disposed = true;
            base.Dispose (disposing);
        }
        #endregion
    }

// Improved version info parser
internal class VersionInfoParser
{
    public VersionInfo Parse (byte[] data)
    {
        using (var input = new BinMemoryStream (data))
        {
            return ParseVersionInfo (input);
        }
    }

    VersionInfo ParseVersionInfo (IBinaryStream input)
    {
        long startPos = input.Position;
        int totalLength = input.ReadUInt16();

        if (input.Length - startPos < totalLength)
            return null;

        int valueLength = input.ReadUInt16();
        int type = input.ReadUInt16();

        if (type != 0)
            return null;

        string key = input.ReadCString (Encoding.Unicode);
        if (key != "VS_VERSION_INFO")
            return null;

        // Align to DWORD boundary
        input.Position = (input.Position + 3) & ~3L;

        var info = new VersionInfo();

        if (valueLength >= 52 && input.ReadUInt32() == 0xFEEF04BDu)
        {
            info.FileVersion = ParseFileVersion (input);
            input.Position += 44; // Skip rest of VS_FIXEDFILEINFO
        }

        long endPos = startPos + totalLength;

        // Parse StringFileInfo and VarFileInfo blocks
        while (input.Position < endPos && input.PeekByte() != -1)
        {
            long blockStart = (input.Position + 3) & ~3L;
            input.Position = blockStart;

            if (input.Position + 6 > input.Length)
                break;

            int blockLength = input.ReadUInt16();
            if (blockLength == 0)
                break;

            int blockValueLength = input.ReadUInt16();
            int blockType = input.ReadUInt16();
            string blockKey = input.ReadCString (Encoding.Unicode);

            if (blockKey == "StringFileInfo")
                ParseStringFileInfo (input, blockStart + blockLength, info);
            else if (blockKey == "VarFileInfo")
                ParseVarFileInfo (input, blockStart + blockLength, info);
            else
                input.Position = blockStart + blockLength;
        }

        return info;
    }

    void ParseStringFileInfo (IBinaryStream input, long endPos, VersionInfo info)
        {
            while (input.Position < endPos)
            {
                long tableStart = (input.Position + 3) & ~3L;
                input.Position = tableStart;

                if (input.Position + 6 > endPos)
                    break;

                int tableLength = input.ReadUInt16();
                if (tableLength == 0)
                    break;

                input.ReadUInt16(); // value length (always 0)
                input.ReadUInt16(); // type (always 1)
                string langCodePage = input.ReadCString (Encoding.Unicode);

                info.StringTables.Add (langCodePage, new Dictionary<string, string>());

                long tableEnd = tableStart + tableLength;
                while (input.Position < tableEnd)
                {
                    long stringStart = (input.Position + 3) & ~3L;
                    input.Position = stringStart;

                    if (input.Position + 6 > tableEnd)
                        break;

                    int stringLength = input.ReadUInt16();
                    if (stringLength == 0)
                        break;

                    int stringValueLength = input.ReadUInt16();
                    input.ReadUInt16(); // type

                    string key = input.ReadCString (Encoding.Unicode);
                    input.Position = (input.Position + 3) & ~3L;

                    string value = stringValueLength > 0
                        ? input.ReadCString (stringValueLength * 2, Encoding.Unicode)
                        : string.Empty;

                    info.StringTables[langCodePage][key] = value;
                    input.Position = stringStart + stringLength;
                }
            }
        }

        void ParseVarFileInfo (IBinaryStream input, long endPos, VersionInfo info)
        {
            // Parse Translation array
            while (input.Position < endPos)
            {
                long varStart = (input.Position + 3) & ~3L;
                input.Position = varStart;

                if (input.Position + 6 > endPos)
                    break;

                int varLength = input.ReadUInt16();
                if (varLength == 0)
                    break;

                int varValueLength = input.ReadUInt16();
                input.ReadUInt16(); // type

                string key = input.ReadCString (Encoding.Unicode);
                if (key == "Translation")
                {
                    input.Position = (input.Position + 3) & ~3L;
                    for (int i = 0; i < varValueLength / 4; i++)
                    {
                        ushort langId = input.ReadUInt16();
                        ushort codePage = input.ReadUInt16();
                        info.Translations.Add((langId, codePage));
                    }
                }

                input.Position = varStart + varLength;
            }
        }

        string ParseFileVersion (IBinaryStream input)
        {
            uint ms = input.ReadUInt32();
            uint ls = input.ReadUInt32();
            return $"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}";
        }
    }

    internal class VersionInfo
    {
        public string FileVersion { get; set; }
        public Dictionary<string, Dictionary<string, string>> StringTables { get; }
            = new Dictionary<string, Dictionary<string, string>>();
        public List<(ushort LangId, ushort CodePage)> Translations { get; }
            = new List<(ushort, ushort)>();

        public void WriteTo (StreamWriter writer)
        {
            if (!string.IsNullOrEmpty (FileVersion))
                writer.WriteLine($"FileVersion: {FileVersion}");

            foreach (var table in StringTables)
            {
                writer.WriteLine($"\nBLOCK \"{table.Key}\"");
                writer.WriteLine("{");
                foreach (var entry in table.Value)
                {
                    writer.WriteLine($"\tVALUE \"{entry.Key}\", \"{entry.Value}\"");
                }
                writer.WriteLine("}");
            }

            if (Translations.Count > 0)
            {
                writer.WriteLine("\nTranslations:");
                foreach (var trans in Translations)
                {
                    writer.WriteLine($"\t0x{trans.LangId:X4} (Language ID), 0x{trans.CodePage:X4} (Code Page)");
                }
            }
        }
    }
}