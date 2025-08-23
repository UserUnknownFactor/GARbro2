using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.YellowCap
{
    [Export(typeof(ImageFormat))]
    public class GefFormat : ImageFormat
    {
        public override string         Tag { get { return "GEF"; } }
        public override string Description { get { return "PNG-embedded image format"; } }
        public override uint     Signature { get { return 0x00010100; } }

        public GefFormat ()
        {
            Signatures = new uint[] { 0x00010100, 0xFF010100 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            if (header.ToUInt32 (0xC) != Png.Signature)
                return null;
            file.Position = 0xC;
            var info = Png.ReadMetaData (file);
            if (null == info || info.Width != header.ToUInt32 (4) || info.Height != header.ToUInt32 (8))
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var part = new StreamRegion (file.AsStream, 0xC, true))
            using (var png = new BinaryStream (part, file.Name))
                return Png.Read (png, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GefFormat.Write not implemented");
        }
    }
}
