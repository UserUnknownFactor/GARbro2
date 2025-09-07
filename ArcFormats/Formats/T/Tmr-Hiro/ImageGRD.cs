using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Collections.Generic;
using System;
using System.Buffers;

namespace GameRes.Formats.TmrHiro
{
    internal class GrdMetaData : ImageMetaData
    {
        public int      Format;
        public int      AlphaSize;
        public int      RSize;
        public int      GSize;
        public int      BSize;
    }

    [Export(typeof(ImageFormat))]
    public class GrdFormat : ImageFormat
    {
        public override string         Tag { get { return "GRD/TMR-HIRO"; } }
        public override string Description { get { return "Tmr-Hiro ADV System image format"; } }
        public override uint     Signature { get { return 0; } }

        public GrdFormat ()
        {
            Extensions = new string[] { "grd", "" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x20);
            if (header[0] != 1 && header[0] != 2)
                return null;
            if (header[1] != 1 && header[1] != 0xA1 && header[1] != 0xA2)
                return null;
            int bpp = header.ToUInt16 (6);
            if (bpp != 24 && bpp != 32)
                return null;
            int screen_width  = header.ToUInt16 ( 2 );
            int screen_height = header.ToUInt16 ( 4 );
            int left          = header.ToUInt16 ( 8 );
            int right         = header.ToUInt16 (0xA);
            int top           = header.ToUInt16 (0xC);
            int bottom        = header.ToUInt16 (0xE);
            var info = new GrdMetaData {
                Format        = header.ToUInt16 (0),
                Width         = (uint)System.Math.Abs (right - left),
                Height        = (uint)System.Math.Abs (bottom - top),
                BPP           = bpp,
                OffsetX       = left,
                OffsetY       = screen_height - bottom,
                AlphaSize     = header.ToInt32 (0x10),
                RSize         = header.ToInt32 (0x14),
                GSize         = header.ToInt32 (0x18),
                BSize         = header.ToInt32 (0x1C),
            };
            if (0x20 + info.AlphaSize + info.RSize + info.BSize + info.GSize != stream.Length)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GrdMetaData)info;
            using (var reader = new GrdReader (stream.AsStream, meta))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GrdFormat.Write not implemented");
        }
    }

    internal sealed class GrdReader : IDisposable
    {
        Stream      m_input;
        GrdMetaData m_info;
        byte[]      m_output;
        int         m_pack_type;
        int         m_pixel_size;
        byte[]      m_channel;
        int         m_channel_size;
        byte[]      m_read_buffer;

        private static readonly ArrayPool<byte> s_bufferPool = ArrayPool<byte>.Shared;

        public PixelFormat Format { get; private set; }
        public        byte[] Data { get { return m_output; } }

        public GrdReader (Stream input, GrdMetaData info)
        {
            m_input = input;
            m_info  = info;
            if (24 == m_info.BPP)
                Format = PixelFormats.Bgr24;
            else if (m_info.AlphaSize > 0)
                Format = PixelFormats.Bgra32;
            else
                Format = PixelFormats.Bgr32;
            m_channel_size = (int)(m_info.Width * m_info.Height);
            m_pack_type = m_info.Format >> 8;
            m_pixel_size = m_info.BPP / 8;
            m_output = new byte[m_pixel_size * m_channel_size];
            m_channel = s_bufferPool.Rent(m_channel_size);
            m_read_buffer = s_bufferPool.Rent(4096);
        }

        public void Unpack ()
        {
            int next_pos = 0x20;
            if (32 == m_info.BPP && m_info.AlphaSize > 0)
            {
                UnpackChannel (3, next_pos, m_info.AlphaSize);
                next_pos += m_info.AlphaSize;
            }
            UnpackChannel (2, next_pos, m_info.RSize);
            next_pos += m_info.RSize;
            UnpackChannel (1, next_pos, m_info.GSize);
            next_pos += m_info.GSize;
            UnpackChannel (0, next_pos, m_info.BSize);
        }

        void UnpackChannel (int dst, int src_pos, int src_size)
        {
            m_input.Position = src_pos;

            if (1 == m_pack_type)
                UnpackRLE (m_input, src_size);
            else
            {
                var data = UnpackHuffman (m_input);
                if (0xA2 == m_pack_type)
                    UnpackLZ77 (data, m_channel);
                else
                {
                    using (var mem = new MemoryStream (data))
                        UnpackRLE (mem, data.Length);
                }
            }

            // Optimized channel interleaving
            if (m_pixel_size == 4)
                InterleaveChannel32bpp(dst);
            else
                InterleaveChannel24bpp(dst);
        }

        unsafe void InterleaveChannel32bpp(int channelIndex)
        {
            fixed(byte* pChannel = m_channel)
            fixed(byte* pOutput  = m_output)
            {
                int width = (int)m_info.Width;
                int height = (int)m_info.Height;

                for (int y = 0; y < height; y++)
                {
                    byte* src = pChannel + ((height - 1 - y) * width);
                    byte* dst = pOutput + (y * width * 4) + channelIndex;

                    for (int x = 0; x < width; x++)
                    {
                        *dst = *src++;
                        dst += 4;
                    }
                }
            }
        }

        unsafe void InterleaveChannel24bpp(int channelIndex)
        {
            fixed (byte* pChannel = m_channel)
            fixed (byte* pOutput  = m_output)
            {
                int width = (int)m_info.Width;
                int height = (int)m_info.Height;

                for (int y = 0; y < height; y++)
                {
                    byte* src = pChannel + ((height - 1 - y) * width);
                    byte* dst = pOutput + (y * width * 3) + channelIndex;

                    for (int x = 0; x < width; x++)
                    {
                        *dst = *src++;
                        dst += 3;
                    }
                }
            }
        }

        void UnpackRLE (Stream input, int src_size)
        {
            int src = 0;
            int dst = 0;
            while (src < src_size && dst < m_channel_size)
            {
                int count = input.ReadByte();
                if (-1 == count)
                    return;
                ++src;
                if (count > 0x7F)
                {
                    count &= 0x7F;
                    byte v = (byte)input.ReadByte();
                    ++src;

                    int remaining = Math.Min(count, m_channel_size - dst);
                    if (remaining > 8)
                    {
                        // Manual unrolled fill
                        unsafe
                        {
                            fixed (byte* pDst = &m_channel[dst])
                            {
                                byte* p = pDst;
                                int i = 0;
                                // Fill 8 bytes at a time
                                for (; i < remaining - 7; i += 8)
                                {
                                    p[0] = v; p[1] = v; p[2] = v; p[3] = v;
                                    p[4] = v; p[5] = v; p[6] = v; p[7] = v;
                                    p += 8;
                                }
                                // Fill remaining bytes
                                for (; i < remaining; i++)
                                    *p++ = v;
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < remaining; ++i)
                            m_channel[dst + i] = v;
                    }
                    dst += remaining;
                }
                else if (count > 0)
                {
                    int toRead = Math.Min(count, m_channel_size - dst);
                    int actuallyRead = input.Read(m_channel, dst, toRead);
                    src += actuallyRead;
                    dst += actuallyRead;
                    if (actuallyRead < toRead)
                        return;
                }
            }
        }

        static void UnpackLZ77 (byte[] input, byte[] output)
        {
            var special = input[8];
            int src = 12;
            int dst = 0;
            while (dst < output.Length && src < input.Length)
            {
                byte b = input[src++];
                if (b == special)
                {
                    if (src >= input.Length) break;
                    byte offset = input[src++];
                    if (offset != special)
                    {
                        if (src >= input.Length) break;
                        byte count = input[src++];
                        if (offset > special)
                            --offset;

                        int copyCount = Math.Min(count, output.Length - dst);
                        if (dst >= offset)
                            Binary.CopyOverlapped (output, dst - offset, dst, copyCount);
                        dst += copyCount;
                    }
                    else
                        output[dst++] = offset;
                }
                else
                    output[dst++] = b;
            }
        }

        const int RootNodeIndex = 0x1FE;
        int m_huffman_unpacked;

        byte[] UnpackHuffman (Stream input)
        {
            var tree        = CreateHuffmanTree (input);
            var unpacked    = new byte[m_huffman_unpacked];
            using (var bits = new LsbBitStream (input, true))
            {
                int dst = 0;
                while (dst < m_huffman_unpacked)
                {
                    int node = RootNodeIndex;
                    while (node > 0xFF)
                    {
                        if (0 != bits.GetNextBit())
                            node = tree[node].Right;
                        else
                            node = tree[node].Left;
                    }
                    unpacked[dst++] = (byte)node;
                }
            }
            return unpacked;
        }

        HuffmanNode[] CreateHuffmanTree (Stream input)
        {
            var nodes = new HuffmanNode[0x200];
            var tree = new List<int> (0x100);
            using (var reader = new ArcView.Reader (input))
            {
                m_huffman_unpacked = reader.ReadInt32();
                reader.ReadInt32(); // packed_size

                for (int i = 0; i < 0x100; i++)
                {
                    nodes[i].Freq = reader.ReadUInt32();
                    AddNode (tree, nodes, i);
                }
            }
            int last_node = 0x100;
            while (tree.Count > 1)
            {
                int l = tree[0];
                tree.RemoveAt (0);
                int r = tree[0];
                tree.RemoveAt (0);
                nodes[last_node].Freq = nodes[l].Freq + nodes[r].Freq;
                nodes[last_node].Left = l;
                nodes[last_node].Right = r;
                AddNode (tree, nodes, last_node++);
            }
            return nodes;
        }

        static void AddNode (List<int> tree, HuffmanNode[] nodes, int index)
        {
            uint freq = nodes[index].Freq;
            int i;
            for (i = 0; i < tree.Count; ++i)
                if (nodes[tree[i]].Freq > freq)
                    break;
            tree.Insert (i, index);
        }

        internal struct HuffmanNode
        {
            public uint Freq;
            public int  Left;
            public int  Right;
        }

        public void Dispose()
        {
            if (m_channel != null)
            {
                s_bufferPool.Return(m_channel, true);
                m_channel = null;
            }
            if (m_read_buffer != null)
            {
                s_bufferPool.Return(m_read_buffer, true);
                m_read_buffer = null;
            }
        }
    }
}
