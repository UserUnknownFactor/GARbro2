using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.MicroVision
{
    internal class GpcMetaData : ImageMetaData
    {
        public  int Type;
    }

    [Export(typeof(ImageFormat))]
    public class GpcFormat : ImageFormat
    {
        public override string         Tag { get { return "GTX"; } }
        public override string Description { get { return "MicroVision image format"; } }
        public override uint     Signature { get { return 0x30435047; } } // 'GPC0'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x40);
            int flags = header.ToUInt16 (0xC);
            if (0 != (flags & 0x1000))
            {
                var v1header = file.ReadBytes (0x30);
                return new GpcMetaData {
                    Width  = v1header.ToUInt16 (0x10),
                    Height = v1header.ToUInt16 (0x12),
                    BPP    = 32,
                    Type   = 0x1000
                };
            }
            else if (0 != (flags & 0x2000))
            {
                var v2header = file.ReadBytes (0x50);
                if (v2header.ToUInt16 (0x14) != 1)
                    return null;
                return new GpcMetaData {
                    Width  = v2header.ToUInt16 (0x10),
                    Height = v2header.ToUInt16 (0x12),
                    BPP    = 32,
                    Type   = 0x2000
                };
            }
            else
                return null;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (GpcMetaData)info;
            if (0x2000 == meta.Type)
            {
                file.Position = 0x90;
                int stride = (int)meta.Width * 4;
                var pixels = new byte[stride * (int)meta.Height];
                if (pixels.Length != file.Read (pixels, 0, pixels.Length))
                    throw new EndOfStreamException();
                return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, stride);
            }
            throw new NotImplementedException();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GpcFormat.Write not implemented");
        }
    }
}
