using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.UMeSoft
{
    internal class IkeMetaData : ImageMetaData
    {
        public int  UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class IkeFormat : ImageFormat
    {
        public override string         Tag { get { return "BMP/IKE"; } }
        public override string Description { get { return "ike-compressed bitmap"; } }
        public override uint     Signature { get { return 0x6B69899D; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x11);
            if (!header.AsciiEqual (2, "ike") || !header.AsciiEqual (0xF, "BM"))
                return null;
            int unpacked_size = IkeReader.DecodeSize (header[10], header[11], header[12]);
            using (var bmp = IkeReader.CreateStream (file, 0x36))
            {
                var info = Bmp.ReadMetaData (bmp);
                if (null == info)
                    return null;
                return new IkeMetaData {
                    Width = info.Width,
                    Height = info.Height,
                    BPP = info.BPP,
                    UnpackedSize = unpacked_size,
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (IkeMetaData)info;
            using (var bmp = IkeReader.CreateStream (file, meta.UnpackedSize))
                return Bmp.Read (bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("IkeFormat.Write not implemented");
        }
    }
}
