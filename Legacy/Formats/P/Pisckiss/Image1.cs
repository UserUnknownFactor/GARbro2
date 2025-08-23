using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [040220][pisckiss] SCHREI-TEN

namespace GameRes.Formats.Pisckiss
{
    [Export(typeof(ImageFormat))]
    public class Bm1Format : ImageFormat
    {
        public override string         Tag { get { return "BMP/PISCKISS"; } }
        public override string Description { get { return "Pisckiss encrypted bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public Bm1Format ()
        {
            Extensions = new[] { "1" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (5);
            if ((header[0] & 0x42) != 0)
                return null;
            int shift = 0;
            uint width = 0;
            uint height = 0;
            for (int i = 0; i < 4; ++i)
            {
                uint v = header[i+1];
                width  |= ((v >> 4 * (i & 1)) & 0xF) << shift;
                height |= ((v >> 4 * (~i & 1)) & 0xF) << shift;
                shift += 4;
            }
            if (0 == width || 0 == height)
                return null;
            uint stride = (width * 3 + 3) & ~3u;
            uint length = 5 + stride * height;
            if (length != file.Length)
                return null;
            return new ImageMetaData {
                Width = width,
                Height = height,
                BPP = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 5;
            int stride = ((int)info.Width * 3 + 3) & ~3;
            var pixels = file.ReadBytes (stride * (int)info.Height);
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Bm1Format.Write not implemented");
        }
    }
}
