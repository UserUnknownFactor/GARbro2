using GameRes.Compression;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Elf
{
    [Export(typeof(ImageFormat))]
    public class G24Format : ImageFormat
    {
        public override string         Tag { get { return "G24"; } }
        public override string Description { get { return "Ai5 engine RGB image format"; } }
        public override uint     Signature { get { return  0; } }
        public override bool      CanWrite { get { return  false; } }

        public G24Format ()
        {
            Extensions = new string[] { "g24", "g16", "g32" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream input)
        {
            int x = input.ReadInt16();
            int y = input.ReadInt16();
            int w = input.ReadInt16();
            int h = input.ReadInt16();
            if (w <= 0 || w > 0x1000 || h <= 0 || h > 0x1000
                || x < 0 || x > 0x800 || y < 0 || y > 0x800)
                return null;
            return new ImageMetaData {
                Width = (uint)w,
                Height = (uint)h,
                OffsetX = x,
                OffsetY = y,
                BPP = input.Name.HasExtension (".G16") ? 16
                    : input.Name.HasExtension (".G32") ? 32 : 24
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 8;
            int stride = (((int)info.Width * info.BPP / 8 + 3) & -4);
            var pixels = new byte[stride * (int)info.Height];
            using (var reader = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
            {
                if (pixels.Length != reader.Read (pixels, 0, pixels.Length))
                    throw new InvalidFormatException();
                var format = 24 == info.BPP ? PixelFormats.Bgr24
                           : 32 == info.BPP ? PixelFormats.Bgra32 
                                            : PixelFormats.Bgr555;
                return ImageData.CreateFlipped (info, format, null, pixels, stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var writer = new BinaryWriter (file))
            {
                var bitmap = image.Bitmap;
                if (bitmap.Format != PixelFormats.Bgr24 && 
                    bitmap.Format != PixelFormats.Bgr32 && 
                    bitmap.Format != PixelFormats.Bgra32 &&
                    bitmap.Format != PixelFormats.Bgr555)
                {
                    bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgr24, null, 0);
                }

                writer.Write ((short)image.OffsetX);
                writer.Write ((short)image.OffsetY);
                writer.Write ((short)bitmap.PixelWidth);
                writer.Write ((short)bitmap.PixelHeight);

                int bpp = bitmap.Format.BitsPerPixel / 8;
                int stride = (bitmap.PixelWidth * bpp + 3) & -4;
                var pixels = new byte[stride * bitmap.PixelHeight];
                bitmap.CopyPixels (pixels, stride, 0);

                // Flip vertically
                var flipped = new byte[pixels.Length];
                for (int y = 0; y < bitmap.PixelHeight; ++y)
                {
                    int src = y * stride;
                    int dst = (bitmap.PixelHeight - 1 - y) * stride;
                    System.Buffer.BlockCopy (pixels, src, flipped, dst, stride);
                }

                using (var lzss = new LzssStream (file, LzssMode.Compress, true))
                {
                    lzss.Write (flipped, 0, flipped.Length);
                }
            }
        }
    }

    [Export(typeof(ImageFormat))]
    public class Msk16Format : ImageFormat
    {
        public override string         Tag { get { return "MSK/G16"; } }
        public override string Description { get { return "Ai5 engine image mask"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return  false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream input)
        {
            if (!input.Name.HasExtension (".MSK"))
                return null;
            var header = input.ReadHeader (4);
            int w = header.ToInt16 (0);
            int h = header.ToInt16 (2);
            if (w * h + 4 != input.Length || w <= 0 || w > 0x1000 || h <= 0 || h > 0x1000)
                return null;
            return new ImageMetaData {
                Width = (uint)w,
                Height = (uint)h,
                BPP = 8,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 4;
            var pixels = stream.ReadBytes ((int)info.Width * (int)info.Height);
            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = (byte)(pixels[i] * 0xFF / 8);
            }
            return ImageData.Create (info, PixelFormats.Gray8, null, pixels, (int)info.Width);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var writer = new BinaryWriter (file))
            {
                var bitmap = image.Bitmap;
                if (bitmap.Format != PixelFormats.Gray8)
                    bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Gray8, null, 0);

                writer.Write ((short)bitmap.PixelWidth);
                writer.Write ((short)bitmap.PixelHeight);

                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight];
                bitmap.CopyPixels (pixels, bitmap.PixelWidth, 0);

                // Convert from 0xFF scale to 8 scale
                for (int i = 0; i < pixels.Length; ++i)
                    pixels[i] = (byte)(pixels[i] * 8 / 0xFF);

                writer.Write (pixels);
            }
        }
    }
}
