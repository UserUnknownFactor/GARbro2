using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Cocktail
{
    [Export(typeof(ImageFormat))]
    public class WmkFormat : ImageFormat
    {
        public override string         Tag { get { return "WMK"; } }
        public override string Description { get { return "Cocktail Soft bitmap mask format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            uint width = file.ReadUInt32();
            uint height = file.ReadUInt32();
            if (width * height + 0x10 != file.Length)
                return null;
            return new ImageMetaData {
                Width = width,
                Height = height,
                BPP = 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 8;
            var pixels = file.ReadBytes ((int)info.Width * (int)info.Height);
            return ImageData.Create (info, PixelFormats.Gray8, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("WmkFormat.Write not implemented");
        }
    }
}
