using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Carriere
{
    [Export(typeof(ImageFormat))]
    public class CgdFormat : ImageFormat
    {
        public override string         Tag { get { return "CGD/CARRIERE"; } }
        public override string Description { get { return "Carriere image format"; } }
        public override uint     Signature { get { return 0x00646763; } } // 'cgd'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            return new ImageMetaData {
                Width  = header.ToUInt32 (12),
                Height = header.ToUInt32 (16),
                BPP    = 32,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x14;
            var pixels = file.ReadBytes ((int)info.Width * (int)info.Height * 4);
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CgdFormat.Write not implemented");
        }
    }
}
