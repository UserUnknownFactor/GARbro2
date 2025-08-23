using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.WestGate
{
    [Export(typeof(ImageFormat))]
    public class NbmpFormat : ImageFormat
    {
        public override string         Tag { get { return "NBMP"; } }
        public override string Description { get { return "West Gate bitmap format"; } }
        public override uint     Signature { get { return 0x504D424E; } } // 'NBMP'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x2C);
            if (header.ToInt32 (4) != 0x28)
                return null;
            int bpp = header.ToInt16 (0x12);
            if (bpp != 24 && bpp != 32 && bpp != 8)
                return null;
            return new ImageMetaData {
                Width = header.ToUInt32 (8),
                Height = header.ToUInt32 (0xC),
                BPP = bpp,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            int stride = ((int)info.Width * info.BPP / 8 + 3) & ~3;
            file.Position = 0x2C;
            BitmapPalette palette = null;
            if (8 == info.BPP)
                palette = ReadPalette (file.AsStream);
            var pixels = file.ReadBytes (stride * (int)info.Height);
            PixelFormat format = 8 == info.BPP ? PixelFormats.Indexed8
                              : 24 == info.BPP ? PixelFormats.Bgr24 : PixelFormats.Bgr32;
            return ImageData.CreateFlipped (info, format, palette, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("NbmpFormat.Write not implemented");
        }
    }
}
