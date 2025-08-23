using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Hmp
{
    [Export(typeof(ImageFormat))]
    [ExportMetadata("Priority", -1)]
    public class AlpFormat : ImageFormat
    {
        public override string         Tag { get { return "ALP/BeF"; } }
        public override string Description { get { return "BeF bitmap mask format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".alp") || file.Length != 0x25800)
                return null;
            return new ImageMetaData { Width = 320, Height = 480, BPP = 8 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var pixels = file.ReadBytes (0x25800);
            for (int i = 0; i < pixels.Length; ++i)
                pixels[i] = (byte)(pixels[i] * 0xFF / 0x40);
            return ImageData.Create (info, PixelFormats.Gray8, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AlpFormat.Write not implemented");
        }
    }
}
