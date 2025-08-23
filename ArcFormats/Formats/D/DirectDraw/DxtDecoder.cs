using System;
using GameRes.Utility;

namespace GameRes.Formats.DirectDraw
{
    public class DxtDecoder
    {
        readonly byte[] m_input;
        readonly byte[] m_output;
        readonly int    m_width;
        readonly int    m_height;
        readonly int    m_output_stride;

        public byte[] Output { get { return m_output; } }

        public DxtDecoder (byte[] input, ImageMetaData info)
        {
            if (input == null)
                throw new ArgumentNullException (nameof(input));

            m_input = input;
            m_width = (int)info.Width;
            m_output_stride = m_width * 4;
            m_height = (int)info.Height;
            m_output = new byte[m_output_stride * m_height];
        }

        public byte[] UnpackDXT1 ()
        {
            int src = 0;
            for (int y = 0; y < m_height; y += 4)
            for (int x = 0; x < m_width; x += 4)
            {
                DecompressDXT1Block (m_input, src, y, x);
                src += 8;
            }

            return m_output;
        }

        public byte[] UnpackDXT3 ()
        {
            int src = 0;
            for (int y = 0; y < m_height; y += 4)
            for (int x = 0; x < m_width; x += 4)
            {
                DecompressDXT3Block (m_input, src, y, x);
                src += 16;
            }
            return m_output;
        }

        public byte[] UnpackDXT5 ()
        {
            int src = 0;
            for (int y = 0; y < m_height; y += 4)
            for (int x = 0; x < m_width; x += 4)
            {
                DecompressDXT5Block (m_input, src, y, x);
                src += 16;
            }
            return m_output;
        }

        public byte[] UnpackBC5()
        {
            int src = 0;
            for (int y = 0; y < m_height; y += 4)
            for (int x = 0; x < m_width; x += 4)
            {
                DecompressBC5Block (m_input, src, y, x);
                src += 16;
            }
            return m_output;
        }

        readonly byte[] m_dxt_buffer = new byte[16];
        readonly byte[] m_alpha_data = new byte[16];

        void DecompressDXT1Block (byte[] input, int src, int block_y, int block_x)
        {
            ReadDXT1Color (input, src, 0);
            ReadDXT1Color (input, src + 2, 4);

            ushort c0 = LittleEndian.ToUInt16 (input, src);
            ushort c1 = LittleEndian.ToUInt16 (input, src + 2);
            bool has_alpha = c0 <= c1;

            for (int i = 0; i < 4; ++i)
            {
                if (has_alpha)
                {
                    m_dxt_buffer[8 + i] = (byte)((m_dxt_buffer[i] + m_dxt_buffer[4 + i]) >> 1);
                    m_dxt_buffer[12 + i] = 0;
                }
                else
                {
                    m_dxt_buffer[8 + i] = (byte)(((m_dxt_buffer[i] << 1) + m_dxt_buffer[4 + i]) / 3);
                    m_dxt_buffer[12 + i] = (byte)(((m_dxt_buffer[4 + i] << 1) + m_dxt_buffer[i]) / 3);
                }
            }

            uint map = LittleEndian.ToUInt32 (input, src + 4);
            for (int y = 0; y < 4 && (block_y + y) < m_height; ++y)
            for (int x = 0; x < 4 && (block_x + x) < m_width; ++x)
            {
                int color = (int)(map & 3) << 2;
                int dst = m_output_stride * (block_y + y) + (block_x + x) * 4;
                m_output[dst    ] = m_dxt_buffer[color];
                m_output[dst + 1] = m_dxt_buffer[color + 1];
                m_output[dst + 2] = m_dxt_buffer[color + 2];
                m_output[dst + 3] = m_dxt_buffer[color + 3];
                map >>= 2;
            }
        }

        void ReadDXT1Color (byte[] input, int src, int idx)
        {
            ushort color = LittleEndian.ToUInt16 (input, src);
            int b = color & 0x1F;
            int g = (color >> 5) & 0x3F;
            int r = color >> 11;

            m_dxt_buffer[idx    ] = (byte)(b << 3 | b >> 2);
            m_dxt_buffer[idx + 1] = (byte)(g << 2 | g >> 4);
            m_dxt_buffer[idx + 2] = (byte)(r << 3 | r >> 2);
            m_dxt_buffer[idx + 3] = 0xFF;
        }

        void DecompressDXT3Block (byte[] input, int src, int block_y, int block_x)
        {
            int alpha_pos = 0;
            for (int i = 0; i < 8; ++i)
            {
                byte a = input[src++];
                m_alpha_data[alpha_pos++] = (byte)((a & 0xF) * 17);
                m_alpha_data[alpha_pos++] = (byte)((a >> 4) * 17);
            }

            ReadDXT1Color (input, src, 0);
            ReadDXT1Color (input, src + 2, 4);

            for (int i = 0; i < 4; ++i)
            {
                m_dxt_buffer[8  + i] = (byte)(((m_dxt_buffer[i] << 1) + m_dxt_buffer[4 + i]) / 3);
                m_dxt_buffer[12 + i] = (byte)(((m_dxt_buffer[4 + i] << 1) + m_dxt_buffer[i]) / 3);
            }

            uint map = LittleEndian.ToUInt32 (input, src + 4);
            for (int y = 0; y < 4 && (block_y + y) < m_height; ++y)
            for (int x = 0; x < 4 && (block_x + x) < m_width; ++x)
            {
                int color = (int)(map & 3) << 2;
                int dst = m_output_stride * (block_y + y) + (block_x + x) * 4;
                m_output[dst    ] = m_dxt_buffer[color];
                m_output[dst + 1] = m_dxt_buffer[color + 1];
                m_output[dst + 2] = m_dxt_buffer[color + 2];
                m_output[dst + 3] = m_alpha_data[y * 4 + x];
                map >>= 2;
            }
        }

        public void DecompressDXT5Block (byte[] input, int src, int block_y, int block_x)
        {
            byte alpha0 = input[src];
            byte alpha1 = input[src + 1];

            DecompressDXT5Alpha (input, src + 2, m_dxt_buffer);

            ushort color0 = LittleEndian.ToUInt16 (input, src + 8);
            ushort color1 = LittleEndian.ToUInt16 (input, src + 10);

            byte r0, g0, b0, r1, g1, b1;
            Rgb565ToRgb888 (color0, out r0, out g0, out b0);
            Rgb565ToRgb888 (color1, out r1, out g1, out b1);

            uint code = LittleEndian.ToUInt32 (input, src + 12);

            for (int y = 0; y < 4 && (block_y + y) < m_height; ++y)
            for (int x = 0; x < 4 && (block_x + x) < m_width; ++x)
            {
                int alpha_code = m_dxt_buffer[4 * y + x];
                byte alpha = InterpolateAlpha (alpha0, alpha1, alpha_code);

                int dst = m_output_stride * (block_y + y) + (block_x + x) * 4;
                switch (code & 3)
                {
                case 0:
                    PutPixel (dst, r0, g0, b0, alpha);
                    break;
                case 1:
                    PutPixel (dst, r1, g1, b1, alpha);
                    break;
                case 2:
                    PutPixel (dst, 
                        (byte)((2 * r0 + r1) / 3), 
                        (byte)((2 * g0 + g1) / 3), 
                        (byte)((2 * b0 + b1) / 3), 
                        alpha);
                    break;
                case 3:
                    PutPixel (dst, 
                        (byte)((r0 + 2 * r1) / 3), 
                        (byte)((g0 + 2 * g1) / 3), 
                        (byte)((b0 + 2 * b1) / 3), 
                        alpha);
                    break;
                }
                code >>= 2;
            }
        }

        void DecompressBC5Block(byte[] input, int src, int block_y, int block_x)
        {
            var red_indices = new byte[16];
            var green_indices = new byte[16];

            byte red0 = input[src];
            byte red1 = input[src + 1];
            DecompressDXT5Alpha(input, src + 2, red_indices);

            byte green0 = input[src + 8];
            byte green1 = input[src + 9];
            DecompressDXT5Alpha(input, src + 10, green_indices);

            for (int y = 0; y < 4 && (block_y + y) < m_height; ++y)
            for (int x = 0; x < 4 && (block_x + x) < m_width; ++x)
            {
                int idx = y * 4 + x;

                byte red = InterpolateBC5Value(red0, red1, red_indices[idx]);
                byte green = InterpolateBC5Value(green0, green1, green_indices[idx]);

                int dst = m_output_stride * (block_y + y) + (block_x + x) * 4;

                // BC5 stores RG data, B is typically 0, A is 255
                // For normal maps, you might reconstruct Z, but standard BC5 is just RG
                m_output[dst    ] = 0;      // B
                m_output[dst + 1] = green;  // G
                m_output[dst + 2] = red;    // R
                m_output[dst + 3] = 0xFF;   // A
            }
        }

        static byte InterpolateBC5Value(byte v0, byte v1, int index)
        {
            if (v0 > v1)
            {
                // 8-value interpolation
                switch (index)
                {
                case 0: return v0;
                case 1: return v1;
                case 2: return (byte)((6 * v0 + 1 * v1) / 7);
                case 3: return (byte)((5 * v0 + 2 * v1) / 7);
                case 4: return (byte)((4 * v0 + 3 * v1) / 7);
                case 5: return (byte)((3 * v0 + 4 * v1) / 7);
                case 6: return (byte)((2 * v0 + 5 * v1) / 7);
                case 7: return (byte)((1 * v0 + 6 * v1) / 7);
                default: return 0;
                }
            }
            else
            {
                // 6-value interpolation with 0 and 255
                switch (index)
                {
                case 0: return v0;
                case 1: return v1;
                case 2: return (byte)((4 * v0 + 1 * v1) / 5);
                case 3: return (byte)((3 * v0 + 2 * v1) / 5);
                case 4: return (byte)((2 * v0 + 3 * v1) / 5);
                case 5: return (byte)((1 * v0 + 4 * v1) / 5);
                case 6: return 0;
                case 7: return 0xFF;
                default: return 0;
                }
            }
        }

        static void Rgb565ToRgb888 (ushort color, out byte r, out byte g, out byte b)
        {
            int t = (color >> 11) * 255 + 16;
            r = (byte)((t / 32 + t) / 32);
            t = ((color & 0x07E0) >> 5) * 255 + 32;
            g = (byte)((t / 64 + t) / 64);
            t = (color & 0x001F) * 255 + 16;
            b = (byte)((t / 32 + t) / 32);
        }

        static byte InterpolateAlpha (byte alpha0, byte alpha1, int code)
        {
            if (code == 0)
                return alpha0;
            if (code == 1)
                return alpha1;

            if (alpha0 > alpha1)
            {
                switch (code)
                {
                case 2: return (byte)((6 * alpha0 + 1 * alpha1) / 7);
                case 3: return (byte)((5 * alpha0 + 2 * alpha1) / 7);
                case 4: return (byte)((4 * alpha0 + 3 * alpha1) / 7);
                case 5: return (byte)((3 * alpha0 + 4 * alpha1) / 7);
                case 6: return (byte)((2 * alpha0 + 5 * alpha1) / 7);
                case 7: return (byte)((1 * alpha0 + 6 * alpha1) / 7);
                default: return 0;
                }
            }
            else
            {
                switch (code)
                {
                case 2: return (byte)((4 * alpha0 + 1 * alpha1) / 5);
                case 3: return (byte)((3 * alpha0 + 2 * alpha1) / 5);
                case 4: return (byte)((2 * alpha0 + 3 * alpha1) / 5);
                case 5: return (byte)((1 * alpha0 + 4 * alpha1) / 5);
                case 6: return 0;
                case 7: return 0xFF;
                default: return 0;
                }
            }
        }

        static void DecompressDXT5Alpha (byte[] input, int src, byte[] output)
        {
            int dst = 0;
            for (int j = 0; j < 2; ++j)
            {
                if (src + 3 > input.Length)
                    break;
                int block = input[src++];
                block |= input[src++] << 8;
                block |= input[src++] << 16;

                for (int i = 0; i < 8; ++i)
                {
                    output[dst++] = (byte)(block & 7);
                    block >>= 3;
                }
            }
        }

        void PutPixel (int dst, byte r, byte g, byte b, byte a)
        {
            m_output[dst    ] = b;
            m_output[dst + 1] = g;
            m_output[dst + 2] = r;
            m_output[dst + 3] = a;
        }
    }
}
