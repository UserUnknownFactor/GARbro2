using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;

namespace GameRes.Formats.Macromedia
{
    #region Entry classes
    internal class SwfEntry : Entry
    {
        public SwfChunk     Chunk;
        public string       Path { get; set; }
        public List<Entry>  Children { get; set; }
    }

    internal class SwfSoundEntry : SwfEntry
    {
        public readonly List<SwfChunk>  SoundStream = new List<SwfChunk>();
    }

    internal class SwfSpriteEntry : SwfEntry
    {
        public List<SwfChunk> SpriteChunks { get; set; } = new List<SwfChunk>();
    }
    #endregion

    [Export(typeof(ArchiveFormat))]
    public class SwfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SWF"; } }
        public override string Description { get { return "Shockwave Flash presentation"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public SwfOpener ()
        {
            Signatures = new uint[] { 0x08535743, 0x08535746, 0 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "CWS") && !file.View.AsciiEqual (0, "FWS"))
                return null;

            bool is_compressed = file.View.ReadByte (0) == 'C';
            int version = file.View.ReadByte (3);
            using (var reader = new SwfReader (file.CreateStream(), version, is_compressed))
            {
                var chunks = reader.Parse();
                var base_name = Path.GetFileNameWithoutExtension (file.Name);

                // Build hierarchical structure
                var root_entries = new List<Entry>();
                var sprite_stack = new Stack<SwfSpriteEntry>();
                SwfSoundEntry current_stream = null;
                var resource_groups = new Dictionary<string, List<Entry>>();

                foreach (var chunk in chunks)
                {
                    if (chunk.Type == Types.DefineSprite)
                    {
                        var sprite_entry = new SwfSpriteEntry
                        {
                            Name = string.Format ("Sprite_{0:D5}", chunk.Id),
                            Type = "sprite",
                            Chunk = chunk,
                            Offset = 0,
                            Size = (uint)chunk.Length,
                            Children = new List<Entry>()
                        };

                        if (sprite_stack.Count > 0)
                            sprite_stack.Peek().Children.Add(sprite_entry);
                        else
                            AddToResourceGroup(resource_groups, "Sprites", sprite_entry);

                        sprite_stack.Push(sprite_entry);
                    }
                    else if (chunk.Type == Types.End && sprite_stack.Count > 0)
                    {
                        sprite_stack.Pop();
                    }
                    else if (IsSoundStream (chunk))
                    {
                        HandleSoundStream(chunk, ref current_stream, base_name, resource_groups);
                    }
                    else if (TypeMap.ContainsKey (chunk.Type))
                    {
                        var entry = CreateEntry (chunk, base_name);
                        if (entry != null)
                        {
                            if (sprite_stack.Count > 0)
                            {
                                sprite_stack.Peek().Children.Add(entry);
                            }
                            else
                            {
                                string group = GetResourceGroup (chunk.Type);
                                AddToResourceGroup (resource_groups, group, entry);
                            }
                        }
                    }
                }

                foreach (var group in resource_groups)
                {
                    if (group.Value.Count > 0)
                    {
                        var folder = new SwfEntry
                        {
                            Name = group.Key,
                            Type = "folder",
                            Offset = 0,
                            Size = 0,
                            Children = group.Value
                        };
                        root_entries.Add(folder);
                    }
                }

                var flat_list = FlattenHierarchy (root_entries);
                return new ArcFile (file, this, flat_list);
            }
        }

        private SwfEntry CreateEntry (SwfChunk chunk, string baseName)
        {
            var type = GetTypeFromId (chunk.Type);
            var name = GenerateResourceName (chunk, baseName);

            return new SwfEntry
            {
                Name = name,
                Type = type,
                Chunk = chunk,
                Offset = 0,
                Size = (uint)chunk.Length
            };
        }

        private string GenerateResourceName (SwfChunk chunk, string baseName)
        {
            string prefix = GetResourcePrefix (chunk.Type);
            string extension = GetResourceExtension (chunk.Type);

            if (chunk.Id >= 0)
                return string.Format ("{0}_{1:D5}.{2}", prefix, chunk.Id, extension);
            else
                return string.Format ("{0}_{1}.{2}", baseName, prefix, extension);
        }

        #region String Representations of Types
        private string GetResourcePrefix (Types type)
        {
            switch (type)
            {
                case Types.DefineBitsJpeg:
                case Types.DefineBitsJpeg2:
                case Types.DefineBitsJpeg3:
                    return "Image";
                case Types.DefineBitsLossless:
                case Types.DefineBitsLossless2:
                    return "Image";
                case Types.DefineSound:
                    return "Sound";
                case Types.DefineShape:
                case Types.DefineShape2:
                case Types.DefineShape3:
                    return "Shape";
                case Types.DefineText:
                case Types.DefineText2:
                    return "Text";
                case Types.DefineFont:
                case Types.DefineFont2:
                case Types.DefineFont3:
                    return "Font";
                case Types.DefineButton:
                case Types.DefineButton2:
                    return "Button";
                case Types.DefineVideoStream:
                    return "Video";
                default:
                    return type.ToString();
            }
        }

        private string GetResourceExtension (Types type)
        {
            switch (type)
            {
                case Types.DefineBitsJpeg:
                case Types.DefineBitsJpeg2:
                case Types.DefineBitsJpeg3:
                    return "jpg";
                case Types.DefineBitsLossless:
                case Types.DefineBitsLossless2:
                    return "png";
                case Types.DefineSound:
                    return "mp3";
                case Types.DefineVideoStream:
                    return "flv";
                default:
                    return "dat";
            }
        }

        private string GetResourceGroup (Types type)
        {
            switch (type)
            {
                case Types.DefineBitsJpeg:
                case Types.DefineBitsJpeg2:
                case Types.DefineBitsJpeg3:
                case Types.DefineBitsLossless:
                case Types.DefineBitsLossless2:
                    return "Images";
                case Types.DefineSound:
                case Types.SoundStreamHead:
                case Types.SoundStreamHead2:
                    return "Audio";
                case Types.DefineShape:
                case Types.DefineShape2:
                case Types.DefineShape3:
                    return "Shapes";
                case Types.DefineText:
                case Types.DefineText2:
                    return "Text";
                case Types.DefineFont:
                case Types.DefineFont2:
                case Types.DefineFont3:
                    return "Fonts";
                case Types.DefineButton:
                case Types.DefineButton2:
                    return "Buttons";
                case Types.DefineVideoStream:
                case Types.VideoFrame:
                    return "Video";
                case Types.DoAction:
                case Types.DoInitAction:
                    return "Scripts";
                default:
                    return "Other";
            }
        }
        #endregion

        private void AddToResourceGroup (Dictionary<string, List<Entry>> groups, string groupName, Entry entry)
        {
            if (!groups.ContainsKey (groupName))
                groups[groupName] = new List<Entry>();
            groups[groupName].Add (entry);
        }

        private void HandleSoundStream (SwfChunk chunk, ref SwfSoundEntry currentStream, 
                                     string baseName, Dictionary<string, List<Entry>> resourceGroups)
        {
            switch (chunk.Type)
            {
                case Types.SoundStreamHead:
                case Types.SoundStreamHead2:
                    if ((chunk.Data[1] & 0x30) != 0x20) // not mp3 stream
                    {
                        currentStream = null;
                        return;
                    }
                    currentStream = new SwfSoundEntry
                    {
                        Name = string.Format ("SoundStream_{0:D5}.mp3", chunk.Id),
                        Type = "audio",
                        Chunk = chunk,
                        Offset = 0,
                    };
                    AddToResourceGroup (resourceGroups, "Audio", currentStream);
                    break;

                case Types.SoundStreamBlock:
                    if (currentStream != null)
                    {
                        currentStream.Size += (uint)(chunk.Data.Length - 4);
                        currentStream.SoundStream.Add(chunk);
                    }
                    break;
            }
        }

        private List<Entry> FlattenHierarchy (List<Entry> entries, string parentPath = "")
        {
            var result = new List<Entry>();

            foreach (var entry in entries)
            {
                var swfEntry = entry as SwfEntry;
                if (swfEntry != null)
                {
                    swfEntry.Path = string.IsNullOrEmpty(parentPath) 
                        ? entry.Name 
                        : parentPath + "/" + entry.Name;

                    if (swfEntry.Type != "folder")
                    {
                        swfEntry.Name = swfEntry.Path;
                        result.Add (swfEntry);
                    }

                    if (swfEntry.Children != null && swfEntry.Children.Count > 0)
                        result.AddRange (FlattenHierarchy(swfEntry.Children, swfEntry.Path));
                }
            }

            return result;
        }

        internal static bool IsSoundStream(SwfChunk chunk)
        {
            return chunk.Type == Types.SoundStreamHead
                || chunk.Type == Types.SoundStreamHead2
                || chunk.Type == Types.SoundStreamBlock;
        }

        static string GetTypeFromId (Types type_id)
        {
            string type;
            if (TypeMap.TryGetValue (type_id, out type))
                return type;
            return type_id.ToString();
        }

        static Stream ExtractChunk (SwfEntry entry)
        {
            return new BinMemoryStream (entry.Chunk.Data);
        }

        static Stream ExtractChunkContents (SwfEntry entry)
        {
            var source = entry.Chunk;
            return new BinMemoryStream (source.Data, 2, source.Length-2);
        }

        static Stream ExtractSoundStream (SwfEntry entry)
        {
            var swe = (SwfSoundEntry)entry;
            var output = new MemoryStream ((int)swe.Size);
            foreach (var chunk in swe.SoundStream)
                output.Write (chunk.Data, 4, chunk.Data.Length-4);
            output.Position = 0;
            return output;
        }

        static Stream ExtractAudio (SwfEntry entry)
        {
            var chunk = entry.Chunk;
            int flags = chunk.Data[2];
            int format = flags >> 4;
            if (2 == format) // MP3
                return new BinMemoryStream (chunk.Data, 9, chunk.Length-9);

            // For other formats, include header info
            return new BinMemoryStream (chunk.Data, 2, chunk.Length-2);
        }

        static Dictionary<Types, Extractor> ExtractMap = new Dictionary<Types, Extractor> {
            { Types.DefineBitsJpeg,      ExtractChunkContents },
            { Types.DefineBitsJpeg2,     ExtractChunk },
            { Types.DefineBitsJpeg3,     ExtractChunk },
            { Types.DefineBitsLossless,  ExtractChunk },
            { Types.DefineBitsLossless2, ExtractChunk },
            { Types.DefineSound,         ExtractAudio },
            { Types.SoundStreamHead,     ExtractSoundStream },
            { Types.SoundStreamHead2,    ExtractSoundStream },
            { Types.DoAction,            ExtractChunkContents },
        };

        static Dictionary<Types, string> TypeMap = new Dictionary<Types, string> {
            { Types.DefineBitsJpeg,         "image" },
            { Types.DefineBitsJpeg2,        "image" },
            { Types.DefineBitsJpeg3,        "image" },
            { Types.DefineBitsLossless,     "image" },
            { Types.DefineBitsLossless2,    "image" },
            { Types.DefineSound,            "audio" },
            { Types.DoAction,               "script" },
            { Types.DoInitAction,           "script" },
            { Types.DefineShape,            "shape" },
            { Types.DefineShape2,           "shape" },
            { Types.DefineShape3,           "shape" },
            { Types.DefineText,             "text" },
            { Types.DefineText2,            "text" },
            { Types.DefineFont,             "font" },
            { Types.DefineFont2,            "font" },
            { Types.DefineFont3,            "font" },
            { Types.DefineButton,           "button" },
            { Types.DefineButton2,          "button" },
            { Types.DefineSprite,           "sprite" },
            { Types.DefineVideoStream,      "video" },
            { Types.VideoFrame,             "video" },
            { Types.JpegTables,             "data" },
            { Types.DefineMorphShape,       "shape" },
            { Types.DefineMorphShape2,      "shape" },
            { Types.DefineBinary,           "binary" },
            { Types.DefineEditText,         "text" },
            { Types.PlaceObject,            "placement" },
            { Types.PlaceObject2,           "placement" },
            { Types.PlaceObject3,           "placement" },
            { Types.RemoveObject,           "placement" },
            { Types.RemoveObject2,          "placement" },
        };

        delegate Stream Extractor(SwfEntry entry);

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var swent = (SwfEntry)entry;

            Extractor extract;
            if (!ExtractMap.TryGetValue(swent.Chunk.Type, out extract))
                extract = ExtractChunk;
            return extract(swent);
        }

        public override IImageDecoder OpenImage(ArcFile arc, Entry entry)
        {
            var swent = (SwfEntry)entry;
            ImageFormat format = null;

            switch (swent.Chunk.Type)
            {
                case Types.DefineBitsLossless:
                    format = new SwfLosslessFormat();
                    break;
                case Types.DefineBitsLossless2:
                    format = new SwfLossless2Format();
                    break;
                case Types.DefineBitsJpeg:
                    format = new SwfJpegFormat();
                    break;
                case Types.DefineBitsJpeg2:
                    format = new SwfJpeg2Format();
                    break;
                case Types.DefineBitsJpeg3:
                    format = new SwfJpeg3Format();
                    break;
                default:
                    return base.OpenImage(arc, entry);
            }

            var stream = new BinMemoryStream(swent.Chunk.Data);
            var info = format.ReadMetaData(stream);
            if (info == null)
                return base.OpenImage(arc, entry);

            stream.Position = 0;
            var image = format.Read(stream, info);
            return new ImageFormatDecoder(stream, format, info, image);
        }
    }

    internal class ImageFormatDecoder : IImageDecoder
    {
        private Stream m_input;
        private ImageFormat m_format;
        private ImageMetaData m_info;
        private ImageData m_data;

        public Stream Source { get { return m_input; } }
        public ImageFormat SourceFormat { get { return m_format; } }
        public ImageMetaData Info { get { return m_info; } }
        public ImageData Image { get { return m_data; } }

        public ImageFormatDecoder(Stream input, ImageFormat format, ImageMetaData info, ImageData data)
        {
            m_input  = input;
            m_format = format;
            m_info   = info;
            m_data   = data;
        }

        public void Dispose()
        {
            m_input?.Dispose();
        }
    }

    #region SWF Image Formats

    internal abstract class SwfImageFormat : ImageFormat
    {
        public override uint Signature { get { return 0; } }

        public SwfImageFormat()
        {
            Extensions = new string[0];
            Signatures = new uint[0];
        }

        public override void Write(Stream file, ImageData bitmap)
        {
            throw new NotImplementedException("SWF image writing not supported");
        }
    }

    internal class SwfJpegFormat : SwfImageFormat
    {
        public override string Tag { get { return "JPEG/SWF"; } }
        public override string Description { get { return "SWF JPEG image"; } }

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            // For DefineBitsJPEG, skip the ID and find JPEG
            file.Position = 2;
            var data = file.ReadBytes((int)(file.Length - 2));
            int jpegPos = JpegUtility.FindSignature(data);
            if (jpegPos < 0)
                return null;

            using (var jpeg = new BinMemoryStream(data, jpegPos, data.Length - jpegPos))
            {
                return ImageFormat.Jpeg.ReadMetaData(jpeg);
            }
        }

        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            file.Position = 2;
            var data = file.ReadBytes((int)(file.Length - 2));
            int jpegPos = JpegUtility.FindSignature(data);

            using (var jpeg = new BinMemoryStream(data, jpegPos, data.Length - jpegPos))
            {
                return ImageFormat.Jpeg.Read(jpeg, info);
            }
        }
    }

    internal class SwfJpeg2Format : SwfImageFormat
    {
        public override string Tag { get { return "JPEG2/SWF"; } }
        public override string Description { get { return "SWF JPEG2 image"; } }

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            file.Position = 2; // Skip ID
            var data = file.ReadBytes((int)(file.Length - 2));
            var normalized = JpegUtility.NormalizeJPEG(data);

            using (var jpeg = new BinMemoryStream(normalized))
            {
                return ImageFormat.Jpeg.ReadMetaData(jpeg);
            }
        }

        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            file.Position = 2; // Skip ID
            var data = file.ReadBytes((int)(file.Length - 2));
            var normalized = JpegUtility.NormalizeJPEG(data);

            using (var jpeg = new BinMemoryStream(normalized))
            {
                return ImageFormat.Jpeg.Read(jpeg, info);
            }
        }
    }

    internal class SwfJpeg3Format : SwfImageFormat
    {
        public override string Tag { get { return "JPEG3/SWF"; } }
        public override string Description { get { return "SWF JPEG3 image with alpha"; } }

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            file.Position = 2; // Skip ID
            int jpegLength = file.ReadInt32();
            var data = file.ReadBytes(jpegLength);
            var normalized = JpegUtility.NormalizeJPEG(data);

            using (var jpeg = new BinMemoryStream(normalized))
            {
                var info = ImageFormat.Jpeg.ReadMetaData(jpeg);
                if (info != null)
                    info.BPP = 32; // Has alpha channel
                return info;
            }
        }

        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            file.Position = 2; // Skip ID
            int jpegLength = file.ReadInt32();
            var jpegData = file.ReadBytes(jpegLength);
            var normalized = JpegUtility.NormalizeJPEG(jpegData);

            BitmapSource image;
            using (var jpeg = new BinMemoryStream(normalized))
            {
                var jpegImage = ImageFormat.Jpeg.Read(jpeg, info);
                image = jpegImage.Bitmap;
            }

            // Read alpha channel
            int alphaSize = (int)(file.Length - file.Position);
            if (alphaSize > 0)
            {
                var alphaData = file.ReadBytes(alphaSize);
                using (var alphaStream = new BinMemoryStream(alphaData))
                using (var zstream = new ZLibStream(alphaStream, CompressionMode.Decompress))
                {
                    var alpha = new byte[info.Width * info.Height];
                    zstream.Read(alpha, 0, alpha.Length);

                    // Convert to BGRA32 if needed
                    if (image.Format != PixelFormats.Bgr32)
                        image = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);

                    int stride = (int)info.Width * 4;
                    var pixels = new byte[stride * info.Height];
                    image.CopyPixels(pixels, stride, 0);

                    // Apply alpha
                    int srcAlpha = 0;
                    for (int dst = 3; dst < pixels.Length; dst += 4)
                        pixels[dst] = alpha[srcAlpha++];

                    return ImageData.Create(info, PixelFormats.Bgra32, null, pixels, stride);
                }
            }

            return new ImageData(image, info);
        }
    }

    internal class SwfLosslessFormat : SwfImageFormat
    {
        public override string Tag { get { return "Lossless/SWF"; } }
        public override string Description { get { return "SWF Lossless image"; } }
        protected virtual bool HasAlpha { get { return false; } }

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            file.Position = 0; // Chunk data includes everything
            file.ReadUInt16(); // ID
            byte format = file.ReadUInt8();
            uint width = file.ReadUInt16();
            uint height = file.ReadUInt16();

            int bpp;
            switch (format)
            {
                case 3: bpp = 8; break;
                case 4: bpp = 16; break;
                case 5: bpp = HasAlpha ? 32 : 24; break;
                default: return null;
            }

            return new ImageMetaData { Width = width, Height = height, BPP = bpp };
        }

        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            file.Position = 2; // Skip ID
            byte format = file.ReadUInt8();
            ushort width = file.ReadUInt16();
            ushort height = file.ReadUInt16();

            int colors = 0;
            if (format == 3)
                colors = file.ReadUInt8() + 1;

            using (var zstream = new ZLibStream(file.AsStream, CompressionMode.Decompress, true))
            {
                PixelFormat pixelFormat;
                BitmapPalette palette = null;

                switch (format)
                {
                    case 3:
                        pixelFormat = PixelFormats.Indexed8;
                        var palFormat = HasAlpha ? PaletteFormat.RgbA : PaletteFormat.RgbX;
                        palette = ReadPalette(zstream, colors, palFormat);
                        break;
                    case 4:
                        pixelFormat = PixelFormats.Bgr565;
                        break;
                    case 5:
                        pixelFormat = HasAlpha ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
                        break;
                    default:
                        throw new InvalidFormatException();
                }

                int stride = (int)info.Width * (info.BPP / 8);
                var pixels = new byte[(int)info.Height * stride];

                if (format == 3) // Indexed
                {
                    int rowSize = (int)info.Width;
                    int alignedRowSize = (rowSize + 3) & ~3;
                    var rowBuffer = new byte[alignedRowSize];

                    for (int y = 0; y < info.Height; y++)
                    {
                        zstream.Read(rowBuffer, 0, alignedRowSize);
                        Buffer.BlockCopy(rowBuffer, 0, pixels, y * rowSize, rowSize);
                    }
                }
                else
                {
                    zstream.Read(pixels, 0, pixels.Length);
                }

                if (format == 5) // 32-bit ARGB to BGRA
                {
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte a = pixels[i];
                        byte r = pixels[i + 1];
                        byte g = pixels[i + 2];
                        byte b = pixels[i + 3];
                        pixels[i] = b;
                        pixels[i + 1] = g;
                        pixels[i + 2] = r;
                        pixels[i + 3] = a;
                    }
                }

                return ImageData.Create(info, pixelFormat, palette, pixels);
            }
        }
    }

    internal class SwfLossless2Format : SwfLosslessFormat
    {
        public override string Tag { get { return "Lossless2/SWF"; } }
        public override string Description { get { return "SWF Lossless2 image with alpha"; } }
        protected override bool HasAlpha { get { return true; } }
    }

    #endregion

    #region Utility Classes

    internal static class JpegUtility
    {
        public static int FindSignature(byte[] data, int startPos = 0)
        {
            while (startPos < data.Length - 1)
            {
                if (data[startPos] == 0xFF && data[startPos + 1] == 0xD8)
                    return startPos;
                startPos++;
            }
            return -1;
        }

        public static byte[] NormalizeJPEG(byte[] jpegData)
        {
            // Find the first JPEG tables final FFD9 (End of Image marker)
            int firstFFD9 = -1;
            for (int i = 0; i < jpegData.Length - 1; i++)
            {
                if (jpegData[i] == 0xFF && jpegData[i + 1] == 0xD9)
                {
                    firstFFD9 = i;
                    break;
                }
            }

            if (firstFFD9 < 0)
                return jpegData;

            // Find the second FFD8 (Start of Image marker) after FFD9
            int secondFFD8 = -1;
            for (int i = firstFFD9 + 2; i < jpegData.Length - 1; i++)
            {
                if (jpegData[i] == 0xFF && jpegData[i + 1] == 0xD8)
                {
                    secondFFD8 = i;
                    break;
                }
            }

            if (secondFFD8 < 0)
                return jpegData;

            // Create standard JPEG
            int tablesLength = firstFFD9;
            int imageDataStart = secondFFD8 + 2;
            int imageDataLength = jpegData.Length - imageDataStart;
            int totalLength = tablesLength + imageDataLength;

            byte[] result = new byte[totalLength];
            Buffer.BlockCopy(jpegData, 0, result, 0, tablesLength);
            Buffer.BlockCopy(jpegData, imageDataStart, result, tablesLength, imageDataLength);

            return result;
        }
    }

    #endregion

    internal enum Types : short
    {
        End                     = 0,
        ShowFrame               = 1,
        DefineShape             = 2,
        PlaceObject             = 4,
        RemoveObject            = 5,
        DefineBitsJpeg          = 6,
        DefineButton            = 7,
        JpegTables              = 8,
        SetBackgroundColor      = 9,
        DefineFont              = 10,
        DefineText              = 11,
        DoAction                = 12,
        DefineFontInfo          = 13,
        DefineSound             = 14,
        StartSound              = 15,
        DefineButtonSound       = 17,
        SoundStreamHead         = 18,
        SoundStreamBlock        = 19,
        DefineBitsLossless      = 20,
        DefineBitsJpeg2         = 21,
        DefineShape2            = 22,
        DefineButtonCxform      = 23,
        Protect                 = 24,
        PlaceObject2            = 26,
        RemoveObject2           = 28,
        DefineShape3            = 32,
        DefineText2             = 33,
        DefineButton2           = 34,
        DefineBitsJpeg3         = 35,
        DefineBitsLossless2     = 36,
        DefineEditText          = 37,
        DefineSprite            = 39,
        FrameLabel              = 43,
        SoundStreamHead2        = 45,
        DefineMorphShape        = 46,
        DefineFont2             = 48,
        ExportAssets            = 56,
        ImportAssets            = 57,
        EnableDebugger          = 58,
        DoInitAction            = 59,
        DefineVideoStream       = 60,
        VideoFrame              = 61,
        DefineFontInfo2         = 62,
        EnableDebugger2         = 64,
        ScriptLimits            = 65,
        SetTabIndex             = 66,
        FileAttributes          = 69,
        PlaceObject3            = 70,
        ImportAssets2           = 71,
        DefineFontAlignZones    = 73,
        CSMTextSettings         = 74,
        DefineFont3             = 75,
        SymbolClass             = 76,
        Metadata                = 77,
        DefineScalingGrid       = 78,
        DoABC                   = 82,
        DefineShape4            = 83,
        DefineMorphShape2       = 84,
        DefineSceneAndFrameLabelData = 86,
        DefineBinary            = 87,
        DefineFontName          = 88,
        StartSound2             = 89,
        DefineBitsJpeg4         = 90,
        DefineFont4             = 91,
    };

    internal class SwfChunk
    {
        public Types    Type;
        public byte[]   Data;

        public int Length { get { return Data.Length; } }
        public int     Id { get { return Data.Length > 2 ? Data.ToUInt16 (0) : -1; } }

        public SwfChunk (Types id, int length)
        {
            Type = id;
            Data = length > 0 ? new byte[length] : Array.Empty<byte>();
        }
    }

    internal sealed class SwfReader : IDisposable
    {
        IBinaryStream   m_input;
        MsbBitStream    m_bits;
        int             m_version;

        Int32Rect       m_dim;

        public SwfReader (IBinaryStream input, int version, bool is_compressed)
        {
            m_input = input;
            m_version = version;
            m_input.Position = 8;
            if (is_compressed)
            {
                var zstream = new ZLibStream (input.AsStream, CompressionMode.Decompress);
                m_input = new BinaryStream (zstream, m_input.Name);
            }
            m_bits = new MsbBitStream (m_input.AsStream, true);
        }

        int     m_frame_rate;
        int     m_frame_count;

        List<SwfChunk>  m_chunks = new List<SwfChunk>();

        public List<SwfChunk> Parse ()
        {
            ReadDimensions();
            m_bits.Reset();
            m_frame_rate = m_input.ReadUInt16();
            m_frame_count = m_input.ReadUInt16();

            // Read all chunks
            for (;;)
            {
                var chunk = ReadChunk();
                if (null == chunk)
                    break;
                m_chunks.Add (chunk);

                if (chunk.Type == Types.DefineSprite)
                    ReadSpriteContents(chunk);
            }
            return m_chunks;
        }

        void ReadSpriteContents(SwfChunk spriteChunk)
        {
            using (var spriteData = new BinMemoryStream(spriteChunk.Data))
            {
                spriteData.Position = 4;

                using (var spriteBits = new MsbBitStream(spriteData, true))
                {

                    var originalInput = m_input;
                    var originalBits = m_bits;

                    m_input = new BinaryStream(spriteData, m_input.Name);
                    m_bits = spriteBits;

                    try
                    {
                        for (;;)
                        {
                            var chunk = ReadChunk();
                            if (null == chunk)
                                break;
                            m_chunks.Add(chunk);
                            if (chunk.Type == Types.End)
                                break;
                        }
                    }
                    finally
                    {
                        m_input = originalInput;
                        m_bits = originalBits;
                    }
                }
            }
        }

        void ReadDimensions ()
        {
            int rsize = m_bits.GetBits (5);
            m_dim.X = GetSignedBits (rsize);
            m_dim.Width = GetSignedBits (rsize) - m_dim.X;
            m_dim.Y = GetSignedBits (rsize);
            m_dim.Height = GetSignedBits (rsize) - m_dim.Y;
        }

        byte[]  m_buffer = new byte[4];

        SwfChunk ReadChunk ()
        {
            if (m_input.Read (m_buffer, 0, 2) != 2)
                return null;
            int length = m_buffer.ToUInt16 (0);
            Types id = (Types)(length >> 6);
            length &= 0x3F;
            if (0x3F == length)
                length = m_input.ReadInt32();

            var chunk = new SwfChunk (id, length);
            if (length > 0 && m_input.Read (chunk.Data, 0, length) < length)
                return null;

            return chunk;
        }

        int GetSignedBits (int count)
        {
            int v = m_bits.GetBits (count);
            if ((v >> (count - 1)) != 0)
                v |= -1 << count;
            return v;
        }

        #region IDisposable Members
        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_bits.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }
}
