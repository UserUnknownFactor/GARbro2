using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.CsWare
{
    internal class B5MetaData : ImageMetaData
    {
        public bool SwapRgb;
    }

    [Export(typeof(ImageFormat))]
    public class B5Format : ImageFormat
    {
        public override string         Tag { get { return "B5"; } }
        public override string Description { get { return "CsWare image format"; } }
        public override uint     Signature { get { return 0x773562; } } // 'b5w'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            return new B5MetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP = 16,
                SwapRgb = header[2] != 'w',
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new B5Reader (file, (B5MetaData)info);
            reader.Unpack();
            return ImageData.Create (info, PixelFormats.Bgr555, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("B5Format.Write not implemented");
        }
    }

    internal class B5Reader
    {
        IBinaryStream   m_input;
        ushort[]        m_output;
        int             m_width;
        int             m_height;
        bool            m_swap;

        public Array    Data { get { return m_output; } }

        public B5Reader (IBinaryStream input, B5MetaData info)
        {
            m_input = input;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_swap = info.SwapRgb;
            m_output = new ushort[m_width * m_height];
        }

        public void Unpack ()
        {
            InitOffsetsTable();
            m_input.Position = 8;
            int dst_row = 0;
            for (int y = 0; y < m_height; ++y)
            {
                int dst = dst_row;
                for (int w = m_width; w > 0; )
                {
                    ushort v = m_input.ReadUInt16();
                    if (0 != (v & 0x8000))
                    {
                        if (m_swap)
                            v = (ushort)(((v | 0xFFE0) << 10) | (v >> 10) & 0x1F | v & 0x3E0);
                        else
                            v = (ushort)(v & 0x7FFF);
                        m_output[dst++] = v;
                        w--;
                    }
                    else
                    {
                        int count = v & 0xFF;
                        int src = m_offsets[v >> 8];
                        w -= count;
                        while (count --> 0)
                        {
                            m_output[dst] = m_output[dst+src];
                            ++dst;
                        }
                    }
                }
                dst_row += m_width;
            }
        }

        int[] m_offsets = new int[128];

        void InitOffsetsTable ()
        {
            int i = 0;
            for (int x = -1; x >= -8; --x)
            {
                m_offsets[i++] = x;
            }
            int offset = -m_width;
            for (int y = 8; y > 0; --y)
            {
                for (int x = 6; x >= -8; --x)
                {
                    m_offsets[i++] = offset + x;
                }
                offset -= m_width;
            }
        }
    }
}
