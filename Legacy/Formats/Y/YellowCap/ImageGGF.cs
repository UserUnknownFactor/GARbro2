using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.YellowCap
{
    [Export(typeof(ImageFormat))]
    public class GgfFormat : ImageFormat
    {
        public override string         Tag { get { return "GGF"; } }
        public override string Description { get { return "BMP-embedded image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (10);
            if (!header.AsciiEqual (8, "BM"))
                return null;
            using (var bmp = OpenBmpStream (file))
            {
                var info = Bmp.ReadMetaData (bmp);
                if (null == info || info.Width != header.ToUInt32 (0) || info.Height != header.ToUInt32 (4))
                    return null;
                return info;
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var bmp = OpenBmpStream (file))
                return Bmp.Read (bmp, info);
        }

        IBinaryStream OpenBmpStream (IBinaryStream file)
        {
            var part = new StreamRegion (file.AsStream, 8, true);
            return new BinaryStream (part, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GgfFormat.Write not implemented");
        }
    }
}
