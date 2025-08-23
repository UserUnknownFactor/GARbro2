using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Leaf
{
    [Export(typeof(ImageFormat))]
    public class LgfFormat : ImageFormat
    {
        public override string         Tag { get { return "LGF"; } }
        public override string Description { get { return "Leaf image format"; } }
        public override uint     Signature { get { return 0; } }

        public LgfFormat ()
        {
            // fourth byte is BPP
            Signatures = new uint[] { 0x1866676C, 0x2066676C, 0x0966676C };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (8);
            int bpp = header[3];
            return new ImageMetaData
            {
                Width   = header.ToUInt16 (4),
                Height  = header.ToUInt16 (6),
                BPP     = 9 == bpp ? 8 : bpp,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            int stride = (int)info.Width * info.BPP / 8;
            var pixels = new byte[stride * (int)info.Height];
            stream.Position = 12;
            BitmapPalette palette = null;
            if (8 == info.BPP)
                palette = ReadPalette (stream.AsStream, 0x100, PaletteFormat.RgbX);
            if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                throw new EndOfStreamException();
            PixelFormat format = 24 == info.BPP ? PixelFormats.Bgr24
                               : 32 == info.BPP ? PixelFormats.Bgr32
                               : PixelFormats.Indexed8;
            return ImageData.Create (info, format, palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("LgfFormat.Write not implemented");
        }
    }
}
