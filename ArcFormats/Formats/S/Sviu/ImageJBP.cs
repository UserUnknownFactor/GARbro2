using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Sviu
{
    [Export(typeof(ImageFormat))]
    public class JbpFormat : ImageFormat
    {
        public override string         Tag { get { return "JBP"; } }
        public override string Description { get { return "SVIU System image format"; } }
        public override uint     Signature { get { return 0x3150424A; } } // 'JBP1'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            return new ImageMetaData {
                Width  = header.ToUInt16 (0x10),
                Height = header.ToUInt16 (0x12),
                BPP    = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var input = file.ReadBytes ((int)file.Length);
            var reader = new Purple.JbpReader (input, 0);
            var pixels = reader.Unpack();
            return ImageData.Create (info, PixelFormats.Bgr32, null, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("JbpFormat.Write not implemented");
        }
    }
}
