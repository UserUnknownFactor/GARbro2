using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// [010810][Powered] Fight! Makoto

namespace GameRes.Formats.Powerd
{
    [Export(typeof(ImageFormat))]
    public class NclFormat : ImageFormat
    {
        public override string         Tag { get { return "NCL"; } }
        public override string Description { get { return "Powerd image format"; } }
        public override uint     Signature { get { return 0x4C4C4543; } } // 'CELL'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            if (header.ToInt32 (4) != 0x010100)
                return null;
            return new ImageMetaData {
                Width  = header.ToUInt32 (0x18),
                Height = header.ToUInt32 (0x1C),
                BPP    = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            ImageData base_img;
            using (var input = new StreamRegion (file.AsStream, 0x24, true))
            using (var bmp = new BinaryStream (input, file.Name))
            {
                var bmp_info = Bmp.ReadMetaData (bmp) as BmpMetaData;
                if (null == bmp_info)
                    throw new InvalidFormatException();
                bmp.Position = 0;
                base_img = Bmp.Read (bmp, bmp_info);
                file.Position = 0x24 + bmp_info.ImageLength;
                if (file.PeekByte() == -1)
                    return base_img;
            }
            using (var input = new StreamRegion (file.AsStream, file.Position, true))
            using (var bmp = new BinaryStream (input, file.Name))
            {
                var bmp_info = Bmp.ReadMetaData (bmp) as BmpMetaData;
                if (null == bmp_info)
                    return base_img;
                bmp.Position = 0;
                var alpha_img = Bmp.Read (bmp, bmp_info);
                var alpha_bmp = new FormatConvertedBitmap (alpha_img.Bitmap, PixelFormats.Gray8, null, 0);
                var alpha = new byte[alpha_bmp.PixelWidth * alpha_bmp.PixelHeight];
                alpha_bmp.CopyPixels (alpha, alpha_bmp.PixelWidth, 0);

                var base_bmp = new FormatConvertedBitmap (base_img.Bitmap, PixelFormats.Bgr32, null, 0);
                int stride = base_bmp.PixelWidth * 4;
                var pixels = new byte[stride * base_bmp.PixelHeight];
                base_bmp.CopyPixels (pixels, stride, 0);
                int asrc = 0;
                for (int dst = 3; dst < pixels.Length; dst += 4)
                {
                    pixels[dst] = alpha[asrc++];
                }
                return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("NclFormat.Write not implemented");
        }
    }
}
