using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.TinkerBell
{
    [Export(typeof(ImageFormat))]
    public class Tb1Format : ImageFormat
    {
        public override string         Tag { get { return "TB1"; } }
        public override string Description { get { return "TinkerBell image format"; } }
        public override uint     Signature { get { return 0x4641454C; } } // 'LEAF'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            if (!header.AsciiEqual (4, "64K"))
                return null;
            int bpp = header.ToUInt16 (0x10);
            if (bpp != 24)
                return null;
            return new ImageMetaData {
                Width = header.ToUInt16 (0xC),
                Height = header.ToUInt16 (0xE),
                BPP = bpp,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            int stride = (int)info.Width * info.BPP / 8;
            var pixels = new byte[stride * (int)info.Height];
            file.Position = 0x18;
            LzssUnpack (file, pixels);
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, stride);
        }

        void LzssUnpack (IBinaryStream input, byte[] output)
        {
            var frame = new byte[0x1000];
            int frame_pos = 0xFEE;
            int bits = 0, bit_mask = 0;
            int dst = 0;
            while (dst < output.Length)
            {
                bit_mask >>= 1;
                if (0 == bit_mask)
                {
                    bits = ~input.ReadByte();
                    bit_mask = 0x80;
                }
                if (0 != (bits & bit_mask))
                {
                    byte v = (byte)~input.ReadUInt8();
                    output[dst++] = frame[frame_pos++ & 0xFFF] = v;
                }
                else
                {
                    int offset = ~input.ReadUInt16() & 0xFFFF;
                    int count = Math.Min ((offset & 0xF) + 3, output.Length - dst);
                    offset >>= 4;
                    while (count --> 0)
                    {
                        byte v = frame[offset++ & 0xFFF];
                        output[dst++] = frame[frame_pos++ & 0xFFF] = v;
                    }
                }
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Tb1Format.Write not implemented");
        }
    }
}
