using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Purple
{
    [Export(typeof(ImageFormat))]
    public class MskFormat : ImageFormat
    {
        public override string         Tag { get { return "MSK0"; } }
        public override string Description { get { return "Cvns engine grayscale image format"; } }
        public override uint     Signature { get { return 0x304B534D; } } // 'MSK0'

        public MskFormat ()
        {
            Extensions = new string[] { "msk" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            return new ImageMetaData
            {
                Width = header.ToUInt32 (8),
                Height = header.ToUInt32 (0xC),
                BPP = 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x10;
            var pixels = file.ReadBytes ((int)info.Width * (int)info.Height);
            return ImageData.Create (info, PixelFormats.Gray8, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MskFormat.Write not implemented");
        }
    }
}
