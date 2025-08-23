using System.IO;
using System.Text;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Amaterasu
{
    [Export(typeof(ImageFormat))]
    public class GrpFormat : ImageFormat
    {
        public override string         Tag { get { return "GRP"; } }
        public override string Description { get { return Localization._T ("GRPDescription"); } }
        public override uint     Signature { get { return 0x00505247; } } // 'GRP'
        public override bool      CanWrite { get { return true; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 4;
            var meta = new ImageMetaData();
            meta.OffsetX = file.ReadInt16();
            meta.OffsetY = file.ReadInt16();
            meta.Width   = file.ReadUInt16();
            meta.Height  = file.ReadUInt16();
            meta.BPP     = 32;
            return meta;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            int width = (int)info.Width;
            int height = (int)info.Height;
            int stride = width*4;
            byte[] pixels = new byte[stride*height];
            file.Position = 12;
            for (int row = height-1; row >= 0; --row)
            {
                if (stride != file.Read (pixels, row*stride, stride))
                    throw new InvalidFormatException();
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream stream, ImageData image)
        {
            using (var file = new BinaryWriter (stream, Encoding.ASCII, true))
            {
                file.Write (Signature);
                file.Write ((short)image.OffsetX);
                file.Write ((short)image.OffsetY);
                file.Write ((ushort)image.Width);
                file.Write ((ushort)image.Height);

                var bitmap = image.Bitmap;
                if (bitmap.Format != PixelFormats.Bgra32)
                {
                    bitmap = new FormatConvertedBitmap (image.Bitmap, PixelFormats.Bgra32, null, 0);
                }
                int stride = (int)image.Width * 4;
                byte[] row_data = new byte[stride];
                Int32Rect rect = new Int32Rect (0, (int)image.Height, (int)image.Width, 1);
                for (uint row = 0; row < image.Height; ++row)
                {
                    --rect.Y;
                    bitmap.CopyPixels (rect, row_data, stride, 0);
                    file.Write (row_data);
                }
            }
        }
    }
}
