using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.AlphaSystem
{
    [Export(typeof(ImageFormat))]
    public class SfgFormat : ImageFormat
    {
        public override string         Tag { get { return "SFG"; } }
        public override string Description { get { return "Alpha System image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            int bpp = header.ToUInt16 (0);
            ushort width  = header.ToUInt16 (2);
            ushort height = header.ToUInt16 (4);
            if (bpp != 1 && bpp != 4 || width == 0 || height == 0)
                return null;
            int expected_length = width * height * bpp + 8;
            if (1 == bpp)
                expected_length += 0x400;
            if (expected_length != file.Length)
                return null;
            return new ImageMetaData {
                Width = width,
                Height = height,
                BPP = bpp * 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 8;
            BitmapPalette palette = null;
            PixelFormat format;
            if (8 == info.BPP)
            {
                palette = ReadPalette (file.AsStream, 0x100, PaletteFormat.BgrA);
                format = PixelFormats.Indexed8;
            }
            else
            {
                format = PixelFormats.Bgra32;
            }
            int stride = info.iWidth * info.BPP / 8;
            var pixels = new byte[stride * info.iHeight];
            file.Read (pixels, 0, pixels.Length);
            return ImageData.Create (info, format, palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SfgFormat.Write not implemented");
        }
    }
}
