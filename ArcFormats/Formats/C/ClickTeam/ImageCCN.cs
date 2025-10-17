using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Formats.Properties;
using GameRes.Compression;

namespace GameRes.Formats.Clickteam
{
    [Export(typeof(ImageFormat))]
    public class CcnImageFormat : ImageFormat
    {
        // Graphic mode constants
        private const byte GM_ANDROID_RGBA_8BPC = 0;
        private const byte GM_ANDROID_RGBA_4BPC = 1;
        private const byte GM_ANDROID_RGBA_5BPC = 2;
        private const byte  GM_ANDROID_RGB_8BPC = 3;
        private const byte        GM_RGB_MASKED = 4;
        private const byte      GM_ANDROID_JPEG = 5;
        private const byte             GM_15BIT = 6;
        private const byte             GM_16BIT = 7;
        private const byte         GM_RGBA_8BPC = 8;
        private const byte        GM_FLASH_RGBA = 9;

        // Flag constants
        private const byte   FLAG_RLE = 0x01;
        private const byte  FLAG_RLEW = 0x02;
        private const byte  FLAG_RLET = 0x04;
        private const byte   FLAG_LZX = 0x10;
        private const byte FLAG_ALPHA = 0x20;
        private const byte   FLAG_ACE = 0x40;
        private const byte   FLAG_MAC = 0x80;

        public enum CCNOutputFormatE
        {
            [Description("Android RGBA 8-bit")]
            AndroidRGBA8 = GM_ANDROID_RGBA_8BPC,

            [Description("Android RGBA 4-bit")]
            AndroidRGBA4 = GM_ANDROID_RGBA_4BPC,

            [Description("Android RGBA 5-bit")]
            AndroidRGBA5 = GM_ANDROID_RGBA_5BPC,

            [Description("Android RGB 8-bit")]
            AndroidRGB8 = GM_ANDROID_RGB_8BPC,

            [Description("RGB Masked")]
            RGBMasked = GM_RGB_MASKED,

            [Description("15-bit RGB")]
            RGB15 = GM_15BIT,

            [Description("16-bit RGB")]
            RGB16 = GM_16BIT,

            [Description("RGBA 8-bit")]
            RGBA8 = GM_RGBA_8BPC,

            [Description("Flash RGBA")]
            FlashRGBA = GM_FLASH_RGBA
        }

        public override string         Tag { get { return "CCNIMG"; } }
        public override string Description { get { return "Clickteam Fusion raw image"; } }
        public override uint     Signature { get { return  0; } }
        public override bool      CanWrite { get { return  true; } }

        public CcnImageFormat()
        {
            Extensions = new string[] { "ccnimg" };
            Settings   = new IResourceSetting[] { CCNOutputFormat, /*CCNCompressionEnabled*/ };
        }

        internal class CcnImageMetaData : ImageMetaData
        {
            public      byte GraphicMode { get; set; }
            public            byte Flags { get; set; }
            public          int DataSize { get; set; }
            public          int Checksum { get; set; }
            public        int References { get; set; }
            public        short HotspotX { get; set; }
            public        short HotspotY { get; set; }
            public    short ActionPointX { get; set; }
            public    short ActionPointY { get; set; }
            public uint TransparentColor { get; set; }
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < 32 || !Extensions.Any (e => file.Name.EndsWith (e)))
                return null;

            file.Position = 0;

            int checksum          = file.ReadInt32();
            int references        = file.ReadInt32();
            int dataSize          = file.ReadInt32();
            short width           = file.ReadInt16();
            short height          = file.ReadInt16();
            byte graphicMode      = file.ReadUInt8();
            byte flags            = file.ReadUInt8();
            short reserved        = file.ReadInt16(); // Reserved
            short hotspotX        = file.ReadInt16();
            short hotspotY        = file.ReadInt16();
            short actionPointX    = file.ReadInt16();
            short actionPointY    = file.ReadInt16();
            uint transparentColor = file.ReadUInt32();

            if (width <= 0 || references > 1000 || width > 30000 || 
                    height <= 0 || height > 30000 || graphicMode > GM_FLASH_RGBA)
                return null;

            int bpp = 32;
            switch (graphicMode)
            {
                case GM_ANDROID_RGB_8BPC:
                case GM_RGB_MASKED:
                    bpp = 24;
                    break;
                case GM_15BIT:
                    bpp = 15;
                    break;
                case GM_16BIT:
                    bpp = 16;
                    break;
            }

            return new CcnImageMetaData
            {
                Width            = (uint)width,
                Height           = (uint)height,
                BPP              = bpp,
                GraphicMode      = graphicMode,
                Flags            = flags,
                DataSize         = dataSize,
                Checksum         = checksum,
                References       = references,
                HotspotX         = hotspotX,
                HotspotY         = hotspotY,
                ActionPointX     = actionPointX,
                ActionPointY     = actionPointY,
                TransparentColor = transparentColor
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (CcnImageMetaData)info;
            file.Position = 32; // Skip header to image data

            byte[] imageData = file.ReadBytes (Math.Min (meta.DataSize, (int)(file.Length - file.Position)));

            byte[] pixels = ConvertToRGBA (imageData, meta);
            var bitmap = BitmapSource.Create ((int)meta.Width, (int)meta.Height, 96, 96,
                                             PixelFormats.Bgra32, null, pixels, (int)meta.Width * 4);

            var ccnImage = new PngImageData (bitmap, info);
            using (var ms     = new MemoryStream())
            using (var writer = new BinaryWriter (ms))
            {
                writer.Write (meta.References);
                writer.Write (meta.HotspotX);
                writer.Write (meta.HotspotY);
                writer.Write (meta.ActionPointX);
                writer.Write (meta.ActionPointY);
                writer.Write (meta.TransparentColor);
                writer.Write (meta.GraphicMode);
                writer.Write (meta.Flags);
                ccnImage.CustomChunks["cCNh"] = ms.ToArray();
            }

            return ccnImage;
        }


        private byte[] ConvertToRGBA (byte[] input, CcnImageMetaData meta)
        {
            int width     = (int)meta.Width;
            int height    = (int)meta.Height;
            byte[] output = new byte[width * height * 4];

            switch (meta.GraphicMode)
            {
            case GM_ANDROID_RGBA_8BPC:
                ConvertAndroidRGBA (input, output, width, height);
                break;
            case GM_ANDROID_RGBA_4BPC:
                ConvertAndroidRGBA4Bit (input, output, width, height);
                break;
            case GM_ANDROID_RGBA_5BPC:
                ConvertAndroidRGBA5Bit (input, output, width, height);
                break;
            case GM_ANDROID_RGB_8BPC:
                ConvertAndroidRGB (input, output, width, height);
                break;
            case GM_RGB_MASKED:
                ConvertRGBMasked (input, output, width, height, meta);
                break;
            case GM_ANDROID_JPEG:
                // TODO: Add jpeg parser
                break;
            case GM_15BIT:
                Convert15BitRGB (input, output, width, height);
                break;
            case GM_16BIT:
                Convert16BitRGB (input, output, width, height);
                break;
            case GM_RGBA_8BPC:
                ConvertRGBA32 (input, output, width, height);
                break;
            case GM_FLASH_RGBA:
                ConvertFlashRGBA (input, output, width, height);
                break;
            default:
                for (int i = 0; i < output.Length; i += 4)
                {
                    output[i    ] = 128;
                    output[i + 1] = 128;
                    output[i + 2] = 128;
                    output[i + 3] = 255;
                }
                break;
            }

            return output;
        }

        private void ConvertAndroidRGBA (byte[] input, byte[] output, int width, int height)
        {
            int pixelCount = Math.Min (input.Length / 4, width * height);
            for (int i = 0; i < pixelCount; i++)
            {
                int idx = i * 4;
                output[idx    ] = input[idx + 2];
                output[idx + 1] = input[idx + 1];
                output[idx + 2] = input[idx    ];
                output[idx + 3] = input[idx + 3];
            }
        }

        private void ConvertAndroidRGBA4Bit (byte[] input, byte[] output, int width, int height)
        {
            int pixelCount = Math.Min (input.Length / 2, width * height);
            for (int i = 0; i < pixelCount; i++)
            {
                ushort pixel = (ushort)(input[i * 2] | (input[i * 2 + 1] << 8));
                int dstIdx = i * 4;

                output[dstIdx    ] = (byte)(((pixel >>  8) & 0xF) * 17);
                output[dstIdx + 1] = (byte)(((pixel >>  4) & 0xF) * 17);
                output[dstIdx + 2] = (byte)(( pixel        & 0xF) * 17);
                output[dstIdx + 3] = (byte)(((pixel >> 12) & 0xF) * 17);
            }
        }

        private void ConvertAndroidRGBA5Bit (byte[] input, byte[] output, int width, int height)
        {
            int pixelCount = Math.Min (input.Length / 2, width * height);
            for (int i = 0; i < pixelCount; i++)
            {
                ushort pixel = (ushort)(input[i * 2] | (input[i * 2 + 1] << 8));
                int dstIdx = i * 4;

                output[dstIdx    ] = (byte)(((pixel >>  10) & 0x1F) * 255 / 31);
                output[dstIdx + 1] = (byte)(((pixel >>   5) & 0x1F) * 255 / 31);
                output[dstIdx + 2] = (byte)(( pixel         & 0x1F) * 255 / 31);
                output[dstIdx + 3] = (byte)(( pixel >>  15) == 1 ? 255 : 0);
            }
        }

        private void ConvertAndroidRGB (byte[] input, byte[] output, int width, int height)
        {
            int stride = ((width * 3 + 3) & ~3);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIdx =  y * stride + x * 3;
                    int dstIdx = (y * width + x) * 4;

                    if (srcIdx + 2 < input.Length)
                    {
                        output[dstIdx    ] = input[srcIdx + 2];
                        output[dstIdx + 1] = input[srcIdx + 1];
                        output[dstIdx + 2] = input[srcIdx    ];
                        output[dstIdx + 3] = 255;
                    }
                }
            }
        }

        private void ConvertRGBMasked (byte[] input, byte[] output, int width, int height, CcnImageMetaData meta)
        {
            int position = 0;
            bool hasRLE = (meta.Flags & (FLAG_RLE | FLAG_RLEW | FLAG_RLET)) != 0;

            System.Diagnostics.Debug.WriteLine ($"ConvertRGBMasked: Fusion={CcnOpener.FusionVersion}, Build={CcnOpener.BuildNumber}, Seeded={CcnOpener.IsSeeded} Offset={position} DataSize={meta.DataSize}");

            int command = 0;
            bool rleLoop = false;
            bool rleCommander = false;

            if (hasRLE && input.Length > 0)
                command = input[position++];

            int rgbPadPixels = GetPaddingForVersion (width, 3, 2, meta);
            int rgbPadBytes = rgbPadPixels * 3;

            byte b = 0, g = 0, r = 0;

            // Transparent color is stored as 0xAARRGGBB (Windows COLORREF format)
            byte transB = (byte)( meta.TransparentColor        & 0xFF);
            byte transG = (byte)((meta.TransparentColor >> 8)  & 0xFF);
            byte transR = (byte)((meta.TransparentColor >> 16) & 0xFF);

            System.Diagnostics.Debug.WriteLine ($"  Size: {width}x{height}, Flags: 0x{meta.Flags:X2}, RLE: {hasRLE}, Padding: {rgbPadBytes} bytes");
            //System.Diagnostics.Debug.WriteLine ($"  TransparentColor: R={transR:X2} G={transG:X2} B={transB:X2}");

            bool swapToRGB = (CcnOpener.FusionVersion == 3.0f && !CcnOpener.IsSeeded);

            //System.Diagnostics.Debug.WriteLine ($"  Fusion3NonSeeded: {swapToRGB}");

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int dstIdx = (y * width + x) * 4;
                    if (!hasRLE || !rleLoop || rleCommander)
                    {
                        if (position + 2 < input.Length)
                        {
                            b = input[position++];
                            g = input[position++];
                            r = input[position++];
                            rleLoop = true;
                        }
                    }

                    if (swapToRGB)
                    {
                        output[dstIdx    ] = r;
                        output[dstIdx + 1] = g;
                        output[dstIdx + 2] = b;
                    }
                    else
                    {
                        output[dstIdx    ] = b;
                        output[dstIdx + 1] = g;
                        output[dstIdx + 2] = r;
                    }

                    if ((meta.Flags & FLAG_ALPHA) == 0 && r == transR && g == transG && b == transB)
                        output[dstIdx + 3] = 0;
                    else
                        output[dstIdx + 3] = 255;

                    if (hasRLE && --command == 0)
                    {
                        if (position < input.Length)
                        {
                            command = input[position++];
                            rleCommander = false;
                            rleLoop = false;

                            if (command > 128)
                            {
                                command -= 128;
                                rleCommander = true;
                            }
                            else if (command == 0)
                                rleLoop = true;
                        }
                    }
                }

                position += rgbPadBytes;
            }

            // Alpha mask handling
            if ((meta.Flags & FLAG_ALPHA) != 0 && position < input.Length)
            {
                int alphaPadBytes = GetAlphaPadding (width);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int dstIdx = (y * width + x) * 4;
                        if (position < input.Length)
                            output[dstIdx + 3] = input[position++];
                    }
                    position += alphaPadBytes;
                }
            }
        }

        private int GetPaddingForVersion (int width, int pointSize, int bytes, CcnImageMetaData meta)
        {
            bool useRLET = (meta.Flags & FLAG_RLET) != 0;
            if (!useRLET || CcnOpener.IsPlus || CcnOpener.FusionVersion < 2.0f)
            {
                int pad = bytes - (width * pointSize % bytes);
                if (pad == bytes)
                    return 0;
                return (int)Math.Ceiling (pad / (float)pointSize);
            }
            else if (CcnOpener.IsAndroid || CcnOpener.IsIOS)
                return 0;
            else if (CcnOpener.BuildNumber < 280)
                return (width * pointSize % bytes) * pointSize;
            else
                return (width % bytes) * pointSize;
        }

        private int GetAlphaPadding (int width)
        {
            return (4 - (width % 4)) % 4;
        }

        private void Convert15BitRGB (byte[] input, byte[] output, int width, int height)
        {
            int stride = ((width * 2 + 3) & ~3);

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int srcIdx = y * stride + x * 2;
                int dstIdx = (y * width + x) * 4;

                if (srcIdx + 1 < input.Length)
                {
                    ushort pixel = (ushort)(input[srcIdx] | (input[srcIdx + 1] << 8));
                    output[dstIdx    ] = (byte)(((pixel & 0x001F) * 255 / 31));
                    output[dstIdx + 1] = (byte)(((pixel & 0x03E0) >> 5) * 255 / 31);
                    output[dstIdx + 2] = (byte)(((pixel & 0x7C00) >> 10) * 255 / 31);
                    output[dstIdx + 3] = 255;
                }
            }

        }

        private void Convert16BitRGB (byte[] input, byte[] output, int width, int height)
        {
            int stride = ((width * 2 + 3) & ~3);

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int srcIdx = y * stride + x * 2;
                int dstIdx = (y * width + x) * 4;

                if (srcIdx + 1 < input.Length)
                {
                    ushort pixel = (ushort)(input[srcIdx] | (input[srcIdx + 1] << 8));
                    output[dstIdx    ] = (byte)(((pixel & 0x001F) * 255 / 31));
                    output[dstIdx + 1] = (byte)(((pixel & 0x07E0) >> 5) * 255 / 63);
                    output[dstIdx + 2] = (byte)(((pixel & 0xF800) >> 11) * 255 / 31);
                    output[dstIdx + 3] = 255;
                }
            }

        }

        private void ConvertRGBA32 (byte[] input, byte[] output, int width, int height)
        {
            int pixelCount = Math.Min (input.Length / 4, width * height);
            for (int i = 0; i < pixelCount; i++)
            {
                int idx = i * 4;
                output[idx    ] = input[idx + 2];
                output[idx + 1] = input[idx + 1];
                output[idx + 2] = input[idx    ];
                output[idx + 3] = input[idx + 3];
            }
        }

        private void ConvertFlashRGBA (byte[] input, byte[] output, int width, int height)
        {
            int pixelCount = Math.Min (input.Length / 4, width * height);
            for (int i = 0; i < pixelCount; i++)
            {
                int idx = i * 4;
                output[idx    ] = input[idx + 3];
                output[idx + 1] = input[idx + 2];
                output[idx + 2] = input[idx + 1];
                output[idx + 3] = input[idx    ];
            }
        }

        FixedSetSetting CCNOutputFormat = new FixedSetSetting (Properties.Settings.Default)
        {
            Name = "CCNOutputFormat",
            Text = Localization._T ("Output Format"),
            Description = Localization._T ("Image format to use"),
            ValuesSet = Enum.GetNames (typeof (CCNOutputFormatE)),
        };

        /*LocalResourceSetting CCNCompressionEnabled = new LocalResourceSetting ("CCNCompressionEnabled")
                {
                    Text = Localization._T ("Enable compression"),
                    Description = Localization._T ("Image compression to use"),
        };*/

        public override void Write (Stream file, ImageData image)
        {
            var formatName = CCNOutputFormat.Get<string>();
            if (!Enum.TryParse<CCNOutputFormatE>(formatName, out var format))
                throw new InvalidOperationException ($"Unknown output format: {formatName}");

            bool compress = false;//CCNCompressionEnabled.Get<bool>();
            int references = 1;
            short hotspotX = 0;
            short hotspotY = 0;
            byte flags = 0;
            short actionPointX = hotspotX;
            short actionPointY = hotspotY;
            uint transparentColor = 0x0000FE00;

            if (image is PngImageData ccnData && ccnData.CustomChunks.TryGetValue ("cCNh", out var chunk))
            {
                using (var ms = new MemoryStream (chunk))
                using (var reader = new BinaryReader (ms))
                {
                    references          = reader.ReadInt32();
                    hotspotX            = reader.ReadInt16();
                    hotspotY            = reader.ReadInt16();
                    actionPointX        = reader.ReadInt16();
                    actionPointY        = reader.ReadInt16();
                    transparentColor    = reader.ReadUInt32();
                    byte originalFormat = reader.ReadByte();
                    //flags             = reader.ReadByte();
                    format = originalFormat != 0 ? (CCNOutputFormatE)originalFormat : format;
                }
            }

            WriteImage (file, image, format, compress, flags, hotspotX, hotspotY,
                       actionPointX, actionPointY, transparentColor, references);
        }

        private void WriteImage (Stream file, ImageData image, CCNOutputFormatE format, 
                               bool compress, byte flags, short hotspotX, short hotspotY,
                               short actionPointX, short actionPointY, 
                               uint transparentColor, int references)
        {
            var bitmap = image.Bitmap;
            if (bitmap.Format != PixelFormats.Bgra32)
                bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);

            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;

            // Validate dimensions
            if (width <= 0 || width > 30000 || height <= 0 || height > 30000)
                throw new InvalidOperationException ("Image dimensions are out of valid range");

            // Get pixel data
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            bitmap.CopyPixels (pixels, stride, 0);

            byte[] imageData = ConvertFromBGRA (pixels, width, height, format);

            /*if (format == CCNOutputFormatE.RGBMasked && HasTransparency (pixels))
                flags |= FLAG_ALPHA;*/

            // Apply compression if enabled
            byte[] outputData;
            int uncompressedSize = imageData.Length;
            /*if (compress)
            {
                flags |= FLAG_LZX;
                outputData = Utils.Compress (imageData);
            }
            else
            {*/
                outputData = imageData;
           //}

            // Write the image
            using (var writer = new BinaryWriter (file, System.Text.Encoding.Default, true))
            {
                uint dataSize = (uint)(outputData.Length + (compress ? 4 : 0));
                // Write header (32 bytes)
                writer.Write ((uint)0);                  // Checksum (placeholder)
                writer.Write ((uint)references);         // References
                writer.Write ((uint)dataSize);           // Data size
                writer.Write ((short)width);             // Width
                writer.Write ((short)height);            // Height
                writer.Write ((byte)format);             // Graphic mode
                writer.Write ((byte)flags);              // Flags
                writer.Write ((short)0);                 // Reserved
                writer.Write ((short)hotspotX);          // HotspotX
                writer.Write ((short)hotspotY);          // HotspotY
                writer.Write ((short)actionPointX);      // ActionPointX
                writer.Write ((short)actionPointY);      // ActionPointY
                writer.Write ((uint)transparentColor);   // Transparent color

                /*if (compress)
                {
                    writer.Write (uncompressedSize);
                    writer.Write (outputData);
                }
                else*/
                    writer.Write (outputData);
            }

            UpdateChecksum (file);
        }

        private bool HasTransparency (byte[] pixels)
        {
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] < 255)
                    return true;
            }
            return false;
        }

        private uint FindTransparentColor (byte[] pixels, int width, int height)
        {
            // Look for first fully transparent pixel
            for (int i = 0; i < width * height; i++)
            {
                int idx = i * 4;
                if (pixels[idx + 3] == 0)
                {
                    // Return as Windows COLORREF (0x00BBGGRR)
                    return (uint)(pixels[idx] | (pixels[idx + 1] << 8) | (pixels[idx + 2] << 16));
                }
            }

            return 0x0000FE00;
        }
        private void UpdateChecksum (Stream stream)
        {
            stream.Position = 4; // Skip checksum field
            int checksum = 0;
            byte[] buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = stream.Read (buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                    checksum = (checksum + buffer[i]) & 0x7FFFFFFF;
            }
            stream.Position = 0;
            using (var writer = new BinaryWriter (stream, System.Text.Encoding.Default, true))
            {
                writer.Write (checksum);
            }
        }

        private byte[] ConvertFromBGRA (byte[] input, int width, int height, CCNOutputFormatE format)
        {
            switch (format)
            {
                case CCNOutputFormatE.AndroidRGBA8:
                    return ConvertFromBGRAToAndroidRGBA (input, width, height);

                case CCNOutputFormatE.AndroidRGBA4:
                    return ConvertFromBGRAToAndroidRGBA4 (input, width, height);

                case CCNOutputFormatE.AndroidRGBA5:
                    return ConvertFromBGRAToAndroidRGBA5 (input, width, height);

                case CCNOutputFormatE.AndroidRGB8:
                    return ConvertFromBGRAToAndroidRGB (input, width, height);

                case CCNOutputFormatE.RGBMasked:
                    return ConvertFromBGRAToRGBMasked (input, width, height);

                case CCNOutputFormatE.RGB15:
                    return ConvertFromBGRATo15Bit (input, width, height);

                case CCNOutputFormatE.RGB16:
                    return ConvertFromBGRATo16Bit (input, width, height);

                case CCNOutputFormatE.RGBA8:
                    return ConvertFromBGRAToRGBA32 (input, width, height);

                case CCNOutputFormatE.FlashRGBA:
                    return ConvertFromBGRAToFlashRGBA (input, width, height);

                default:
                    throw new NotSupportedException ($"Output format {format} is not supported");
            }
        }

        private byte[] ConvertFromBGRAToAndroidRGBA (byte[] input, int width, int height)
        {
            byte[] output = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                int idx = i * 4;
                output[idx    ] = input[idx + 2]; // R
                output[idx + 1] = input[idx + 1]; // G
                output[idx + 2] = input[idx    ]; // B
                output[idx + 3] = input[idx + 3]; // A
            }
            return output;
        }

        private byte[] ConvertFromBGRAToAndroidRGBA4 (byte[] input, int width, int height)
        {
            byte[] output = new byte[width * height * 2];
            for (int i = 0; i < width * height; i++)
            {
                int srcIdx = i * 4;
                int dstIdx = i * 2;

                byte r = (byte)(input[srcIdx + 2] / 17);
                byte g = (byte)(input[srcIdx + 1] / 17);
                byte b = (byte)(input[srcIdx    ] / 17);
                byte a = (byte)(input[srcIdx + 3] / 17);

                ushort pixel = (ushort)((a << 12) | (r << 8) | (g << 4) | b);
                output[dstIdx    ] = (byte)(pixel & 0xFF);
                output[dstIdx + 1] = (byte)(pixel >> 8);
            }
            return output;
        }

        private byte[] ConvertFromBGRAToAndroidRGBA5 (byte[] input, int width, int height)
        {
            byte[] output = new byte[width * height * 2];
            for (int i = 0; i < width * height; i++)
            {
                int srcIdx = i * 4;
                int dstIdx = i * 2;

                byte r = (byte)(input[srcIdx + 2] * 31 / 255);
                byte g = (byte)(input[srcIdx + 1] * 31 / 255);
                byte b = (byte)(input[srcIdx    ] * 31 / 255);
                byte a = (byte)(input[srcIdx + 3] > 127 ? 1 : 0);

                ushort pixel = (ushort)((a << 15) | (r << 10) | (g << 5) | b);
                output[dstIdx    ] = (byte)(pixel & 0xFF);
                output[dstIdx + 1] = (byte)(pixel >> 8);
            }
            return output;
        }

        private byte[] ConvertFromBGRAToAndroidRGB (byte[] input, int width, int height)
        {
            int stride = ((width * 3 + 3) & ~3);
            byte[] output = new byte[stride * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = (y * width + x) * 4;
                    int dstIdx = y * stride + x * 3;

                    output[dstIdx    ] = input[srcIdx + 2]; // R
                    output[dstIdx + 1] = input[srcIdx + 1]; // G
                    output[dstIdx + 2] = input[srcIdx    ]; // B
                }
            }
            return output;
        }

        private byte[] ConvertFromBGRAToRGBMasked (byte[] input, int width, int height)
        {
            var meta = new CcnImageMetaData
            {
                Width       = (uint)width,
                Height      = (uint)height,
                GraphicMode = GM_RGB_MASKED,
                Flags       = HasTransparency (input) ? FLAG_ALPHA : (byte)0
            };

            // Calculate padding
            int rgbPadPixels = GetPaddingForVersion (width, 3, 2, meta);
            int rgbPadBytes  = rgbPadPixels * 3;
            int rgbStride    = width * 3 + rgbPadBytes;

            // Check if we need alpha channel
            bool hasAlpha = (meta.Flags & FLAG_ALPHA) != 0;

            int alphaPadBytes = hasAlpha ? GetAlphaPadding (width) : 0;
            int alphaStride = hasAlpha ? width + alphaPadBytes : 0;

            byte[] output = new byte[rgbStride * height + (hasAlpha ? alphaStride * height : 0)];
            int position = 0;

            // Write RGB data
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = (y * width + x) * 4;
                    output[position++] = input[srcIdx    ]; // B
                    output[position++] = input[srcIdx + 1]; // G
                    output[position++] = input[srcIdx + 2]; // R
                }
                position += rgbPadBytes;
            }

            // Write alpha mask if needed
            if (hasAlpha)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int srcIdx = (y * width + x) * 4 + 3;
                        output[position++] = input[srcIdx];
                    }
                    position += alphaPadBytes;
                }
            }

            return output;
        }

        private byte[] ConvertFromBGRATo15Bit (byte[] input, int width, int height)
        {
            int stride = ((width * 2 + 3) & ~3);
            byte[] output = new byte[stride * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = (y * width + x) * 4;
                    int dstIdx = y * stride + x * 2;

                    byte r = (byte)(input[srcIdx + 2] * 31 / 255);
                    byte g = (byte)(input[srcIdx + 1] * 31 / 255);
                    byte b = (byte)(input[srcIdx    ] * 31 / 255);

                    ushort pixel = (ushort)((r << 10) | (g << 5) | b);
                    output[dstIdx    ] = (byte)(pixel & 0xFF);
                    output[dstIdx + 1] = (byte)(pixel >> 8);
                }
            }
            return output;
        }

        private byte[] ConvertFromBGRATo16Bit (byte[] input, int width, int height)
        {
            int stride = ((width * 2 + 3) & ~3);
            byte[] output = new byte[stride * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = (y * width + x) * 4;
                    int dstIdx = y * stride + x * 2;

                    byte r = (byte)(input[srcIdx + 2] * 31 / 255);
                    byte g = (byte)(input[srcIdx + 1] * 63 / 255);
                    byte b = (byte)(input[srcIdx    ] * 31 / 255);

                    ushort pixel = (ushort)((r << 11) | (g << 5) | b);
                    output[dstIdx    ] = (byte)(pixel & 0xFF);
                    output[dstIdx + 1] = (byte)(pixel >> 8);
                }
            }
            return output;
        }

        private byte[] ConvertFromBGRAToRGBA32 (byte[] input, int width, int height)
        {
            byte[] output = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                int idx = i * 4;
                output[idx    ] = input[idx + 2]; // R
                output[idx + 1] = input[idx + 1]; // G
                output[idx + 2] = input[idx    ]; // B
                output[idx + 3] = input[idx + 3]; // A
            }
            return output;
        }

        private byte[] ConvertFromBGRAToFlashRGBA (byte[] input, int width, int height)
        {
            byte[] output = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                int idx = i * 4;
                output[idx    ] = input[idx + 3]; // A
                output[idx + 1] = input[idx + 2]; // R
                output[idx + 2] = input[idx + 1]; // G
                output[idx + 3] = input[idx    ]; // B
            }
            return output;
        }
    }
}