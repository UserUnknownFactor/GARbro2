using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Gsx
{
    [Export(typeof(ImageFormat))]
    public class K2Format : ImageFormat
    {
        public override string         Tag { get { return "K2"; } }
        public override string Description { get { return "Toyo GSX image format"; } }
        public override uint     Signature { get { return 0x18324B; } } // 'K2'

        public K2Format ()
        {
            Signatures = new uint[] { 0x18324B, 0x20324B, 0x10324B, 0x0F324B, 0x08324B, 0x04324B, 0x01324B };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var bmp_header = Decompress (file, 0x36);
            using (var bmp = new BinMemoryStream (bmp_header))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var bmp_data = Decompress (file);
            using (var bmp = new BinMemoryStream (bmp_data))
                return Bmp.Read (bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("K2Format.Write not implemented");
        }

        internal byte[] Decompress (IBinaryStream input)
        {
            input.Position = 6;
            int unpacked_size = input.ReadInt32();
            return Decompress (input, unpacked_size);
        }

        internal byte[] Decompress (IBinaryStream input, int unpacked_size)
        {
            input.Position = 0x12;
            int data_pos = input.ReadInt32();
            int bits_length = Math.Min (data_pos-0x10, (unpacked_size + 7) / 8);
            var ctl_bits = input.ReadBytes (bits_length);
            input.Position = 6 + data_pos;
            var output = new byte[unpacked_size];

            using (var mem = new MemoryStream (ctl_bits))
            using (var bits = new MsbBitStream (mem))
            using (var data = new MsbBitStream (input.AsStream, true))
            {
                int dst = 0;
                while (dst < unpacked_size)
                {
                    int ctl = bits.GetNextBit();
                    if (-1 == ctl)
                        break;
                    if (ctl != 0)
                    {
                        output[dst++] = (byte)data.GetBits (8);
                    }
                    else
                    {
                        int offset, count;
                        if (bits.GetNextBit() != 0)
                        {
                            offset = data.GetBits (14);
                            count = data.GetBits (4) + 3;
                        }
                        else
                        {
                            offset = data.GetBits (9);
                            count = data.GetBits (3) + 2;
                        }
                        count = Math.Min (count, output.Length-dst);
                        Binary.CopyOverlapped (output, dst-offset-1, dst, count);
                        dst += count;
                    }
                }
                return output;
            }
        }
    }
}
