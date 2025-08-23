using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Mink
{
    [Export(typeof(ImageFormat))]
    public class xxxFormat : ImageFormat
    {
        public override string         Tag { get { return "MCP"; } }
        public override string Description { get { return "Mink image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            return new ImageMetaData {
                Width  = header.ToUInt32 (8),
                Height = header.ToUInt32 (0xC),
                BPP    = header.ToInt32 (0x10),
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (xxxMetaData)info;

            return ImageData.Create (info, format, palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("McpFormat.Write not implemented");
        }
    }
}
