using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Seraphim
{
    [Export(typeof(ImageFormat))]
    public class Cp3Format : ImageFormat
    {
        public override string         Tag { get { return "CP3"; } }
        public override string Description { get { return "Seraphim engine multi-frame image"; } }
        public override uint     Signature { get { return 0x58335043; } } // 'CP3X'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x3C);
            return new ImageMetaData {
                Width  = header.ToUInt32 (0x34),
                Height = header.ToUInt32 (0x38),
                BPP    = 32,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x3C;
            int stride = (int)info.Width * 4;
            var pixels = file.ReadBytes (stride * (int)info.Height);
            return ImageData.CreateFlipped (info, PixelFormats.Bgra32, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Cp3Format.Write not implemented");
        }
    }
}
