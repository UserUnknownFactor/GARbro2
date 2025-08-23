using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Tanuki
{
    [Export(typeof(ImageFormat))]
    public class AmapFormat : ImageFormat
    {
        public override string         Tag { get { return "AMAP"; } }
        public override string Description { get { return "TanukiSoft bitmap format"; } }
        public override uint     Signature { get { return 0x50414D41; } } // 'AMAP'

        public AmapFormat ()
        {
            Extensions = new[] { "af" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            return new ImageMetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP    = 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x10;
            var pixels = LzssUnpack (file);
            return ImageData.Create (info, PixelFormats.Gray8, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AmapFormat.Write not implemented");
        }

        byte[] LzssUnpack (IBinaryStream input)
        {
            int unpacked_size = input.ReadInt32();
            var output = new byte[unpacked_size];
            var frame = new byte[0x1000];
            int frame_pos = 0xFEE;
            int dst = 0;
            int ctl = 0;
            while (dst < unpacked_size)
            {
                ctl >>= 1;
                if (0 == (ctl & 0x100))
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        break;
                    ctl |= 0xFF00;
                }
                if (0 != (ctl & 1))
                {
                    int b = input.ReadByte();
                    if (-1 == b)
                        break;
                    output[dst++] = frame[frame_pos++ & 0xFFF] = (byte)b;
                }
                else
                {
                    int lo = input.ReadByte();
                    if (-1 == lo)
                        break;
                    int hi = input.ReadByte();
                    if (-1 == hi)
                        break;
                    int offset = (hi & 0xF0) << 4 | lo;
                    for (int count = 3 + (~hi & 0xF); count != 0; --count)
                    {
                        byte v = frame[offset++ & 0xFFF];
                        output[dst++] = frame[frame_pos++ & 0xFFF] = v;
                    }
                }
            }
            return output;
        }
    }
}
