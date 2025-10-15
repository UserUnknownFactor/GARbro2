using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Elf
{
    internal class GccMetaData : ImageMetaData
    {
        public uint Signature;
    }

    [Export(typeof(ImageFormat))]
    public class GccFormat : ImageFormat
    {
        public override string         Tag { get { return "GCC"; } }
        public override string Description { get { return "AI5WIN engine image format"; } }
        public override uint     Signature { get { return  0x6d343252; } } // 'R24m'
        public override bool      CanWrite { get { return  false; } }

        public GccFormat ()
        {
            // 'R24m', 'R24n', 'G24m', 'G24n'
            Signatures = new uint[] { 0x6d343252, 0x6E343252, 0x6D343247, 0x6E343247 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (12);
            return new GccMetaData
            {
                Width     = header.ToUInt16 (8),
                Height    = header.ToUInt16 (10),
                BPP       = 'm' == header[3] ? 32 : 24,
                OffsetX   = header.ToInt16 (4),
                OffsetY   = header.ToInt16 (6),
                Signature = header.ToUInt32 (0),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GccMetaData)info;
            var reader = new Reader (stream.AsStream, meta);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var writer = new BinaryWriter (file))
            {
                var bitmap = image.Bitmap;
                bool has_alpha = bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Pbgra32;

                if (bitmap.Format != PixelFormats.Bgr24 && bitmap.Format != PixelFormats.Bgra32)
                    bitmap = new FormatConvertedBitmap (bitmap, has_alpha ? PixelFormats.Bgra32 : PixelFormats.Bgr24, null, 0);

                uint signature = has_alpha ? 0x6D343247u : 0x6E343247u; // G24m or G24n
                writer.Write (signature);
                writer.Write ((short)image.OffsetX);
                writer.Write ((short)image.OffsetY);
                writer.Write ((ushort)bitmap.PixelWidth);
                writer.Write ((ushort)bitmap.PixelHeight);

                if (has_alpha)
                {
                    writer.Write (0); // placeholder for compressed data size
                    writer.Write ((ushort)bitmap.PixelWidth);  // alpha width
                    writer.Write ((ushort)bitmap.PixelHeight); // alpha height
                    writer.Write (0); // placeholder for alpha offset
                }

                int pixel_bytes = bitmap.Format.BitsPerPixel / 8;
                int stride = bitmap.PixelWidth * pixel_bytes;
                var pixels = new byte[stride * bitmap.PixelHeight];
                bitmap.CopyPixels (pixels, stride, 0);

                // Separate RGB and alpha if needed
                var rgb_data = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 3];
                byte[] alpha_data = null;

                if (has_alpha)
                {
                    alpha_data = new byte[bitmap.PixelWidth * bitmap.PixelHeight];
                    for (int i = 0, src = 0; i < rgb_data.Length; i += 3, src += 4)
                    {
                        rgb_data[i] = pixels[src];
                        rgb_data[i + 1] = pixels[src + 1];
                        rgb_data[i + 2] = pixels[src + 2];
                        alpha_data[i / 3] = pixels[src + 3];
                    }
                }
                else
                    Buffer.BlockCopy (pixels, 0, rgb_data, 0, rgb_data.Length);

                FlipRgbVertically (rgb_data, bitmap.PixelWidth, bitmap.PixelHeight);

                long data_start = file.Position;
                using (var lzss = new LzssWriter (file))
                {
                    lzss.Pack (rgb_data, 0, rgb_data.Length);
                }

                if (has_alpha)
                {
                    long alpha_offset = file.Position - data_start;

                    // NOTE: simplified compress alpha (just RLE)
                    WriteCompressedAlpha (file, alpha_data);

                    // Update header with actual offsets
                    long end_pos = file.Position;
                    file.Position = 0x0C;
                    writer.Write ((uint)(end_pos - data_start));
                    file.Position = 0x1C;
                    writer.Write ((uint)alpha_offset);
                    file.Position = end_pos;
                }
            }
        }

        private void FlipRgbVertically (byte[] data, int width, int height)
        {
            int stride = width * 3;
            var temp = new byte[stride];
            for (int y = 0; y < height / 2; ++y)
            {
                int top = y * stride;
                int bottom = (height - 1 - y) * stride;
                Buffer.BlockCopy (data, top, temp, 0, stride);
                Buffer.BlockCopy (data, bottom, data, top, stride);
                Buffer.BlockCopy (temp, 0, data, bottom, stride);
            }
        }

        private void WriteCompressedAlpha (Stream output, byte[] alpha)
        {
            using (var writer = new BinaryWriter (output))
            {
                int i = 0;
                while (i < alpha.Length)
                {
                    byte value = alpha[i];
                    int count = 1;

                    while (i + count < alpha.Length && alpha[i + count] == value && count < 127)
                        count++;

                    if (count > 1)
                    {
                        writer.Write ((byte)(0x80 | count));
                        writer.Write (value);
                    }
                    else
                    {
                        writer.Write ((byte)1);
                        writer.Write (value);
                    }

                    i += count;
                }
            }
        }

        internal class Reader
        {
            byte[]          m_input;
            GccMetaData     m_info;
            byte[]          m_output;
            int             m_width;
            int             m_height;
            int             m_alpha_width;
            int             m_alpha_height;

            public PixelFormat Format { get; private set; }
            public byte[]        Data { get { return m_output; } }

            public Reader (Stream input, GccMetaData info)
            {
                m_input = new byte[input.Length];
                input.Read (m_input, 0, m_input.Length);
                m_info = info;
                m_width = (int)m_info.Width;
                m_height = (int)m_info.Height;
            }

            public void Unpack ()
            {
                switch (m_info.Signature)
                {
                case 0x6E343247: UnpackNormal (DecompressLzss); break;         // G24n
                case 0x6D343247: UnpackMasked (DecompressLzss); break;         // G24m
                case 0x6E343252: UnpackNormal (DecompressAlternative); break;  // R24n
                case 0x6D343252: UnpackMasked (DecompressAlternative); break;  // R24m
                default:
                    throw new NotSupportedException();
                }
            }

            private void UnpackNormal (Action<int> decompressor)
            {
                decompressor (0x14);
                FlipPixelsVertically (m_width*3);
                Format = PixelFormats.Bgr24;
            }

            private void UnpackMasked (Action<int> decompressor)
            {
                decompressor (0x20);
                var alpha = DecompressAlphaChannel();
                if (m_alpha_width < (m_info.OffsetX + m_width) || m_alpha_height < (m_info.OffsetY + m_height))
                {
                    FlipPixelsVertically (m_width*3);
                    Format = PixelFormats.Bgr24;
                }
                else
                {
                    MergeAlphaChannel (alpha);
                    Format = PixelFormats.Bgra32;
                }
            }

            private void FlipPixelsVertically (int stride)
            {
                var pixels = new byte[m_output.Length];
                int dst = 0;
                for (int src = stride * (m_height-1); src >= 0; src -= stride)
                {
                    Buffer.BlockCopy (m_output, src, pixels, dst, stride);
                    dst += stride;
                }
                m_output = pixels;
            }

            private void MergeAlphaChannel (byte[] alpha)
            {
                Debug.Assert (m_alpha_width >= (m_info.OffsetX + m_width) && m_alpha_height >= (m_info.OffsetY + m_height));
                int src_stride = m_width * 3; 
                var pixels = new byte[m_width * m_height * 4];
                int dst = 0;
                int alpha_row = m_alpha_width * (m_alpha_height - m_info.OffsetY - 1);
                for (int row = m_width * (m_height-1); row >= 0; row -= m_width)
                {
                    int src = row*3;
                    for (int x = 0; x < m_width; ++x)
                    {
                        pixels[dst++] = m_output[src++];
                        pixels[dst++] = m_output[src++];
                        pixels[dst++] = m_output[src++];
                        pixels[dst++] = alpha[alpha_row + m_info.OffsetX + x];
                    }
                    alpha_row -= m_alpha_width;
                }
                m_output = pixels;
            }

            void DecompressLzss (int offset)
            {
                int out_length = m_width * m_height * 3;
                using (var input = new MemoryStream (m_input, offset, m_input.Length-offset))
                using (var lzss = new LzssReader (input, (int)input.Length, out_length))
                {
                    lzss.Unpack();
                    m_output = lzss.Data;
                }
            }

            int m_bit_index;
            int m_current_byte;
            int m_bit_mask;

            void InitBitReader (int start_index)
            {
                m_bit_index = start_index;
                m_bit_mask = 0x80;
            }

            bool ReadNextBit ()
            {
                m_bit_mask <<= 1;
                if (0x100 == m_bit_mask)
                {
                    m_current_byte = m_input[m_bit_index++];
                    m_bit_mask = 1;
                }
                return 0 != (m_current_byte & m_bit_mask);
            }

            byte[] DecompressAlphaChannel ()
            {
                m_alpha_width    = LittleEndian.ToUInt16 (m_input, 0x18);
                m_alpha_height   = LittleEndian.ToUInt16 (m_input, 0x1A);
                int total_pixels = m_alpha_width * m_alpha_height;
                var alpha = new byte[total_pixels];
                int bit_offset = 0x20 + LittleEndian.ToInt32 (m_input, 0x0C);
                InitBitReader (bit_offset);
                int data_offset = bit_offset + LittleEndian.ToInt32 (m_input, 0x1C);
                int output_pos = 0;
                while (output_pos < total_pixels)
                {
                    if (ReadNextBit())
                    {
                        int run_length = ReadRunLength();
                        byte value = m_input[data_offset++];
                        for (int i = 0; i < run_length; ++ i)
                        {
                            alpha[output_pos++] = value;
                        }
                    }
                    else
                    {
                        alpha[output_pos++] = m_input[data_offset++];
                    }
                }
                return alpha;
            }

            int ReadRunLength () // sub_444F60
            {
                int length = 1;
                int zero_bits = 0;
                while (!ReadNextBit())
                    ++zero_bits;
                while (zero_bits != 0)
                {
                    --zero_bits;
                    length <<= 1;
                    if (ReadNextBit())
                        length |= 1;
                }
                return length;
            }

            int m_output_pos;

            private void DecompressAlternative (int offset) // sub_445620
            {
                byte[] work_buffer = new byte[0x10001];

                int data_ptr = offset + LittleEndian.ToInt32 (m_input, 0x10); // within m_input
                InitBitReader (offset);
                int total_size = 3 * m_width * m_height;
                m_output = new byte[total_size];
                m_output_pos = 0;
                int processed = 0;
                while (processed < total_size)
                {
                    int block_size = Math.Min (total_size - processed, 0xffff);
                    if (ReadNextBit())
                    {
                        data_ptr = DecompressBlock (data_ptr, work_buffer, block_size + 2);
                        DecodeHuffman (work_buffer, block_size);
                    }
                    else
                        data_ptr = CopyRawBlock (data_ptr, block_size);
                    processed += block_size;
                }
            }

            ushort[] frequency_table = new ushort[0x100];
            ushort[] cumulative_freq = new ushort[0x100];
            ushort[] symbol_lookup = new ushort[0x10000];

            void DecodeHuffman (byte[] buffer, int size) // sub_444E40
            {
                // Build frequency table
                for (int i = 0; i < frequency_table.Length; ++i)
                    frequency_table[i] = 0;
                for (int i = 0; i < size; ++i)
                    ++frequency_table[buffer[2+i]];

                // Build cumulative frequency table
                ushort cumulative = 0;
                for (int i = 0; i < 0x100; ++i)
                {
                    cumulative_freq[i] = cumulative;
                    cumulative += frequency_table[i];
                    frequency_table[i] = 0;
                }

                // Build symbol lookup table
                for (int i = 0; i < size; ++i)
                {
                    int symbol = buffer[2+i];
                    int index = frequency_table[symbol] + cumulative_freq[symbol];
                    symbol_lookup[index] = (ushort)i;
                    frequency_table[symbol]++;
                }

                // Decode using lookup table
                int start_index = LittleEndian.ToUInt16 (buffer, 0);
                int current_index = symbol_lookup[start_index];
                for (int i = 0; i < size; ++i)
                {
                    m_output[m_output_pos++] = buffer[2+current_index];
                    current_index = symbol_lookup[current_index];
                }
            }

            int DecompressBlock (int input_ptr, byte[] output_buffer, int buffer_size) // sub_4450E0
            {
                byte[] lru_buffer1 = new byte[0x10];
                byte[] lru_buffer2 = new byte[0x10];

                // Initialize LRU buffers
                for (byte i = 0; i < 0x10; ++i)
                {
                    lru_buffer1[i] = i;
                    lru_buffer2[i] = i;
                }

                int output_pos = 0;
                sbyte previous_byte = -1;

                while (output_pos < buffer_size)
                {
                    int current_byte;
                    int lru_index;

                    if (!ReadNextBit())
                    {
                        if (ReadNextBit())
                        {
                            lru_index = ReadRunLength();
                            current_byte = lru_buffer2[lru_index];
                            output_buffer[output_pos++] = (byte)current_byte;
                        }
                        else
                        {
                            if (ReadNextBit())
                            {
                                int delta = ReadRunLength();
                                if (ReadNextBit())
                                    current_byte = (previous_byte - delta) & 0xff;
                                else
                                    current_byte = (previous_byte + delta) & 0xff;
                            }
                            else
                            {
                                current_byte = m_input[input_ptr++];
                            }
                            output_buffer[output_pos++] = (byte)current_byte;
                            lru_index = FindInLRU (lru_buffer2, current_byte);
                        }
                    }
                    else
                    {
                        int run_index;
                        int run_length = ReadRunLength();

                        if (ReadNextBit())
                        {
                            run_index = 0;
                            current_byte = lru_buffer1[0];
                        }
                        else if (ReadNextBit())
                        {
                            run_index = ReadRunLength();
                            current_byte = lru_buffer1[run_index];
                        }
                        else
                        {
                            if (ReadNextBit())
                            {
                                int delta = ReadRunLength();
                                if (ReadNextBit())
                                    current_byte = (previous_byte - delta) & 0xff;
                                else
                                    current_byte = (previous_byte + delta) & 0xff;
                            }
                            else
                            {
                                current_byte = m_input[input_ptr++];
                            }
                            run_index = FindInLRU (lru_buffer1, current_byte);
                        }

                        // Update LRU buffer 1
                        if (run_index != 0)
                        {
                            UpdateLRU (lru_buffer1, run_index, current_byte);
                        }

                        // Write run
                        for (int n = 0; n < run_length; ++n)
                            output_buffer[output_pos++] = (byte)current_byte;

                        lru_index = FindInLRU (lru_buffer2, current_byte);
                    }

                    // Update LRU buffer 2
                    if (0 != (byte)lru_index)
                    {
                        UpdateLRU (lru_buffer2, lru_index, current_byte);
                    }
                    previous_byte = (sbyte)current_byte;
                }
                return input_ptr;
            }

            int FindInLRU (byte[] lru_buffer, int value)
            {
                int index = 0;
                while (lru_buffer[index] != value)
                {
                    ++index;
                    if (index >= 0x10)
                        return 0xff;
                }
                return index;
            }

            void UpdateLRU (byte[] lru_buffer, int index, int value)
            {
                for (int i = index & 0xF; i != 0; --i)
                    lru_buffer[i] = lru_buffer[i-1];
                lru_buffer[0] = (byte)value;
            }

            int CopyRawBlock (int input_ptr, int block_size) // sub_445400
            {
                int bytes_copied = 0;
                while (bytes_copied < block_size)
                {
                    if (!ReadNextBit())
                    {
                        // Copy RGB triplet
                        m_output[m_output_pos++] = m_input[input_ptr++];
                        m_output[m_output_pos++] = m_input[input_ptr++];
                        m_output[m_output_pos++] = m_input[input_ptr++];
                        bytes_copied += 3;
                    }
                    else
                    {
                        // RLE encoded RGB triplet
                        int run_length = ReadRunLength();
                        byte b = m_input[input_ptr++];
                        byte g = m_input[input_ptr++];
                        byte r = m_input[input_ptr++];
                        for (int i = 0; i < run_length; ++i)
                        {
                            m_output[m_output_pos++] = b;
                            m_output[m_output_pos++] = g;
                            m_output[m_output_pos++] = r;
                        }
                        bytes_copied += 3 * run_length;
                    }
                }
                return input_ptr;
            }
        }
    }
}
