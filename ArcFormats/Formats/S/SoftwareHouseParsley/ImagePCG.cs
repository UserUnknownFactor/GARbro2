using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Parsley
{
    [Export(typeof(ImageFormat))]
    public class PcgFormat : ImageFormat
    {
        public override string         Tag { get { return "PCG"; } }
        public override string Description { get { return "Software House Parsley image format"; } }
        public override uint     Signature { get { return 0x30474350; } } // 'PCG0'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            uint width =  header.ToUInt32 (12);
            uint height = header.ToUInt32 (16);
            if (file.Length != width * height * 4 + 0x14)
                return null;
            return new ImageMetaData {
                Width  = width,
                Height = height,
                BPP    = 32,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            int stride = info.iWidth * 4;
            file.Position = 0x14;
            var pixels = file.ReadBytes (stride * info.iHeight);
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PcgFormat.Write not implemented");
        }
    }
}
