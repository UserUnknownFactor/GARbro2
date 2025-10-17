using System;
using System.Collections.Generic;
using System.IO;

namespace GameRes.Compression
{
    public enum LzssMode
    {
        Decompress,
        Compress,
    }

    public class LzssSettings
    {
        public int        FrameSize { get; set; }
        public byte       FrameFill { get; set; }
        public int     FrameInitPos { get; set; }
        public int   MinMatchLength { get; set; }
        public int   MaxMatchLength { get; set; }

        public LzssSettings ()
        {
            FrameSize = 0x1000;
            FrameFill = 0;
            FrameInitPos = 0xFEE;
            MinMatchLength = 3;
            MaxMatchLength = 18;
        }
    }

    public sealed class LzssCoroutine : Decompressor
    {
        Stream          m_input;
        LzssSettings    m_settings;

        public LzssSettings Settings { get { return m_settings; } }

        public override void Initialize (Stream input)
        {
            m_input = input;
            m_settings = new LzssSettings();
        }

        protected override IEnumerator<int> Unpack ()
        {
            byte[] frame = new byte[Settings.FrameSize];
            if (Settings.FrameFill != 0)
                for (int i = 0; i < frame.Length; ++i)
                    frame[i] = Settings.FrameFill;
            int frame_pos = Settings.FrameInitPos;
            int frame_mask = Settings.FrameSize - 1;

            for (;;)
            {
                int ctl = m_input.ReadByte();
                if (-1 == ctl)
                    yield break;

                for (int bit = 1; bit != 0x100; bit <<= 1)
                {
                    if (0 != (ctl & bit))
                    {
                        int b = m_input.ReadByte();
                        if (-1 == b)
                            yield break;
                        frame[frame_pos++ & frame_mask] = (byte)b;
                        m_buffer[m_pos++] = (byte)b;
                        if (0 == --m_length)
                            yield return m_pos;
                    }
                    else
                    {
                        int lo = m_input.ReadByte();
                        if (-1 == lo)
                            yield break;
                        int hi = m_input.ReadByte();
                        if (-1 == hi)
                            yield break;
                        int offset = (hi & 0xF0) << 4 | lo;
                        int count = (hi & 0x0F) + Settings.MinMatchLength;
                        for (; count != 0; --count)
                        {
                            byte v = frame[offset++ & frame_mask];
                            frame[frame_pos++ & frame_mask] = v;
                            m_buffer[m_pos++] = v;
                            if (0 == --m_length)
                                yield return m_pos;
                        }
                    }
                }
            }
        }
    }

    public class LzssStream : Stream
    {
        private Stream      m_base_stream;
        private LzssMode    m_mode;
        private bool        m_leave_open;
        private long        m_position;
        private byte[]      m_buffer;
        private int         m_buffer_pos;
        private int         m_buffer_size;
        private LzssSettings m_settings;

        // For decompression
        private LzssDecompressor m_decompressor;

        // For compression
        private LzssCompressor m_compressor;

        public LzssSettings Config 
        { 
            get { return m_settings; }
            set { m_settings = value ?? new LzssSettings(); }
        }

        public LzssStream (Stream input, LzssMode mode = LzssMode.Decompress, bool leave_open = false)
        {
            m_base_stream = input;
            m_mode = mode;
            m_leave_open = leave_open;
            m_settings = new LzssSettings();
            m_buffer = new byte[4096];
            m_buffer_pos = 0;
            m_buffer_size = 0;
            m_position = 0;

            if (mode == LzssMode.Decompress)
                m_decompressor = new LzssDecompressor (input, m_settings);
            else
                m_compressor = new LzssCompressor (input, m_settings);
        }

        public override bool  CanRead => m_mode == LzssMode.Decompress;
        public override bool CanWrite => m_mode == LzssMode.Compress;
        public override bool  CanSeek => false;
        public override long   Length => throw new NotSupportedException();
        public override long Position 
        { 
            get => m_position; 
            set => throw new NotSupportedException(); 
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            if (m_mode != LzssMode.Decompress)
                throw new NotSupportedException ("Cannot read from compression stream");

            int totalRead = 0;

            while (count > 0)
            {
                if (m_buffer_pos >= m_buffer_size)
                {
                    m_buffer_size = m_decompressor.ReadDecompressed (m_buffer, 0, m_buffer.Length);
                    m_buffer_pos = 0;

                    if (m_buffer_size == 0)
                        break;
                }

                int available = m_buffer_size - m_buffer_pos;
                int toRead = Math.Min (available, count);

                Array.Copy (m_buffer, m_buffer_pos, buffer, offset, toRead);
                m_buffer_pos += toRead;
                offset       += toRead;
                count        -= toRead;
                totalRead    += toRead;
                m_position   += toRead;
            }

            return totalRead;
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            if (m_mode != LzssMode.Compress)
                throw new NotSupportedException ("Cannot write to decompression stream");

            m_compressor.WriteCompressed (buffer, offset, count);
            m_position += count;
        }

        public override void Flush()
        {
            if (m_mode == LzssMode.Compress)
                m_compressor.Flush();
            m_base_stream.Flush();
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength (long value)
        {
            throw new NotSupportedException();
        }

        #region IDisposable Members
        protected override void Dispose (bool disposing)
        {
            if (disposing)
            {
                if (m_mode == LzssMode.Compress)
                {
                    m_compressor?.Flush();
                    m_compressor?.Dispose();
                }
                else
                    m_decompressor?.Dispose();

                if (!m_leave_open)
                    m_base_stream?.Dispose();
            }
            base.Dispose (disposing);
        }
        #endregion
    }

    public class LzssDecompressor : IDisposable
    {
        private Stream m_input;
        private LzssSettings m_settings;
        private byte[] m_frame;
        private int m_frame_pos;
        private int m_frame_mask;

        private byte[] m_input_buffer;
        private int m_input_pos;
        private int m_input_size;
        private const int INPUT_BUFFER_SIZE = 8192;

        // State for partial match processing
        private bool m_in_match;
        private int m_match_offset;
        private int m_match_remaining;
        private int m_control_byte;
        private int m_control_bit;

        public LzssDecompressor (Stream input, LzssSettings settings)
        {
            m_input = input;
            m_settings = settings;
            m_frame = new byte[settings.FrameSize];
            m_frame_mask = settings.FrameSize - 1;
            m_frame_pos = settings.FrameInitPos;
            m_control_byte = 0;
            m_control_bit = 0x100;

            m_input_buffer = new byte[INPUT_BUFFER_SIZE];
            m_input_pos = 0;
            m_input_size = 0;

            m_in_match = false;
            m_match_offset = 0;
            m_match_remaining = 0;

            if (settings.FrameFill != 0)
            {
                for (int i = 0; i < m_frame.Length; i++)
                    m_frame[i] = settings.FrameFill;
            }
        }

        private int ReadByte ()
        {
            if (m_input_pos >= m_input_size)
            {
                m_input_size = m_input.Read (m_input_buffer, 0, INPUT_BUFFER_SIZE);
                m_input_pos = 0;
                if (m_input_size == 0)
                    return -1;
            }
            return m_input_buffer[m_input_pos++];
        }

        public int ReadDecompressed (byte[] buffer, int offset, int count)
        {
            int written = 0;

            while (written < count)
            {
                if (m_in_match)
                {
                    // Fast path for continuing a match
                    int copy_count = Math.Min (m_match_remaining, count - written);
                    for (int i = 0; i < copy_count; i++)
                    {
                        byte b = m_frame[m_match_offset++ & m_frame_mask];
                        m_frame[m_frame_pos++ & m_frame_mask] = b;
                        buffer[offset + written++] = b;
                    }
                    m_match_remaining -= copy_count;

                    if (m_match_remaining == 0)
                        m_in_match = false;

                    if (written >= count)
                        break;
                }

                // Get new control byte if needed
                if (m_control_bit >= 0x100)
                {
                    int b = ReadByte();
                    if (b == -1)
                        break;

                    m_control_byte = b;
                    m_control_bit = 1;
                }

                if ((m_control_byte & m_control_bit) != 0)
                {
                    // Literal byte
                    int b = ReadByte();
                    if (b == -1)
                        break;

                    m_frame[m_frame_pos++ & m_frame_mask] = (byte)b;
                    buffer[offset + written++] = (byte)b;
                }
                else
                {
                    // Match
                    int lo = ReadByte();
                    if (lo == -1)
                        break;
                    int hi = ReadByte();
                    if (hi == -1)
                        break;

                    m_match_offset = ((hi & 0xF0) << 4) | lo;
                    int match_length = (hi & 0x0F) + m_settings.MinMatchLength;

                    // Process as much of the match as we can
                    int copy_count = Math.Min (match_length, count - written);
                    for (int i = 0; i < copy_count; i++)
                    {
                        byte b = m_frame[m_match_offset++ & m_frame_mask];
                        m_frame[m_frame_pos++ & m_frame_mask] = b;
                        buffer[offset + written++] = b;
                    }
                    match_length -= copy_count;

                    // Save state if match is incomplete
                    if (match_length > 0)
                    {
                        m_in_match = true;
                        m_match_remaining = match_length;
                    }
                }

                m_control_bit <<= 1;
            }

            return written;
        }

        #region IDisposable Members
        public void Dispose ()
        {
            // Nothing to dispose as we don't own the stream
        }
        #endregion
    }

    public class LzssReader : IDisposable
    {
        BinaryReader    m_input;
        byte[]          m_output;
        int             m_size;

        public BinaryReader Input { get { return m_input; } }
        public byte[]        Data { get { return m_output; } }
        public int      FrameSize { get; set; }
        public byte     FrameFill { get; set; }
        public int   FrameInitPos { get; set; }
        public int MinMatchLength { get; set; }

        public LzssReader (Stream input, int input_length, int output_length)
        {
            m_input = new BinaryReader (input, System.Text.Encoding.ASCII, true);
            m_output = new byte[output_length];
            m_size = input_length;

            FrameSize      = 0x1000;
            FrameFill      = 0;
            FrameInitPos   = 0xFEE;
            MinMatchLength = 3;
        }

        public void Unpack ()
        {
            int dst = 0;
            var frame = new byte[FrameSize];
            if (FrameFill != 0)
                for (int i = 0; i < frame.Length; ++i)
                    frame[i] = FrameFill;
            int frame_pos = FrameInitPos;
            int frame_mask = FrameSize - 1;
            int remaining = (int)m_size;

            while (remaining > 0 && dst < m_output.Length)
            {
                int ctl = m_input.ReadByte();
                --remaining;

                for (int bit = 1; remaining > 0 && bit != 0x100; bit <<= 1)
                {
                    if (dst >= m_output.Length)
                        return;

                    if (0 != (ctl & bit))
                    {
                        byte b = m_input.ReadByte();
                        --remaining;
                        frame[frame_pos++] = b;
                        frame_pos &= frame_mask;
                        m_output[dst++] = b;
                    }
                    else
                    {
                        if (remaining < 2)
                            return;
                        int lo = m_input.ReadByte();
                        int hi = m_input.ReadByte();
                        remaining -= 2;
                        int offset = (hi & 0xf0) << 4 | lo;

                        for (int count = MinMatchLength + (hi & 0xF); count != 0; --count)
                        {
                            if (dst >= m_output.Length)
                                break;
                            byte v = frame[offset++];
                            offset &= frame_mask;
                            frame[frame_pos++] = v;
                            frame_pos &= frame_mask;
                            m_output[dst++] = v;
                        }
                    }
                }
            }
        }

        #region IDisposable Members
        bool disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    m_input.Dispose();
                }
                disposed = true;
            }
        }
        #endregion
    }

    public class LzssCompressor : IDisposable
    {
        private Stream m_output;
        private LzssSettings m_settings;
        private byte[] m_frame;
        private int m_frame_mask;
        private int m_frame_pos;
        private List<byte> m_output_buffer;

        private byte m_control_byte;
        private int m_control_pos;
        public int m_control_bit;

        private int m_last_match_pos = -1;

        public LzssCompressor (Stream output, LzssSettings settings)
        {
            m_output = output;
            m_settings = settings;
            m_frame = new byte[settings.FrameSize];
            m_frame_mask = settings.FrameSize - 1;
            m_frame_pos = settings.FrameInitPos;
            m_output_buffer = new List<byte>();
            m_control_byte = 0;
            m_control_bit = 1;
            m_control_pos = 0;

            // Initialize frame buffer with fill character
            if (settings.FrameFill != 0)
            {
                for (int i = 0; i < m_frame.Length; i++)
                    m_frame[i] = settings.FrameFill;
            }

            // Reserve space for first control byte
            m_output_buffer.Add (0);
        }

        public void WriteCompressed (byte[] buffer, int offset, int count)
        {
            int pos = offset;
            int end = offset + count;

            while (pos < end)
            {
                int remaining = end - pos;
                int maxLength = Math.Min (m_settings.MaxMatchLength, remaining);

                // Find the best match in the frame buffer
                var match = FindBestMatch (buffer, pos, maxLength);

                if (match.Length >= m_settings.MinMatchLength)
                {
                    // Write match (control bit = 0, so don't set the bit)
                    WriteMatch (match.Offset, match.Length);
                    m_last_match_pos = match.Offset;  // Remember for next search

                    // Update frame buffer with matched bytes
                    for (int i = 0; i < match.Length; i++)
                    {
                        m_frame[m_frame_pos] = buffer[pos + i];
                        m_frame_pos = (m_frame_pos + 1) & m_frame_mask;
                    }
                    pos += match.Length;
                }
                else
                {
                    // Write literal (control bit = 1)
                    WriteLiteral (buffer[pos]);
                    m_last_match_pos = -1;

                    // Update frame buffer with literal byte
                    m_frame[m_frame_pos] = buffer[pos];
                    m_frame_pos = (m_frame_pos + 1) & m_frame_mask;
                    pos++;
                }
            }
        }

        private (int Offset, int Length) FindBestMatch (byte[] buffer, int pos, int maxLength)
        {
            int bestOffset = 0;
            int bestLength = 0;

            // Search the entire frame buffer for the best match
            // We search all positions in the circular buffer
            byte firstByte = buffer[pos];
            byte secondByte = (maxLength > 1 && pos + 1 < buffer.Length) ? buffer[pos + 1] : (byte)0;
            bool checkSecond = (maxLength > 1);

            // First, check near the last match position (often matches are clustered)
            if (m_last_match_pos >= 0)
            {
                for (int delta = -16; delta <= 16; delta++)
                {
                    int searchPos = (m_last_match_pos + delta) & m_frame_mask;
                    // Don't search at the current write position
                    if (searchPos == m_frame_pos)
                        continue;

                    if (m_frame[searchPos] != firstByte)
                        continue;
                    if (checkSecond && m_frame[(searchPos + 1) & m_frame_mask] != secondByte)
                        continue;

                    int matchLength = GetMatchLength (buffer, pos, searchPos, maxLength);
                    if (matchLength >= m_settings.MinMatchLength && matchLength > bestLength)
                    {
                        bestLength = matchLength;
                        bestOffset = searchPos;
                        if (matchLength == maxLength)
                            return (bestOffset, bestLength);
                    }
                }
            }

            // Then do full search with early exit optimization
            // Search backwards from current position (most recent data first)
            for (int dist = 1; dist < m_settings.FrameSize; dist++)
            {
                int searchPos = (m_frame_pos - dist) & m_frame_mask;

                if (m_frame[searchPos] != firstByte)
                    continue;
                if (checkSecond && m_frame[(searchPos + 1) & m_frame_mask] != secondByte)  
                    continue;

                int matchLength = GetMatchLength (buffer, pos, searchPos, maxLength);
                if (matchLength >= m_settings.MinMatchLength && matchLength > bestLength)
                {
                    bestLength = matchLength;
                    bestOffset = searchPos;
                    if (matchLength == maxLength)
                        break;
                }
            }

            return (bestOffset, bestLength);
        }

        private int GetMatchLength (byte[] buffer, int bufferPos, int framePos, int maxLength)
        {
            int matchLength = 0;
            // Compare bytes starting from searchPos in frame with bytes at pos in buffer
            while (matchLength < maxLength)
            {
                int frameIndex = (framePos + matchLength) & m_frame_mask;
                // Stop if we've wrapped around to the current write position
                if (frameIndex == m_frame_pos)
                    break;
                if (m_frame[frameIndex] != buffer[bufferPos + matchLength])
                    break;
                matchLength++;
            }
            return matchLength;
        }

        private void WriteLiteral (byte value)
        {
            // Set control bit to 1 for literal
            m_control_byte |= (byte)m_control_bit;
            m_output_buffer.Add (value);
            AdvanceControlBit();
        }

        private void WriteMatch (int offset, int length)
        {
            // Control bit is 0 for match (don't set the bit)
            // Match encoding per LZSS standard:
            // - byte 0: low 8 bits of offset
            // - byte 1: high 4 bits of offset (bits 7-4) | (length - MinMatchLength) (bits 3-0)
            int adjustedLength = length - m_settings.MinMatchLength;
            // Ensure offset is within valid range (12-bit value, 0-4095)
            offset = offset & m_frame_mask;

            // Pack the offset and length into two bytes
            byte low = (byte)(offset & 0xFF);
            byte high = (byte)(((offset >> 4) & 0xF0) | (adjustedLength & 0x0F));

            m_output_buffer.Add (low);
            m_output_buffer.Add (high);

            // Don't set control bit (it remains 0 for match)
            AdvanceControlBit();
        }

        private void AdvanceControlBit ()
        {
            m_control_bit <<= 1;

            if (m_control_bit >= 0x100)
            {
                // Write the control byte and buffered data
                m_output_buffer[m_control_pos] = m_control_byte;
                m_output.Write (m_output_buffer.ToArray(), 0, m_output_buffer.Count);

                // Reset for next block
                m_output_buffer.Clear();
                m_control_byte = 0;
                m_control_bit = 1;
                m_control_pos = m_output_buffer.Count;
                m_output_buffer.Add (0); // Reserve space for next control byte
            }
        }

        public void Flush ()
        {
            if (m_control_bit > 1)
            {
                // Write final control byte and any remaining data
                m_output_buffer[m_control_pos] = m_control_byte;
                m_output.Write (m_output_buffer.ToArray(), 0, m_output_buffer.Count);
                m_output_buffer.Clear();

                m_control_byte = 0;
                m_control_bit = 1;
            }

            m_output.Flush();
        }

        #region IDisposable Members
        public void Dispose ()
        {
            Flush();
        }
        #endregion
    }
}
