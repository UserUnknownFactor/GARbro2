using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Pan
{
    [Export(typeof(ImageFormat))]
    public class TblFormat : ImageFormat
    {
        public override string         Tag { get { return "TBL/PAN"; } }
        public override string Description { get { return "Pan engine bitmap mask"; } }
        public override uint     Signature { get { return 0x6C6274; } } // 'tbl'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            return new ImageMetaData {
                Width  = header.ToUInt32 (0x0C),
                Height = header.ToUInt32 (0x10),
                BPP    = 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x14;
            var pixels = file.ReadBytes ((int)info.Width * (int)info.Height);
            return ImageData.Create (info, PixelFormats.Gray8, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("TblFormat.Write not implemented");
        }
    }
}
