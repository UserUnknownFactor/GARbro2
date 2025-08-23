using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.ProjectMyu
{
    [Export(typeof(ImageFormat))]
    public class KgrFormat : ImageFormat
    {
        public override string         Tag { get { return "KGR"; } }
        public override string Description { get { return "Project-μ obfuscated bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".kgr"))
                return null;
            var header = file.ReadHeader (0x36);
            if (!header.AsciiEqual ("BM"))
                return null;
            int bpp = header.ToUInt16 (0x1C);
            if (bpp != 16 && bpp != 24)
                return null;
            return new ImageMetaData {
                Width = header.ToUInt32 (0x12),
                Height = header.ToUInt32 (0x16),
                BPP = bpp,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x36;
            int stride = (int)info.Width * info.BPP / 8;
            var pixels = file.ReadBytes (stride * (int)info.Height);
            PixelFormat format;
            if (16 == info.BPP)
                format = PixelFormats.Bgr565;
            else
                format = PixelFormats.Bgr24;
            return ImageData.CreateFlipped (info, format, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("KgrFormat.Write not implemented");
        }
    }
}
