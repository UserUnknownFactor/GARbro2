using System.Collections.Generic;
using System.IO;
using GameRes.Formats;

namespace GameRes.Compression
{
    public class HuffmanStream : PackedStream<HuffmanDecompressor>
    {
        public HuffmanStream (Stream input, bool leave_open = false) : base (input, leave_open)
        {
        }
    }

    public class HuffmanDecompressor : Decompressor
    {
        MsbBitStream        m_input;

        public override void Initialize (Stream input)
        {
            m_input = new MsbBitStream (input, true);
        }

        const int TreeSize = 512;

        ushort[] lhs = new ushort[TreeSize];
        ushort[] rhs = new ushort[TreeSize];
        ushort m_token = 256;

        protected override IEnumerator<int> Unpack ()
        {
            m_token = 256;
            ushort root = CreateTree();
            for (;;)
            {
                ushort symbol = root;
                while (symbol >= 0x100)
                {
                    int bit = m_input.GetBits (1);
                    if (-1 == bit)
                        yield break;
                    if (bit != 0)
                        symbol = rhs[symbol];
                    else
                        symbol = lhs[symbol];
                }
                m_buffer[m_pos++] = (byte)symbol;
                if (0 == --m_length)
                    yield return m_pos;
            }
        }

        ushort CreateTree ()
        {
            int bit = m_input.GetBits (1);
            if (-1 == bit)
            {
                throw new EndOfStreamException ("Unexpected end of the Huffman-compressed stream.");
            }
            else if (bit != 0)
            {
                ushort v = m_token++;
                if (v >= TreeSize)
                    throw new InvalidFormatException ("Invalid Huffman-compressed stream.");
                lhs[v] = CreateTree();
                rhs[v] = CreateTree();
                return v;
            }
            else
            {
                return (ushort)m_input.GetBits (8);
            }
        }

        #region IDisposable Members
        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (disposing && !m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }

    public class HuffmanDecoder
    {
        byte[] m_src;
        byte[] m_dst;
        int m_input_pos;
        int m_remaining;

        public HuffmanDecoder (byte[] src, int index, int length, byte[] dst)
        {
            m_src = src;
            m_dst = dst;
            m_input_pos = index;
            m_remaining = length;
        }

        public HuffmanDecoder (byte[] src, byte[] dst) : this (src, 0, src.Length, dst)
        {
        }

        public byte[] Unpack ()
        {
            using (var packed = new BinMemoryStream (m_src, m_input_pos, m_remaining))
            using (var hstr = new HuffmanStream (packed))
            {
                hstr.Read (m_dst, 0, m_dst.Length);
                return m_dst;
            }
        }
    }
}
