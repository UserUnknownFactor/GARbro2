using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.HCSystem
{
    internal class OpfMetaData : ImageMetaData
    {
        public int  Stride;
        public int  DataOffset;
        public int  DataLength;
    }

    [Export(typeof(ImageFormat))]
    public class OpfFormat : ImageFormat
    {
        public override string         Tag { get { return "OPF"; } }
        public override string Description { get { return "hcsystem engine image format"; } }
        public override uint     Signature { get { return 0x2046504F; } } // 'OPF '

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            var info = new OpfMetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP    = header.ToInt32 (0xC),
                Stride = header.ToInt32 (0x10),
                DataOffset = header.ToInt32 (0x14),
                DataLength = header.ToInt32 (0x18),
            };
            if (info.BPP > 32 || info.DataOffset < 32)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (OpfMetaData)info;
            file.Position = meta.DataOffset;
            PixelFormat format;
            switch (meta.BPP)
            {
            case 24: format = PixelFormats.Bgr24; break;
            case 32: format = PixelFormats.Bgra32; break;
            default: throw new InvalidFormatException ("Not supported OPF color depth.");
            }
            var pixels = file.ReadBytes (meta.DataLength);
            return ImageData.Create (info, format, null, pixels, meta.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("OpfFormat.Write not implemented");
        }
    }
}
