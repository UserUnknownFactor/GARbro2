using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;

namespace GameRes.Formats.Elf
{
    [Export(typeof(ImageFormat))]
    public class RmtFormat : ImageFormat
    {
        public override string         Tag { get { return "RMT"; } }
        public override string Description { get { return "Ai6 engine compressed image format"; } }
        public override uint     Signature { get { return  0x20544D52; } } // 'RMT '
        public override bool      CanWrite { get { return  false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            return new ImageMetaData {
                OffsetX = header.ToInt32 (4),
                OffsetY = header.ToInt32 (8),
                Width   = header.ToUInt32 (0xC),
                Height  = header.ToUInt32 (0x10),
                BPP     = 32,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            int stride = (int)info.Width * 4;
            var pixels = new byte[stride * (int)info.Height];
            file.Position = 0x14;
            using (var input = new LzssStream (file.AsStream, LzssMode.Decompress, true))
                input.Read (pixels, 0, pixels.Length);
            for (int i = 4; i < stride; i += 4)
            {
                pixels[i  ] += pixels[i-4];
                pixels[i+1] += pixels[i-3];
                pixels[i+2] += pixels[i-2];
                pixels[i+3] += pixels[i-1];
            }
            for (int i = stride; i < pixels.Length; i += 4)
            {
                pixels[i  ] += pixels[i-stride];
                pixels[i+1] += pixels[i-stride+1];
                pixels[i+2] += pixels[i-stride+2];
                pixels[i+3] += pixels[i-stride+3];
            }
            return ImageData.CreateFlipped (info, PixelFormats.Bgra32, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var writer = new BinaryWriter (file))
            {
                var bitmap = image.Bitmap;
                if (bitmap.Format != PixelFormats.Bgra32)
                    bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);

                writer.Write (0x20544D52); // 'RMT '
                writer.Write ((int)image.OffsetX);
                writer.Write ((int)image.OffsetY);
                writer.Write ((uint)bitmap.PixelWidth);
                writer.Write ((uint)bitmap.PixelHeight);

                int stride = bitmap.PixelWidth * 4;
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

                // Apply delta encoding
                for (int i = pixels.Length - 4; i >= stride; i -= 4)
                {
                    flipped[i      ] -= flipped[i - stride];
                    flipped[i + 1] -= flipped[i - stride + 1];
                    flipped[i + 2] -= flipped[i - stride + 2];
                    flipped[i + 3] -= flipped[i - stride + 3];
                }

                for (int i = stride - 4; i >= 4; i -= 4)
                {
                    flipped[i      ] -= flipped[i - 4];
                    flipped[i + 1] -= flipped[i - 3];
                    flipped[i + 2] -= flipped[i - 2];
                    flipped[i + 3] -= flipped[i - 1];
                }

                using (var lzss = new LzssWriter (file))
                {
                    lzss.Pack (flipped, 0, flipped.Length);
                }
            }
        }
    }
}
