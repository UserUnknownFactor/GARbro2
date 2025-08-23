using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [990521][Sepia] Hikari no Naka de Dakishimete

namespace GameRes.Formats.Hmp
{
    [Export(typeof(ImageFormat))]
    public class CbfFormat : ImageFormat
    {
        public override string         Tag { get { return "CBF/MA"; } }
        public override string Description { get { return "h.m.p image format"; } }
        public override uint     Signature { get { return 0x432D414D; } } // 'MA-CBF'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x1C);
            if (!header.AsciiEqual ("MA-CBF"))
                return null;
            return new ImageMetaData {
                Width  = header.ToUInt32 (0x10),
                Height = header.ToUInt32 (0x14),
                BPP    = 16,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x24;
            int stride = info.iWidth * 2;
            var pixels = new byte[stride * info.iHeight];
            file.Read (pixels, 0, pixels.Length);
            return ImageData.CreateFlipped (info, PixelFormats.Bgr555, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CbfFormat.Write not implemented");
        }
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "NE")]
    [ExportMetadata("Target", "WAV")]
    public class NeFormat : ResourceAlias { }
}
