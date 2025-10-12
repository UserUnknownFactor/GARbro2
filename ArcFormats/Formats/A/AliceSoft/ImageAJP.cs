using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.AliceSoft
{
    internal class AjpMetaData : ImageMetaData
    {
        public uint ImageOffset;
        public uint ImageSize;
        public uint AlphaOffset;
        public uint AlphaSize;
        public uint AlphaUnpacked;
    }

    [Export(typeof(ImageFormat))]
    public class AjpFormat : ImageFormat
    {
        public override string         Tag { get { return "AJP"; } }
        public override string Description { get { return "AliceSoft JPEG image format"; } }
        public override uint     Signature { get { return  0x504A41; } } // 'AJP\0'
        public override bool      CanWrite { get { return  true; } }

        internal static byte[] Key = {
            0x5D, 0x91, 0xAE, 0x87, 0x4A, 0x56, 0x41, 0xCD,
            0x83, 0xEC, 0x4C, 0x92, 0xB5, 0xCB, 0x16, 0x34
        };

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x24);
            int version     = header.ToInt32  (4);
            if (version < 0)
                return null;
            var info = new AjpMetaData
            {
                Width       = header.ToUInt32 (0xC ),
                Height      = header.ToUInt32 (0x10),
                BPP         = 32,
                ImageOffset = header.ToUInt32 (0x14),
                ImageSize   = header.ToUInt32 (0x18),
                AlphaOffset = header.ToUInt32 (0x1C),
                AlphaSize   = header.ToUInt32 (0x20),
            };
            if (version > 0)
                info.AlphaUnpacked = stream.ReadUInt32();
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (AjpMetaData)info;
            int stride = (int)meta.Width * 4;
            byte[] pixels;
            using (var jpeg = DecryptStream (stream.AsStream, meta.ImageOffset, meta.ImageSize))
            {
                var decoder = new JpegBitmapDecoder (jpeg,
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapSource bitmap = decoder.Frames[0];
                if (0 == meta.AlphaOffset || 0 == meta.AlphaSize)
                {
                    bitmap.Freeze();
                    return new ImageData (bitmap, info);
                }
                if (bitmap.Format.BitsPerPixel != 32)
                    bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgr32, null, 0);

                pixels = new byte[stride * (int)meta.Height];
                bitmap.CopyPixels (pixels, stride, 0);
            }
            Stream mask = DecryptStream (stream.AsStream, meta.AlphaOffset, meta.AlphaSize);
            byte[] alpha;
            if (meta.AlphaUnpacked != 0)
            {
                using (mask = new ZLibStream (mask, CompressionMode.Decompress))
                {
                    alpha = new byte[meta.AlphaUnpacked];
                    mask.Read (alpha, 0, alpha.Length);
                }
            }
            else
            {
                using (mask)
                {
                    alpha = ReadMask (mask);
                }
            }
            int src = 0;
            for (int dst = 3; dst < pixels.Length; dst += 4)
            {
                pixels[dst] = alpha[src++];
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, stride);
        }

        Stream DecryptStream (Stream input, uint offset, uint size)
        {
            var header = new byte[Key.Length];
            input.Position = offset;
            input.Read (header, 0, header.Length);
            for (int i = 0; i < header.Length; ++i)
                header[i] ^= Key[i];
            if (size > header.Length)
            {
                var rest = new StreamRegion (input, input.Position, size - header.Length, true);
                return new PrefixStream (header, rest);
            }
            else
                return new MemoryStream (header, 0, (int)size);
        }

        byte[] ReadMask (Stream input)
        {
            var header = new byte[0x40];
            input.Read (header, 0, header.Length);
            int width       = LittleEndian.ToInt32 (header, 0x18);
            int height      = LittleEndian.ToInt32 (header, 0x1C);
            int data_offset = LittleEndian.ToInt32 (header, 0x20);
            int pal_offset  = LittleEndian.ToInt32 (header, 0x24);
            var pixels = new byte[width * height];
            int dst = 0;
            input.Position = data_offset;
            while (dst < pixels.Length)
            {
                int c = input.ReadByte();
                if (-1 == c)
                    break;
                if (c < 0xF8)
                {
                    pixels[dst++] = (byte)c;
                    continue;
                }

                int count = 0;
                switch (c)
                {
                case 0xF8:
                    pixels[dst++] = (byte)input.ReadByte();
                    break;

                case 0xFC:
                    count = input.ReadByte();
                    input.Read (pixels, dst, 2);
                    count = count * 2 + 4;
                    Binary.CopyOverlapped (pixels, dst, dst+2, count);
                    dst += count+2;
                    break;

                case 0xFD:
                    count = input.ReadByte() + 4;
                    byte b = (byte)input.ReadByte();
                    while (count --> 0)
                        pixels[dst++] = b;
                    break;

                case 0xFE:
                    count = input.ReadByte() + 3;
                    Binary.CopyOverlapped (pixels, dst - width*2, dst, count);
                    dst += count;
                    break;

                case 0xFF:
                    count = input.ReadByte() + 3;
                    Binary.CopyOverlapped (pixels, dst - width, dst, count);
                    dst += count;
                    break;

                default:
                    throw new InvalidFormatException();
                }
            }
            input.Position = pal_offset;
            var index = ReadGrayPalette (input);
            for (int i = 0; i < pixels.Length; ++i)
                pixels[i] = index[pixels[i]];
            return pixels;
        }

        byte[] ReadGrayPalette (Stream input)
        {
            var palette = new byte[0x300];
            if (0x300 != input.Read (palette, 0, 0x300))
                throw new EndOfStreamException();
            var gray = new byte[0x100];
            for (int i = 0; i < 0x100; ++i)
            {
                int c = i * 3;
                gray[i] = (byte)((palette[c] + palette[c+1] + palette[c+2]) / 3);
            }
            return gray;
        }

        public override void Write (Stream file, ImageData image)
        {
            var bitmap = image.Bitmap;
            if (bitmap.Format != PixelFormats.Bgra32 && bitmap.Format != PixelFormats.Bgr24)
            {
                if (bitmap.Format.BitsPerPixel < 24)
                    bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgr24, null, 0);
                else
                    bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);
            }

            int stride = (int)image.Width * (bitmap.Format.BitsPerPixel / 8);
            var pixels = new byte[stride * (int)image.Height];
            bitmap.CopyPixels (pixels, stride, 0);

            bool hasAlpha = bitmap.Format == PixelFormats.Bgra32;

            byte[] jpegData;
            using (var ms = new MemoryStream())
            {
                var encoder = new JpegBitmapEncoder();
                encoder.QualityLevel = 90;

                BitmapSource jpegSource;
                if (hasAlpha)
                {
                    // Strip alpha
                    var rgbPixels = new byte[image.Width * image.Height * 3];
                    for (int i = 0, j = 0; i < pixels.Length; i += 4, j += 3)
                    {
                        rgbPixels[j    ] = pixels[i    ];
                        rgbPixels[j + 1] = pixels[i + 1];
                        rgbPixels[j + 2] = pixels[i + 2];
                    }
                    jpegSource = BitmapSource.Create ((int)image.Width, (int)image.Height,
                        96, 96, PixelFormats.Bgr24, null, rgbPixels, (int)image.Width * 3);
                }
                else
                {
                    jpegSource = BitmapSource.Create ((int)image.Width, (int)image.Height,
                        96, 96, PixelFormats.Bgr24, null, pixels, stride);
                }

                encoder.Frames.Add (BitmapFrame.Create (jpegSource));
                encoder.Save (ms);
                jpegData = ms.ToArray();
            }

            for (int i = 0; i < (jpegData.Length > 16 ? 16 : jpegData.Length); i++)
                jpegData[i] ^= Key[i];

            // Prepare alpha mask if needed
            byte[] maskData = new byte[0];
            if (hasAlpha)
            {
                var alpha = new byte[image.Width * image.Height];
                for (int i = 0, j = 3; i < alpha.Length; i++, j += 4)
                    alpha[i] = pixels[j];

                // Compress alpha with zlib
                using (var ms = new MemoryStream())
                {
                    using (var zlib = new ZLibStream (ms, CompressionMode.Compress, CompressionLevel.Level9))
                    {
                        zlib.Write (alpha, 0, alpha.Length);
                    }
                    maskData = ms.ToArray();
                }

                for (int i = 0; i < (maskData.Length > 16 ? 16 : maskData.Length); i++)
                    maskData[i] ^= Key[i];
            }

            // Write AJP file
            using (var writer = new BinaryWriter (file))
            {
                writer.Write (0x504A41);   // 'AJP\0'
                writer.Write ((int)1);     // version
                writer.Write ((int)0);     // reserved
                writer.Write ((uint)image.Width);
                writer.Write ((uint)image.Height);
                writer.Write ((uint)0x24); // JPEG offset
                writer.Write ((uint)jpegData.Length);
                writer.Write ((uint)(0x24 + jpegData.Length)); // mask offset
                writer.Write ((uint)maskData.Length);

                writer.Write (jpegData);
                if (maskData.Length > 0)
                    writer.Write (maskData);
            }
        }
    }
}
