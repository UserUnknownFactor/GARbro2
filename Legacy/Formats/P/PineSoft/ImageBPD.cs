using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.PineSoft
{
    [Export(typeof(ImageFormat))]
    public class BpdFormat : ImageFormat
    {
        public override string         Tag { get { return "BPD"; } }
        public override string Description { get { return "PineSoft image format"; } }
        public override uint     Signature { get { return 0x445042; } } // 'BPD'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            return new ImageMetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP = 32,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var pixels = new byte[info.iWidth * info.iHeight * 4];
            file.Position = 8;
            file.Read (pixels, 0, pixels.Length);
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BpdFormat.Write not implemented");
        }
    }
}
