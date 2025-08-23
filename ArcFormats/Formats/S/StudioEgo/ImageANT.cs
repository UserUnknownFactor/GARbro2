using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Ego
{
    [Export(typeof(ImageFormat))]
    public class AntFormat : ImageFormat
    {
        public override string         Tag { get { return "ANT"; } }
        public override string Description { get { return "Studio e.go! bitmap format"; } }
        public override uint     Signature { get { return 0x49544E41; } } // 'ANTI'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x18);
            return new ImageMetaData
            {
                Width   = header.ToUInt32 (0xC),
                Height  = header.ToUInt32 (0x10),
                BPP     = 32,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var pixels = new byte[info.Width*info.Height*4];
            stream.Position = 0x18;
            int dst = 0;
            for (uint y = 0; y < info.Height; ++y)
            {
                while (dst < pixels.Length)
                {
                    byte a = stream.ReadUInt8();
                    if (0 == a)
                    {
                        byte count = stream.ReadUInt8();
                        if (0 == count)
                            break;
                        dst += count * 4;
                    }
                    else
                    {
                        stream.Read (pixels, dst, 3);
                        pixels[dst + 3] = (byte)a;
                        dst += 4;
                    }
                }
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AntFormat.Write not implemented");
        }
    }
}
