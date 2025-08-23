using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.FC01
{
    internal class AcdMetaData : ImageMetaData
    {
        public int DataOffset;
        public int PackedSize;
        public int UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class AcdFormat : ImageFormat
    {
        public override string         Tag { get { return "ACD"; } }
        public override string Description { get { return "F&C Co. image format"; } }
        public override uint     Signature { get { return 0x20444341; } } // 'ACD'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x1c);
            int header_size = header.ToInt32 (8);
            if (!header.AsciiEqual (4, "1.00") || header_size < 0x1c)
                throw new NotSupportedException ("Not supported ACD image version");
            int packed_size = header.ToInt32 (0x0C);
            int unpacked_size = header.ToInt32 (0x10);
            return new AcdMetaData
            {
                Width = header.ToUInt32 (0x14),
                Height = header.ToUInt32 (0x18),
                BPP = 24,
                DataOffset = header_size,
                PackedSize = packed_size,
                UnpackedSize = unpacked_size,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (AcdMetaData)info;
            stream.Position = meta.DataOffset;
            var lzssReader = new MrgLzssReader (stream, meta.PackedSize, meta.UnpackedSize);
            lzssReader.Unpack();
            var decoder = new AcdDecoder (lzssReader.Data, meta);
            decoder.Unpack();
            return ImageData.Create (info, PixelFormats.Gray8, null, decoder.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AcdFormat.Write not implemented");
        }
    }

    internal class AcdDecoder
    {
        byte[]          m_input;
        byte[]          m_output;

        public byte[] Data { get { return m_output; } }

        public AcdDecoder (byte[] input, AcdMetaData info)
        {
            m_input = input;
            m_output = new byte[info.Width*info.Height];
        }

        int m_src;
        int m_bits;

        public byte[] Unpack ()
        {
            m_src = 0; // @@SB
            m_bits = 0;
            for (int dst = 0; dst < m_output.Length; dst++)
            {
                int pixel = 0;
                if (0 != GetBit())
                {
                    --pixel;
                    if (0 == GetBit())
                    {
                        pixel += 3;
                        int bit;
                        do
                        {
                            bit = GetBit();
                            pixel += pixel + bit;
                            bit = (pixel >> 8) & 1;
                            pixel &= 0xff;
                        }
                        while (0 == bit);
                        if (0 != pixel)
                        {
                            ++pixel;
                            pixel *= 0x28CCCCD;
                            pixel = (int)((uint)pixel >> 24);
                        }
                    }
                }
                m_output[dst] = (byte)pixel;
            }
            return m_output;
        }

        int GetBit ()
        {
            int bit = m_bits >> 7;
            m_bits = (m_bits << 1) & 0xff;
            if (0 == m_bits)
            {
                if (m_src >= m_input.Length)
                    throw new InvalidFormatException();
                m_bits = m_input[m_src++];
                bit = m_bits >> 7;
                m_bits = (m_bits << 1) & 0xff | 1;
            }
            return bit;
        }
    }
}
