using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Tama
{
    [Export(typeof(ImageFormat))]
    public class SurFormat : ImageFormat
    {
        public override string         Tag { get { return "SUR"; } }
        public override string Description { get { return "TamaSoft ADV system image"; } }
        public override uint     Signature { get { return 0x52555345; } } // 'ESUR'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x10);
            return new ImageMetaData
            {
                Width  = header.ToUInt32 (8),
                Height = header.ToUInt32 (12),
                BPP    = 32,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var pixels = new byte[info.Width * info.Height * 4];
            stream.Position = 0x20;
            UnpackLzss (stream.AsStream, pixels);
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SurFormat.Write not implemented");
        }

        /// <summary>
        /// Differs from a common LZSS implementation by frame offset encoding.
        /// </summary>
        void UnpackLzss (Stream input, byte[] output)
        {
            int dst = 0;
            var frame = new byte[0x1000];
            int frame_pos = 0xFEE;
            const int frame_mask = 0xFFF;
            int ctl = 2;
            while (dst < output.Length)
            {
                ctl >>= 1;
                if (1 == ctl)
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        throw new EndOfStreamException();
                    ctl |= 0x100;
                }
                if (0 != (ctl & 1))
                {
                    byte b = (byte)input.ReadByte();
                    frame[frame_pos++] = b;
                    frame_pos &= frame_mask;
                    output[dst++] = b;
                }
                else
                {
                    int lo = input.ReadByte();
                    int hi = input.ReadByte();
                    if (-1 == hi)
                        throw new EndOfStreamException();
                    int offset = hi >> 4 | lo << 4;
                    int count = Math.Min (3 + (hi & 0xF), output.Length - dst);
                    while (count --> 0)
                    {
                        byte v = frame[offset++];
                        offset &= frame_mask;
                        frame[frame_pos++] = v;
                        frame_pos &= frame_mask;
                        output[dst++] = v;
                    }
                }
            }
        }
    }
}
