using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;

namespace GameRes.Compression
{
    public enum LzxMode
    {
        Decompress,
        Compress,
    }

    public class LzxSettings
    {
        public int WindowBits { get; set; }
        public bool IntelE8 { get; set; }
        public int CompressionLevel { get; set; }

        public LzxSettings()
        {
            WindowBits = 16;
            IntelE8 = true;
            CompressionLevel = 5;
        }
    }

    public sealed class LzxCoroutine : Decompressor
    {
        Stream m_input;
        LzxSettings m_settings;
        LzxDecompressor m_decompressor;

        public LzxSettings Settings { get { return m_settings; } }

        public override void Initialize(Stream input)
        {
            m_input = input;
            m_settings = new LzxSettings();
            m_decompressor = new LzxDecompressor(input, m_settings);
        }

        protected override IEnumerator<int> Unpack()
        {
            byte[] temp = new byte[4096];

            for (; ; )
            {
                int read = m_decompressor.ReadDecompressed(temp, 0, Math.Min(temp.Length, m_length));
                if (read == 0)
                    yield break;

                Array.Copy(temp, 0, m_buffer, m_pos, read);
                m_pos += read;
                m_length -= read;

                if (m_length == 0)
                    yield return m_pos;
            }
        }
    }

    public class LzxStream : Stream
    {
        private Stream m_base_stream;
        private LzxMode m_mode;
        private bool m_leave_open;
        private long m_position;
        private byte[] m_buffer;
        private int m_buffer_pos;
        private int m_buffer_size;
        private LzxSettings m_settings;

        private LzxDecompressor m_decompressor;

        public LzxSettings Config
        {
            get { return m_settings; }
            set { m_settings = value ?? new LzxSettings(); }
        }

        public LzxStream(Stream input, LzxMode mode = LzxMode.Decompress, bool leave_open = false)
        {
            m_base_stream = input;
            m_mode = mode;
            m_leave_open = leave_open;
            m_settings = new LzxSettings();
            m_buffer = new byte[65536];
            m_buffer_pos = 0;
            m_buffer_size = 0;
            m_position = 0;

            if (mode == LzxMode.Decompress)
            {
                m_decompressor = new LzxDecompressor(input, m_settings);
            }
        }

        public override bool CanRead => m_mode == LzxMode.Decompress;
        public override bool CanWrite => m_mode == LzxMode.Compress;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => m_position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (m_mode != LzxMode.Decompress)
                throw new NotSupportedException("Cannot read from compression stream");

            int totalRead = 0;

            while (count > 0)
            {
                if (m_buffer_pos >= m_buffer_size)
                {
                    m_buffer_size = m_decompressor.ReadDecompressed(m_buffer, 0, m_buffer.Length);
                    m_buffer_pos = 0;

                    if (m_buffer_size == 0)
                        break;
                }

                int available = m_buffer_size - m_buffer_pos;
                int toRead = Math.Min(available, count);

                Array.Copy(m_buffer, m_buffer_pos, buffer, offset, toRead);
                m_buffer_pos += toRead;
                offset += toRead;
                count -= toRead;
                totalRead += toRead;
                m_position += toRead;
            }

            return totalRead;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Cannot write to decompression stream");
        }

        public override void Flush()
        {
            m_base_stream.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_decompressor?.Dispose();
                if (!m_leave_open)
                    m_base_stream?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public class LzxDecompressor : IDisposable
    {
        private const int MIN_MATCH = 2;
        private const int MAX_MATCH = 257;
        private const int NUM_CHARS = 256;
        private const int BLOCKTYPE_VERBATIM = 1;
        private const int BLOCKTYPE_ALIGNED = 2;
        private const int BLOCKTYPE_UNCOMPRESSED = 3;
        private const int BLOCKTYPE_INVALID = 4;
        private const int PRETREE_NUM_ELEMENTS = 20;
        private const int SECONDARY_NUM_ELEMENTS = 249;
        private const int ALIGNED_NUM_ELEMENTS = 8;
        private const int NUM_PRIMARY_LENGTHS = 7;
        private const int E8_DISABLE_THRESHOLD = 32768;
        private const int MAX_GROWTH = 6144;

        private Stream m_input;
        private LzxSettings m_settings;
        private int m_window_size;
        private int m_window_mask;
        private byte[] m_local_window;
        private int m_window_position;
        private int m_output_position;
        private int m_main_elements;
        private int m_R0, m_R1, m_R2;

        private int m_block_type;
        private int m_block_remaining;
        private int m_block_length;
        private int m_blocks_remaining;
        private bool m_read_header;
        private int m_intel_filesize;
        private int m_intel_curpos;
        private bool m_intel_started;
        private int m_frames_read;

        internal int m_bits_left;
        private int m_block_align_offset;
        private byte[] m_input_bytes;
        private int m_index;
        private int m_length;
        private bool m_abort;

        private LzxHuffmanTable m_pretree;
        private LzxHuffmanTable m_main_tree;
        private LzxHuffmanTable m_length_tree;
        private LzxHuffmanTable m_aligned_tree;

        private readonly int[] m_position_base;
        private readonly int[] m_extra_bits;

        public LzxDecompressor(Stream input, LzxSettings settings = null)
        {
            m_input = input;
            m_settings = settings ?? new LzxSettings();
            m_window_size = 1 << settings.WindowBits;
            m_window_mask = m_window_size - 1;

            m_local_window = new byte[m_window_size + 261];

            m_window_position = 0;
            m_R0 = m_R1 = m_R2 = 1;
            m_read_header = true;
            m_frames_read = 0;
            m_block_type = BLOCKTYPE_INVALID;
            m_blocks_remaining = 0;
            m_intel_curpos = 0;
            m_intel_started = false;

            m_extra_bits = new int[51];
            m_position_base = new int[51];
            InitializePositionTables();

            MaybeReset();

            int main_size = NUM_CHARS + m_main_elements * ALIGNED_NUM_ELEMENTS;
            m_pretree = new LzxHuffmanTable(PRETREE_NUM_ELEMENTS, ALIGNED_NUM_ELEMENTS, this, null);
            m_main_tree = new LzxHuffmanTable(main_size, 9, this, m_pretree);
            m_length_tree = new LzxHuffmanTable(SECONDARY_NUM_ELEMENTS, 6, this, m_pretree);
            m_aligned_tree = new LzxHuffmanTable(ALIGNED_NUM_ELEMENTS, NUM_PRIMARY_LENGTHS, this, m_pretree);
        }

        private void InitializePositionTables()
        {
            int i = 4;
            int j = 1;
            do
            {
                m_extra_bits[i] = j;
                m_extra_bits[i + 1] = j;
                i += 2;
                j++;
            } while (j <= 16);

            do
            {
                m_extra_bits[i++] = 17;
            } while (i < 51);

            i = -2;
            for (j = 0; j < m_extra_bits.Length; j++)
            {
                m_position_base[j] = i;
                i += 1 << m_extra_bits[j];
            }
        }

        private void MaybeReset()
        {
            m_main_elements = 4;
            int i = 4;
            do
            {
                i += 1 << m_extra_bits[m_main_elements];
                m_main_elements++;
            } while (i < m_window_size);
        }

        // In LzxDecompressor class, modify SetCompressedData:
        public void SetCompressedData(byte[] data)
        {
            m_input_bytes = (byte[])data.Clone();
            m_length = m_input_bytes.Length;
            m_index = 0;
            m_abort = false;

            // ALWAYS initialize bitstream for new compressed data
            InitBitStream();
        }

        // Also modify ReadDecompressed to handle the case where we've consumed all input:
        public int ReadDecompressed(byte[] buffer, int offset, int count)
        {
            if (m_input_bytes == null || m_length == 0)
                return 0;

            // Reset abort flag when we have fresh data
            if (m_index == 0)
                m_abort = false;

            if (m_abort)
                return 0;

            int decompressed_length = DecompressLoop(count);

            // Copy from window to output buffer
            int srcPos = m_output_position;
            for (int i = 0; i < decompressed_length; i++)
            {
                buffer[offset + i] = m_local_window[srcPos];
                srcPos = (srcPos + 1) & m_window_mask;
            }

            // Apply Intel E8 translation if needed
            if (m_frames_read++ < E8_DISABLE_THRESHOLD && m_intel_filesize != 0)
                DecodeIntelBlock(buffer, offset, decompressed_length);

            return decompressed_length;
        }

        private int DecompressLoop(int bytesToRead)
        {
            int requested = bytesToRead;
            int last_window_position = 0;
            int k, m;

            if (m_read_header)
            {
                if (ReadBits(1) == 1)
                {
                    k = ReadBits(16);
                    m = ReadBits(16);
                    m_intel_filesize = (k << 16) | m;
                }
                else
                {
                    m_intel_filesize = 0;
                }
                m_read_header = false;
            }

            last_window_position = 0;

            while (bytesToRead > 0)
            {
                if (m_blocks_remaining == 0)
                {
                    if (m_block_type == BLOCKTYPE_UNCOMPRESSED)
                    {
                        if ((m_block_length & 1) != 0 && m_index < m_length)
                            m_index++;
                        m_block_type = BLOCKTYPE_INVALID;
                        InitBitStream();
                    }

                    m_block_type = ReadBits(3);
                    k = ReadBits(8);
                    m = ReadBits(8);
                    int n = ReadBits(8);

                    if (m_abort)
                        break;

                    m_block_remaining = m_block_length = (k << 16) + (m << 8) + n;

                    if (m_block_type == BLOCKTYPE_ALIGNED)
                    {
                        m_aligned_tree.ReadLengths();
                        m_aligned_tree.BuildTable();
                    }

                    if (m_block_type == BLOCKTYPE_ALIGNED || m_block_type == BLOCKTYPE_VERBATIM)
                    {
                        m_main_tree.Read();
                        m_length_tree.Read();

                        m_main_tree.ReadLengths(0, NUM_CHARS);
                        m_main_tree.ReadLengths(NUM_CHARS,
                            NUM_CHARS + m_main_elements * ALIGNED_NUM_ELEMENTS);
                        m_main_tree.BuildTable();

                        if (m_main_tree.GetLength(0xE8) != 0)
                            m_intel_started = true;

                        m_length_tree.ReadLengths(0, SECONDARY_NUM_ELEMENTS);
                        m_length_tree.BuildTable();
                    }
                    else if (m_block_type == BLOCKTYPE_UNCOMPRESSED)
                    {
                        m_intel_started = true;
                        m_index -= 2;

                        if (m_index < 0 || m_index + 12 >= m_length)
                            throw new InvalidDataException("Corrupt data");

                        m_R0 = ReadInt();
                        m_R1 = ReadInt();
                        m_R2 = ReadInt();
                    }
                    else
                    {
                        throw new InvalidDataException("Invalid block type");
                    }

                    // THIS LINE MUST BE HERE - INSIDE THE IF BLOCK
                    m_blocks_remaining = 1;
                }

                // NOT HERE! Remove it from here if it exists outside the if block

                while (m_block_remaining > 0 && bytesToRead > 0)
                {
                    int take = (m_block_remaining < bytesToRead)
                        ? m_block_remaining
                        : bytesToRead;

                    DecompressBlockActions(take);

                    m_block_remaining -= take;
                    bytesToRead -= take;
                    last_window_position += take;
                }

                if (m_block_remaining == 0)
                    m_blocks_remaining = 0;

                if (bytesToRead == 0 && m_block_align_offset != 16)
                    ReadNumberBits(m_block_align_offset);
            }

            if (m_window_position == 0)
                m_output_position = m_window_size - last_window_position;
            else
                m_output_position = m_window_position - last_window_position;

            m_output_position = (m_output_position + m_window_size) & m_window_mask;

            return last_window_position;
        }

        private void DecompressBlockActions(int bytes_to_read)
        {
            m_window_position &= m_window_mask;

            if (bytes_to_read > m_window_size)
                throw new InvalidDataException($"Window overflow {m_window_position + bytes_to_read} > {m_window_size}");

            switch (m_block_type)
            {
                case BLOCKTYPE_UNCOMPRESSED: UncompressedAlgo(bytes_to_read); break;
                case BLOCKTYPE_ALIGNED: AlignedAlgo(bytes_to_read); break;
                case BLOCKTYPE_VERBATIM: VerbatimAlgo(bytes_to_read); break;
                default: throw new InvalidDataException("Invalid block type");
            }
        }

        private void VerbatimAlgo(int this_run)
        {
            int i = m_window_position;
            int mask = m_window_mask;
            byte[] window = m_local_window;
            int r0 = m_R0;
            int r1 = m_R1;
            int r2 = m_R2;

            while (this_run > 0)
            {
                int main_element = m_main_tree.DecodeElement();

                if (main_element < NUM_CHARS)
                {
                    window[i] = (byte)main_element;
                    i = (i + 1) & mask;
                    this_run--;
                }
                else
                {
                    main_element -= NUM_CHARS;

                    int match_length = main_element & NUM_PRIMARY_LENGTHS;
                    if (match_length == NUM_PRIMARY_LENGTHS)
                        match_length += m_length_tree.DecodeElement();

                    int match_offset = main_element >> 3;

                    if (match_offset == 0)
                    {
                        match_offset = r0;
                    }
                    else if (match_offset == 1)
                    {
                        match_offset = r1;
                        r1 = r0;
                        r0 = match_offset;
                    }
                    else if (match_offset > 2)
                    {
                        if (match_offset > 3)
                            match_offset = VerbatimAlgo2(m_extra_bits[match_offset]) + m_position_base[match_offset];
                        else
                            match_offset = 1;

                        r2 = r1;
                        r1 = r0;
                        r0 = match_offset;
                    }
                    else
                    {
                        match_offset = r2;
                        r2 = r0;
                        r0 = match_offset;
                    }

                    match_length += MIN_MATCH;
                    this_run -= match_length;

                    while (match_length > 0)
                    {
                        window[i] = window[((i - match_offset) + m_window_size) & mask];
                        i = (i + 1) & mask;
                        match_length--;
                    }
                }
            }

            m_R0 = r0;
            m_R1 = r1;
            m_R2 = r2;
            m_window_position = i;
        }

        private void AlignedAlgo(int this_run)
        {
            int window_pos = m_window_position;
            int mask = m_window_mask;
            byte[] window = m_local_window;
            int r0 = m_R0;
            int r1 = m_R1;
            int r2 = m_R2;

            while (this_run > 0)
            {
                int main_element = m_main_tree.DecodeElement();

                if (main_element < NUM_CHARS)
                {
                    window[window_pos] = (byte)main_element;
                    window_pos = (window_pos + 1) & mask;
                    this_run--;
                }
                else
                {
                    main_element -= NUM_CHARS;

                    int match_length = main_element & NUM_PRIMARY_LENGTHS;
                    if (match_length == NUM_PRIMARY_LENGTHS)
                        match_length += m_length_tree.DecodeElement();

                    int match_offset = main_element >> 3;

                    if (match_offset > 2)
                    {
                        int extra = m_extra_bits[match_offset];
                        match_offset = m_position_base[match_offset];

                        if (extra > 3)
                            match_offset += (ReadBits(extra - 3) << 3) + m_aligned_tree.DecodeElement();
                        else if (extra == 3)
                            match_offset += m_aligned_tree.DecodeElement();
                        else if (extra > 0)
                            match_offset += ReadBits(extra);
                        else
                            match_offset = 1;

                        r2 = r1;
                        r1 = r0;
                        r0 = match_offset;
                    }
                    else if (match_offset == 0)
                    {
                        match_offset = r0;
                    }
                    else if (match_offset == 1)
                    {
                        match_offset = r1;
                        r1 = r0;
                        r0 = match_offset;
                    }
                    else
                    {
                        match_offset = r2;
                        r2 = r0;
                        r0 = match_offset;
                    }

                    match_length += MIN_MATCH;
                    this_run -= match_length;

                    while (match_length > 0)
                    {
                        window[window_pos] = window[((window_pos - match_offset) + m_window_size) & mask];
                        window_pos = (window_pos + 1) & mask;
                        match_length--;
                    }
                }
            }

            m_R0 = r0;
            m_R1 = r1;
            m_R2 = r2;
            m_window_position = window_pos;
        }

        private void UncompressedAlgo(int length)
        {
            if (m_index + length > m_length)
                throw new InvalidDataException("Input buffer overflow");

            m_intel_started = true;

            for (int n = 0; n < length; n++)
            {
                m_local_window[m_window_position] = m_input_bytes[m_index++];
                m_window_position = (m_window_position + 1) & m_window_mask;
            }
        }

        private int VerbatimAlgo2(int position)
        {
            int i = (int)((uint)m_bits_left >> (32 - position));

            m_bits_left <<= position;
            m_block_align_offset -= position;

            if (m_block_align_offset <= 0)
            {
                m_bits_left |= ReadShort() << (-m_block_align_offset);
                m_block_align_offset += 16;

                if (m_block_align_offset <= 0)
                {
                    m_bits_left |= ReadShort() << (-m_block_align_offset);
                    m_block_align_offset += 16;
                }
            }

            return i;
        }

        private void DecodeIntelBlock(byte[] bytes, int offset, int out_length)
        {
            if (out_length <= 10 || !m_intel_started)
            {
                m_intel_curpos += out_length;
                return;
            }

            int cursorPos = m_intel_curpos;
            int fileSize = m_intel_filesize;
            int adjustedOutLength = out_length - 10;

            int dataIndex = offset;
            int cursor_pos = cursorPos + adjustedOutLength;

            while (cursorPos < cursor_pos)
            {
                while (dataIndex < offset + out_length - 10 && bytes[dataIndex] == 0xE8)
                {
                    if (cursorPos >= cursor_pos)
                        break;

                    dataIndex++; // Skip E8 byte

                    int abs_off = (bytes[dataIndex] & 0xFF) |
                                 ((bytes[dataIndex + 1] & 0xFF) << 8) |
                                 ((bytes[dataIndex + 2] & 0xFF) << 16) |
                                 ((bytes[dataIndex + 3] & 0xFF) << 24);

                    if ((abs_off >= -cursorPos) && (abs_off < fileSize))
                    {
                        int rel_off = (abs_off >= 0) ? abs_off - cursorPos : abs_off + fileSize;
                        bytes[dataIndex] = (byte)(rel_off & 0xFF);
                        bytes[dataIndex + 1] = (byte)((rel_off >> 8) & 0xFF);
                        bytes[dataIndex + 2] = (byte)((rel_off >> 16) & 0xFF);
                        bytes[dataIndex + 3] = (byte)((rel_off >> 24) & 0xFF);
                    }

                    dataIndex += 4;
                    cursorPos += 5;
                }
                dataIndex++;
                cursorPos++;
            }

            m_intel_curpos += out_length;
        }

        private void InitBitStream()
        {
            if (m_block_type != BLOCKTYPE_UNCOMPRESSED)
            {
                m_bits_left = (ReadShort() << 16) | ReadShort();
                m_block_align_offset = 16;
            }
        }

        internal int ReadBits(int num_bits_to_read)
        {
            int i = (int)((uint)m_bits_left >> (32 - num_bits_to_read));
            ReadNumberBits(num_bits_to_read);
            return i;
        }

        internal void ReadNumberBits(int num_bits)
        {
            m_bits_left <<= num_bits;
            m_block_align_offset -= num_bits;

            if (m_block_align_offset <= 0)
            {
                m_bits_left |= ReadShort() << (-m_block_align_offset);
                m_block_align_offset += 16;
            }
        }

        private int ReadShort()
        {
            if (m_index < m_length)
            {
                int i = (m_input_bytes[m_index] & 0xFF) | ((m_input_bytes[m_index + 1] & 0xFF) << 8);
                m_index += 2;
                return i;
            }

            m_abort = true;
            m_index = 0;
            return 0;
        }

        private int ReadInt()
        {
            int i = (m_input_bytes[m_index] & 0xFF) |
                   ((m_input_bytes[m_index + 1] & 0xFF) << 8) |
                   ((m_input_bytes[m_index + 2] & 0xFF) << 16) |
                   ((m_input_bytes[m_index + 3] & 0xFF) << 24);
            m_index += 4;
            return i;
        }

        public void Dispose()
        {
            m_local_window = null;
            m_input_bytes = null;
        }

        private class LzxHuffmanTable
        {
            private int m_size;
            private int[] m_aa;
            internal int[] m_lens;

            private int[] m_a1;
            private int[] m_a2;
            private int[] m_table;

            private int m_b1;
            private int m_b2;
            private int m_b3;
            private int m_b4;

            private LzxDecompressor m_decompressor;
            private LzxHuffmanTable m_root;

            private int[] m_c1 = new int[17];
            private int[] m_c2 = new int[17];
            private int[] m_c3 = new int[18];

            private static readonly byte[] ARRAY = {
                0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
                0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16
            };

            public LzxHuffmanTable(int size, int param_int2, LzxDecompressor decompressor, LzxHuffmanTable root)
            {
                m_size = size;
                m_b1 = param_int2;
                m_decompressor = decompressor;
                m_root = root;
                m_b2 = 1 << m_b1;
                m_b3 = m_b2 - 1;
                m_b4 = 32 - m_b1;
                m_a1 = new int[m_size * 2];
                m_a2 = new int[m_size * 2];
                m_table = new int[m_b2];
                m_aa = new int[m_size];
                m_lens = new int[m_size];
            }

            public void Reset()
            {
                for (int i = 0; i < m_size; i++)
                {
                    m_lens[i] = 0;
                    m_aa[i] = 0;
                }
            }

            public int GetLength(int symbol)
            {
                return m_lens[symbol];
            }

            public void ReadLengths()
            {
                for (int i = 0; i < m_size; i++)
                {
                    m_lens[i] = m_decompressor.ReadBits(3);
                }
            }

            public void ReadLengths(int first, int last)
            {
                for (int i = 0; i < 20; i++)
                {
                    m_root.m_lens[i] = m_decompressor.ReadBits(4);
                }

                m_root.BuildTable();

                for (int i = first; i < last; i++)
                {
                    int k = m_root.DecodeElement();
                    int j;

                    if (k == 17)
                    {
                        j = m_decompressor.ReadBits(4) + 4;
                        if (i + j >= last)
                            j = last - i;
                        while (j-- > 0)
                            m_lens[i++] = 0;
                        i--;
                    }
                    else if (k == 18)
                    {
                        j = m_decompressor.ReadBits(5) + 20;
                        if (i + j >= last)
                            j = last - i;
                        while (j-- > 0)
                            m_lens[i++] = 0;
                        i--;
                    }
                    else if (k == 19)
                    {
                        j = m_decompressor.ReadBits(1) + 4;
                        if (i + j >= last)
                            j = last - i;

                        k = m_root.DecodeElement();
                        int m = ARRAY[m_aa[i] - k + 17];

                        while (j-- > 0)
                            m_lens[i++] = m;
                        i--;
                    }
                    else
                    {
                        m_lens[i] = ARRAY[m_aa[i] - k + 17];
                    }
                }
            }

            public void BuildTable()
            {
                int[] table = m_table;
                int[] c3 = m_c3;
                int b1 = m_b1;
                int i = 1;

                do
                {
                    m_c1[i] = 0;
                    i++;
                } while (i <= 16);

                for (i = 0; i < m_size; i++)
                {
                    m_c1[m_lens[i]]++;
                }

                c3[1] = 0;
                i = 1;
                do
                {
                    c3[i + 1] = c3[i] + (m_c1[i] << (16 - i));
                    i++;
                } while (i <= 16);

                if (c3[17] != 65536)
                {
                    if (c3[17] == 0)
                    {
                        for (i = 0; i < m_b2; i++)
                            table[i] = 0;
                        return;
                    }
                    throw new InvalidDataException("Invalid Huffman code lengths");
                }

                int i2 = 16 - b1;
                for (i = 1; i <= b1; i++)
                {
                    c3[i] >>= i2;
                    m_c2[i] = 1 << (b1 - i);
                }

                while (i <= 16)
                {
                    m_c2[i] = 1 << (16 - i);
                    i++;
                }

                i = c3[b1 + 1] >> i2;
                if (i != 65536)
                {
                    while (i < m_b2)
                    {
                        table[i] = 0;
                        i++;
                    }
                }

                int k = m_size;
                for (int j = 0; j < m_size; j++)
                {
                    int i1 = m_lens[j];
                    if (i1 != 0)
                    {
                        int m = c3[i1] + m_c2[i1];
                        if (i1 <= b1)
                        {
                            if (m > m_b2)
                                throw new InvalidDataException("Huffman table overflow");
                            for (i = c3[i1]; i < m; i++)
                                table[i] = j;
                            c3[i1] = m;
                        }
                        else
                        {
                            int n = c3[i1];
                            c3[i1] = m;
                            int i6 = n >> i2;
                            int i5 = 2;
                            i = i1 - b1;
                            n <<= b1;

                            do
                            {
                                int i4;
                                if (i5 == 2)
                                    i4 = table[i6];
                                else if (i5 == 0)
                                    i4 = m_a1[i6];
                                else
                                    i4 = m_a2[i6];

                                if (i4 == 0)
                                {
                                    m_a1[k] = 0;
                                    m_a2[k] = 0;
                                    if (i5 == 2)
                                        table[i6] = -k;
                                    else if (i5 == 0)
                                        m_a1[i6] = -k;
                                    else
                                        m_a2[i6] = -k;
                                    i4 = -k;
                                    k++;
                                }

                                i6 = -i4;
                                if ((n & 0x8000) == 0)
                                    i5 = 0;
                                else
                                    i5 = 1;
                                n <<= 1;
                                i--;
                            } while (i != 0);

                            if (i5 == 0)
                                m_a1[i6] = j;
                            else
                                m_a2[i6] = j;
                        }
                    }
                }
            }

            public void Read()
            {
                Array.Copy(m_lens, 0, m_aa, 0, m_size);
            }

            public int DecodeElement()
            {
                int i = m_table[(m_decompressor.m_bits_left >> m_b4) & m_b3];

                while (i < 0)
                {
                    int j = 1 << (m_b4 - 1);
                    do
                    {
                        i = -i;
                        if ((m_decompressor.m_bits_left & j) == 0)
                            i = m_a1[i];
                        else
                            i = m_a2[i];
                        j >>= 1;
                    } while (i < 0);
                }

                m_decompressor.ReadNumberBits(m_lens[i]);
                return i;
            }
        }
    }

    public class LzxReader : IDisposable
    {
        BinaryReader m_input;
        byte[] m_output;
        int m_input_size;
        int m_output_size;

        public BinaryReader Input { get { return m_input; } }
        public byte[] Data { get { return m_output; } }
        public int WindowBits { get; set; }
        public bool IntelE8 { get; set; }

        public LzxReader(Stream input, int input_length, int output_length)
        {
            m_input = new BinaryReader(input, System.Text.Encoding.ASCII, true);
            m_output = new byte[output_length];
            m_input_size = input_length;
            m_output_size = output_length;

            WindowBits = 16;
            IntelE8 = true;
        }

        public void Unpack()
        {
            var settings = new LzxSettings
            {
                WindowBits = this.WindowBits,
                IntelE8 = this.IntelE8
            };

            using (var decompressor = new LzxDecompressor(m_input.BaseStream, settings))
            {
                int total_read = 0;
                while (total_read < m_output_size)
                {
                    int read = decompressor.ReadDecompressed(m_output, total_read, m_output_size - total_read);
                    if (read == 0)
                        break;
                    total_read += read;
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_input?.Dispose();
            }
        }
    }
}