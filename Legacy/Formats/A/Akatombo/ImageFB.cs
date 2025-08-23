using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Akatombo
{
    [Export(typeof(ImageFormat))]
    public class FbFormat : ImageFormat
    {
        public override string         Tag { get { return "FB"; } }
        public override string Description { get { return "Akatombo image format"; } }
        public override uint     Signature { get { return 0x184246; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            if (!header.AsciiEqual ("FB"))
                return null;
            return new ImageMetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP = header[2],
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new FbReader (file, info);
            var pixels = reader.Unpack();
            return ImageData.CreateFlipped (info, reader.Format, null, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("FbFormat.Write not implemented");
        }
    }

    internal class FbReader
    {
        IBinaryStream   m_input;
        int             m_width;
        byte[]          m_output;

        public PixelFormat Format { get; private set; }
        public int         Stride { get; private set; }

        public FbReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_width = info.iWidth;
            Stride = 4 * m_width;
            m_output = new byte[Stride * info.iHeight];
            Format = PixelFormats.Bgr32;
        }

        uint    m_bits;

        public byte[] Unpack ()
        {
            m_input.Position = 8;
            m_bits = 0x80000000;
            for (int dst = 0; dst < m_output.Length; dst += 4)
            {
                int bit = GetNextBit();
                if (0 == bit)
                {
                    bit = GetNextBit();
                    int pos = GetNextBit();
                    if (bit != 0)
                        pos = dst + 4 * (pos - m_width);
                    else
                        pos = dst + 4 * (-m_width & -pos) - 4;
                    if (pos >= 0)
                        Buffer.BlockCopy (m_output, pos, m_output, dst, 4);
                }
                m_output[dst  ] += ReadDiff();
                m_output[dst+1] += ReadDiff();
                m_output[dst+2] += ReadDiff();
            }
            return m_output;
        }

        byte ReadDiff ()
        {
            int count = 1;
            while (GetNextBit() != 0)
            {
                ++count;
            }
            int n = 1;
            while (count --> 0)
            {
                n = (n << 1) | GetNextBit();
            }
            return (byte)(-(n & 1) ^ ((n >> 1) - 1));
        }

        byte[] m_bits_buffer = new byte[4];

        int GetNextBit ()
        {
            uint bit = m_bits >> 31;
            m_bits <<= 1;
            if (0 == m_bits)
            {
                if (0 == m_input.Read (m_bits_buffer, 0, 4))
                    throw new EndOfStreamException();
                m_bits = BigEndian.ToUInt32 (m_bits_buffer, 0);
                bit = m_bits >> 31;
                m_bits = (m_bits << 1) | 1;
            }
            return (int)bit;
        }
    }
}
