using System;
using System.IO;

namespace GameRes.Formats
{
    /// <summary>
    /// Base class for various filter streams that wrap an underlying stream and delegate operations to it.
    /// </summary>
    /// <remarks>
    /// This class serves as a foundation for creating stream decorators that can modify or extend
    /// the behavior of existing streams while maintaining the same Stream interface.
    /// </remarks>
    public class ProxyStream : Stream
    {
        Stream      m_stream;
        bool        m_should_dispose;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProxyStream"/> class.
        /// </summary>
        /// <param name="input">The underlying stream to wrap.</param>
        /// <param name="leave_open">If true, the underlying stream will not be disposed when this stream is disposed.</param>
        public ProxyStream (Stream input, bool leave_open = false)
        {
            m_stream = input;
            m_should_dispose = !leave_open;
        }

        /// <summary>
        /// Gets the underlying stream that this proxy wraps.
        /// </summary>
        public Stream BaseStream { get { return m_stream; } }

        /// <inheritdoc/>
        public override bool CanRead  { get { return m_stream.CanRead; } }

        /// <inheritdoc/>
        public override bool CanSeek  { get { return m_stream.CanSeek; } }

        /// <inheritdoc/>
        public override bool CanWrite { get { return m_stream.CanWrite; } }

        /// <inheritdoc/>
        public override long Length   { get { return m_stream.Length; } }

        /// <inheritdoc/>
        public override long Position
        {
            get { return m_stream.Position; }
            set { m_stream.Position = value; }
        }

        /// <summary>
        /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
        /// </summary>
        /// <param name="buffer">An array of bytes to store the read data.</param>
        /// <param name="offset">The zero-based byte offset in buffer at which to begin storing the data.</param>
        /// <param name="count">The maximum number of bytes to be read.</param>
        /// <returns>The total number of bytes read into the buffer.</returns>
        public override int Read (byte[] buffer, int offset, int count)
        {
            return m_stream.Read (buffer, offset, count);
        }

        /// <inheritdoc/>
        public override void Flush()
        {
            m_stream.Flush();
        }

        /// <inheritdoc/>
        public override long Seek (long offset, SeekOrigin origin)
        {
            return m_stream.Seek (offset, origin);
        }

        /// <summary>
        /// Sets the length of the current stream.
        /// </summary>
        /// <param name="length">The desired length of the current stream in bytes.</param>
        public override void SetLength (long length)
        {
            m_stream.SetLength (length);
        }

        /// <inheritdoc/>
        public override void Write (byte[] buffer, int offset, int count)
        {
            m_stream.Write (buffer, offset, count);
        }

        bool _proxy_disposed = false;
        
        /// <summary>
        /// Releases the unmanaged resources used by the Stream and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose (bool disposing)
        {
            if (!_proxy_disposed)
            {
                if (m_should_dispose && disposing)
                    m_stream.Dispose();
                _proxy_disposed = true;
                base.Dispose (disposing);
            }
        }
    }

    /// <summary>
    /// A read-only proxy stream that prevents write operations on the underlying stream.
    /// </summary>
    public class InputProxyStream : ProxyStream
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InputProxyStream"/> class.
        /// </summary>
        /// <param name="input">The underlying stream to wrap.</param>
        /// <param name="leave_open">If true, the underlying stream will not be disposed when this stream is disposed.</param>
        public InputProxyStream (Stream input, bool leave_open = false) : base (input, leave_open)
        {
        }

        /// <inheritdoc/>
        public override bool CanWrite { get { return false; } }

        /// <summary>
        /// Throws NotSupportedException as writing is not allowed on this stream.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown as this stream is read-only.</exception>
        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("Stream.Write method is not supported");
        }

        /// <summary>
        /// Throws NotSupportedException as setting length is not allowed on this stream.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown as this stream is read-only.</exception>
        public override void SetLength (long length)
        {
            throw new NotSupportedException ("Stream.SetLength method is not supported");
        }
    }

    /// <summary>
    /// A stream that prepends a byte array header to an existing stream, creating a virtual concatenation.
    /// </summary>
    /// <remarks>
    /// This stream is useful when you need to add a header to an existing stream without modifying the original data.
    /// The header bytes are read first, followed by the content of the underlying stream.
    /// </remarks>
    public class PrefixStream : InputProxyStream
    {
        byte[]  m_header;
        long    m_position = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrefixStream"/> class.
        /// </summary>
        /// <param name="header">The byte array to prepend to the stream.</param>
        /// <param name="main">The main stream to read after the header.</param>
        /// <param name="leave_open">If true, the underlying stream will not be disposed when this stream is disposed.</param>
        public PrefixStream (byte[] header, Stream main, bool leave_open = false)
            : base (main, leave_open)
        {
            m_header = header;
        }

        /// <summary>
        /// Gets the total length of the stream (header length + underlying stream length).
        /// </summary>
        public override long Length   { get { return BaseStream.Length + m_header.Length; } }

        /// <summary>
        /// Gets or sets the position within the current stream.
        /// </summary>
        /// <exception cref="NotSupportedException">Thrown when the underlying stream does not support seeking.</exception>
        public override long Position
        {
            get { return m_position; }
            set
            {
                if (!BaseStream.CanSeek)
                    throw new NotSupportedException ("Underlying stream does not support Stream.Position property");
                m_position = Math.Max (value, 0);
                if (m_position > m_header.Length)
                {
                    long stream_pos = BaseStream.Seek (m_position - m_header.Length, SeekOrigin.Begin);
                    m_position = m_header.Length + stream_pos;
                }
            }
        }

        /// <summary>
        /// Sets the position within the current stream.
        /// </summary>
        /// <param name="offset">A byte offset relative to the origin parameter.</param>
        /// <param name="origin">A value of type SeekOrigin indicating the reference point used to obtain the new position.</param>
        /// <returns>The new position within the current stream.</returns>
        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
                Position = offset;
            else if (SeekOrigin.Current == origin)
                Position = m_position + offset;
            else
                Position = Length + offset;

            return m_position;
        }

        /// <summary>
        /// Reads a sequence of bytes from the current stream, starting with the header bytes if not yet read.
        /// </summary>
        /// <param name="buffer">An array of bytes to store the read data.</param>
        /// <param name="offset">The zero-based byte offset in buffer at which to begin storing the data.</param>
        /// <param name="count">The maximum number of bytes to be read.</param>
        /// <returns>The total number of bytes read into the buffer.</returns>
        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = 0;
            if (m_position < m_header.Length)
            {
                int header_count = Math.Min (count, m_header.Length - (int)m_position);
                Buffer.BlockCopy (m_header, (int)m_position, buffer, offset, header_count);
                m_position += header_count;
                read += header_count;
                offset += header_count;
                count -= header_count;
            }
            if (count > 0)
            {
                if (m_header.Length == m_position && BaseStream.CanSeek)
                    BaseStream.Position = 0;
                int stream_read = BaseStream.Read (buffer, offset, count);
                m_position += stream_read;
                read += stream_read;
            }
            return read;
        }

        /// <summary>
        /// Reads a byte from the stream and advances the position within the stream by one byte.
        /// </summary>
        /// <returns>The unsigned byte cast to an Int32, or -1 if at the end of the stream.</returns>
        public override int ReadByte ()
        {
            if (m_position < m_header.Length)
                return m_header[m_position++];
            if (m_position == m_header.Length && BaseStream.CanSeek)
                BaseStream.Position = 0;
            int b = BaseStream.ReadByte();
            if (-1 != b)
                m_position++;
            return b;
        }
    }

    /// <summary>
    /// Represents a region within an existing stream, providing a view of a subset of the underlying stream's data.
    /// </summary>
    /// <remarks>
    /// This stream allows you to work with a portion of a larger stream as if it were a complete stream.
    /// The underlying stream must support seeking (CanSeek == true).
    /// </remarks>
    public class StreamRegion : InputProxyStream
    {
        private long    m_begin;
        private long    m_end;

        /// <summary>
        /// Initializes a new instance of the <see cref="StreamRegion"/> class with specified bounds.
        /// </summary>
        /// <param name="main">The underlying stream containing the region.</param>
        /// <param name="offset">The starting position of the region within the underlying stream.</param>
        /// <param name="length">The length of the region.</param>
        /// <param name="leave_open">If true, the underlying stream will not be disposed when this stream is disposed.</param>
        public StreamRegion (Stream main, long offset, long length, bool leave_open = false)
            : base (main, leave_open)
        {
            m_begin = offset;
            m_end = Math.Min (offset + length, BaseStream.Length);
            BaseStream.Position = m_begin;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StreamRegion"/> class from the specified offset to the end of the stream.
        /// </summary>
        /// <param name="main">The underlying stream containing the region.</param>
        /// <param name="offset">The starting position of the region within the underlying stream.</param>
        /// <param name="leave_open">If true, the underlying stream will not be disposed when this stream is disposed.</param>
        public StreamRegion (Stream main, long offset, bool leave_open = false)
            : this (main, offset, main.Length-offset, leave_open)
        {
        }

        /// <summary>
        /// Gets a value indicating whether the current stream supports seeking. Always returns true.
        /// </summary>
        public override bool CanSeek  { get { return true; } }

        /// <summary>
        /// Gets the length of the region in bytes.
        /// </summary>
        public override long Length   { get { return m_end - m_begin; } }

        /// <summary>
        /// Gets or sets the position within the region (0-based from the start of the region).
        /// </summary>
        public override long Position
        {
            get { return BaseStream.Position - m_begin; }
            set { BaseStream.Position = Math.Max (m_begin + value, m_begin); }
        }

        /// <summary>
        /// Sets the position within the region.
        /// </summary>
        /// <param name="offset">A byte offset relative to the origin parameter.</param>
        /// <param name="origin">A value of type SeekOrigin indicating the reference point used to obtain the new position.</param>
        /// <returns>The new position within the region.</returns>
        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
                offset += m_begin;
            else if (SeekOrigin.Current == origin)
                offset += BaseStream.Position;
            else
                offset += m_end;
            offset = Math.Max (offset, m_begin);
            BaseStream.Position = offset;
            return offset - m_begin;
        }

        /// <summary>
        /// Reads a sequence of bytes from the region, ensuring reads don't exceed the region boundaries.
        /// </summary>
        /// <param name="buffer">An array of bytes to store the read data.</param>
        /// <param name="offset">The zero-based byte offset in buffer at which to begin storing the data.</param>
        /// <param name="count">The maximum number of bytes to be read.</param>
        /// <returns>The total number of bytes read into the buffer.</returns>
        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = 0;
            long available = m_end - BaseStream.Position;
            if (available > 0)
            {
                read = BaseStream.Read (buffer, offset, (int)Math.Min (count, available));
            }
            return read;
        }

        /// <summary>
        /// Reads a byte from the stream and advances the position within the stream by one byte.
        /// </summary>
        /// <returns>The unsigned byte cast to an Int32, or -1 if at the end of the region.</returns>
        public override int ReadByte ()
        {
            if (BaseStream.Position < m_end)
                return BaseStream.ReadByte();
            else
                return -1;
        }
    }

    /// <summary>
    /// Specifies options for stream behavior.
    /// </summary>
    public enum StreamOption
    {
        /// <summary>
        /// No special options.
        /// </summary>
        None,
        /// <summary>
        /// Fill remaining bytes with zeros when the underlying stream is shorter than the limit.
        /// </summary>
        Fill,
    }

    /// <summary>
    /// Limits underlying stream to the first N bytes, with optional zero-padding if the stream is shorter.
    /// </summary>
    public class LimitStream : InputProxyStream
    {
        bool    m_can_seek;
        long    m_position;
        long    m_last;
        bool    m_fill;

        /// <summary>
        /// Initializes a new instance of the <see cref="LimitStream"/> class.
        /// </summary>
        /// <param name="input">The underlying stream to limit.</param>
        /// <param name="last">The maximum number of bytes that can be read from this stream.</param>
        /// <param name="leave_open">If true, the underlying stream will not be disposed when this stream is disposed.</param>
        public LimitStream (Stream input, long last, bool leave_open = false) : base (input, leave_open)
        {
            m_can_seek = input.CanSeek;
            m_position = 0;
            m_last = last;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LimitStream"/> class with additional options.
        /// </summary>
        /// <param name="input">The underlying stream to limit.</param>
        /// <param name="last">The maximum number of bytes that can be read from this stream.</param>
        /// <param name="option">Specifies stream behavior options.</param>
        /// <param name="leave_open">If true, the underlying stream will not be disposed when this stream is disposed.</param>
        public LimitStream (Stream input, long last, StreamOption option, bool leave_open = false)
            : this (input, last, leave_open)
        {
            if (StreamOption.Fill == option)
            {
                if (m_can_seek && input.Length < m_last)
                {
                    input.Position = m_position;
                    m_can_seek = false;
                }
                m_fill = true;
            }
        }

        /// <inheritdoc/>
        public override bool CanSeek  { get { return m_can_seek; } }
        
        /// <summary>
        /// Gets the length limit of the stream.
        /// </summary>
        public override long Length   { get { return m_last; } }

        /// <summary>
        /// Reads a sequence of bytes from the stream, limited by the maximum length.
        /// If Fill option is enabled, pads with zeros when the underlying stream ends.
        /// </summary>
        /// <inheritdoc/>
        public override int Read (byte[] buffer, int offset, int count)
        {
            if (m_can_seek)
                m_position = Position;
            if (m_position >= m_last)
                return 0;
            count = (int)Math.Min (count, m_last - m_position);
            int read = BaseStream.Read (buffer, offset, count);
            if (m_fill)
            {
                while (read < count)
                {
                    buffer[read++] = 0;
                }
            }
            m_position += read;
            return read;
        }

        /// <inheritdoc/>
        public override int ReadByte ()
        {
            if (m_can_seek)
                m_position = Position;
            if (m_position >= m_last)
                return -1;
            int b = BaseStream.ReadByte();
            if (-1 != b)
                ++m_position;
            return b;
        }
    }

    /// <summary>
    /// Lazily evaluated wrapper around non-seekable streams that provides seeking capability by buffering read data.
    /// </summary>
    /// <remarks>
    /// This class is useful when you need to perform seek operations on streams that don't naturally support seeking,
    /// such as network streams. It buffers all read data in memory to enable backward seeking.
    /// </remarks>
    public class SeekableStream : Stream
    {
        Stream      m_source;
        Stream      m_buffer;
        bool        m_should_dispose;
        bool        m_source_depleted;
        long        m_read_pos;

        /// <summary>
        /// Initializes a new instance of the <see cref="SeekableStream"/> class.
        /// </summary>
        /// <param name="input">The underlying stream to make seekable.</param>
        /// <param name="leave_open">If true, the underlying stream will not be disposed when this stream is disposed.</param>
        public SeekableStream (Stream input, bool leave_open = false)
        {
            m_source = input;
            m_should_dispose = !leave_open;
            m_read_pos = 0;
            if (m_source.CanSeek)
            {
                m_buffer = m_source;
                m_source_depleted = true;
            }
            else
            {
                m_buffer = new MemoryStream();
                m_source_depleted = false;
            }
        }

        #region IO.Stream Members
        /// <inheritdoc/>
        public override bool CanRead  { get { return m_buffer.CanRead; } }
        
        /// <summary>
        /// Gets a value indicating whether the current stream supports seeking. Always returns true.
        /// </summary>
        public override bool CanSeek  { get { return true; } }
        
        /// <summary>
        /// Gets a value indicating whether the current stream supports writing. Always returns false.
        /// </summary>
        public override bool CanWrite { get { return false; } }
        
        /// <summary>
        /// Gets the length of the stream. If the source hasn't been fully read, it will be read to completion.
        /// </summary>
        public override long Length
        {
            get
            {
                if (!m_source_depleted)
                {
                    m_buffer.Seek (0, SeekOrigin.End);
                    m_source.CopyTo (m_buffer);
                    m_source_depleted = true;
                }
                return m_buffer.Length;
            }
        }
        
        /// <inheritdoc/>
        public override long Position
        {
            get { return m_read_pos; }
            set { m_read_pos = value; }
        }

        /// <summary>
        /// Reads from the buffer if data is available, otherwise reads from source and buffers the data.
        /// </summary>
        /// <inheritdoc/>
        public override int Read (byte[] buffer, int offset, int count)
        {
            int read, total_read = 0;
            if (m_source_depleted)
            {
                m_buffer.Position = m_read_pos;
                total_read = m_buffer.Read (buffer, offset, count);
                m_read_pos += total_read;
                return total_read;
            }
            if (m_read_pos < m_buffer.Length)
            {
                int available = (int)Math.Min (m_buffer.Length-m_read_pos, count);
                m_buffer.Position = m_read_pos;
                total_read = m_buffer.Read (buffer, offset, available);
                m_read_pos += total_read;
                count -= total_read;
                if (0 == count)
                    return total_read;
                offset += total_read;
            }
            else if (count > 0)
            {
                m_buffer.Seek (0, SeekOrigin.End);
                while (m_read_pos > m_buffer.Length)
                {
                    int available = (int)Math.Min (m_read_pos - m_buffer.Length, count);
                    read = m_source.Read (buffer, offset, available);
                    if (0 == read)
                    {
                        m_source_depleted = true;
                        return 0;
                    }
                    m_buffer.Write (buffer, offset, read);
                }
            }
            read = m_source.Read (buffer, offset, count);
            m_read_pos += read;
            m_buffer.Write (buffer, offset, read);
            return total_read + read;
        }

        /// <inheritdoc/>
        public override void Flush()
        {
        }

        /// <inheritdoc/>
        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
                m_read_pos = offset;
            else if (SeekOrigin.Current == origin)
                m_read_pos += offset;
            else
                m_read_pos = Length + offset;

            return m_read_pos;
        }

        /// <summary>
        /// Throws NotSupportedException as this stream is read-only.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown as this stream is read-only.</exception>
        public override void SetLength (long length)
        {
            throw new NotSupportedException ("SeekableStream.SetLength method is not supported");
        }

        /// <summary>
        /// Throws NotSupportedException as this stream is read-only.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown as this stream is read-only.</exception>
        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("SeekableStream.Write method is not supported");
        }

        /// <summary>
        /// Throws NotSupportedException as this stream is read-only.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown as this stream is read-only.</exception>
        public override void WriteByte (byte value)
        {
            throw new NotSupportedException ("SeekableStream.WriteByte method is not supported");
        }
        #endregion

        #region IDisposable Members
        bool _disposed = false;
        /// <inheritdoc/>
        protected override void Dispose (bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (m_should_dispose)
                    m_source.Dispose();
                if (m_buffer != m_source)
                    m_buffer.Dispose();
            }
            _disposed = true;
            base.Dispose (disposing);
        }
        #endregion
    }

    /// <summary>
    /// Concatenates two input streams into a single virtual stream.
    /// </summary>
    /// <remarks>
    /// Reads from the first stream until it's exhausted, then continues reading from the second stream.
    /// Both streams must support seeking for the concatenated stream to support seeking.
    /// </remarks>
    public class ConcatStream : InputProxyStream
    {
        Stream      m_second;
        long        m_position;
        Stream      m_active;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConcatStream"/> class.
        /// </summary>
        /// <param name="first">The first stream to read from.</param>
        /// <param name="second">The second stream to read from after the first is exhausted.</param>
        public ConcatStream (Stream first, Stream second) : base (first)
        {
            m_second = second;
            m_position = 0;
            m_active = first;
        }

        /// <summary>
        /// Gets the first stream in the concatenation.
        /// </summary>
        internal Stream  First { get { return BaseStream; } }
        
        /// <summary>
        /// Gets the second stream in the concatenation.
        /// </summary>
        internal Stream Second { get { return m_second; } }

        /// <summary>
        /// Gets a value indicating whether the current stream supports seeking.
        /// Both underlying streams must support seeking.
        /// </summary>
        public override bool CanSeek  { get { return First.CanSeek && Second.CanSeek; } }
        
        /// <summary>
        /// Gets the total length of both streams combined.
        /// </summary>
        public override long Length   { get { return First.Length + Second.Length; } }
        
        /// <inheritdoc/>
        public override long Position
        {
            get { return m_position; }
            set { m_position = value; }
        }

        /// <summary>
        /// Reads from the appropriate stream based on the current position.
        /// </summary>
        /// <inheritdoc/>
        public override int Read (byte[] buffer, int offset, int count)
        {
            if (First.CanSeek)
            {
                if (m_position >= First.Length)
                {
                    m_active = Second;
                    m_active.Position = m_position - First.Length;
                }
                else
                {
                    m_active = First;
                    m_active.Position = m_position;
                }
            }
            int total_read = 0;
            while (count > 0)
            {
                int read = m_active.Read (buffer, offset, count);
                if (0 == read)
                    break;
                total_read += read;
                m_position += read;
                offset += read;
                count -= read;
            }
            if (count > 0 && m_active != Second)
            {
                m_active = Second;
                if (m_active.CanSeek)
                    m_active.Position = 0;
                int read = m_active.Read (buffer, offset, count);
                m_position += read;
                total_read += read;
            }
            return total_read;
        }

        /// <inheritdoc/>
        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
                Position = offset;
            else if (SeekOrigin.Current == origin)
                Position = m_position + offset;
            else
                Position = Length + offset;

            return m_position;
        }

        bool _disposed = false;
        /// <inheritdoc/>
        protected override void Dispose (bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                    m_second.Dispose();
                _disposed = true;
                base.Dispose (disposing);
            }
        }
    }
}