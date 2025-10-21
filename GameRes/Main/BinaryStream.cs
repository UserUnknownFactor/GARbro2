using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GameRes.Utility;

namespace GameRes
{
    public interface IBinaryStream : IDisposable
    {
        /// <summary>
        /// Name of the stream (could be name of the underlying file) or an empty string.
        /// </summary>
        string     Name { get; set; }
        /// <summary>
        /// First 4 bytes of the stream as a little-endian integer.
        /// </summary>
        uint  Signature { get; }
        Stream AsStream { get; }
        bool    CanSeek { get; }
        long     Length { get; }
        long   Position { get; set; }

        /// <summary>
        /// Default encoding for string operations when encoding is not specified.
        /// </summary>
        Encoding DefaultEncoding { get; set; }

        long    Seek (long offset, SeekOrigin origin);

        int     Read (byte[] buffer, int offset, int count);
        int     ReadByte ();

        /// <summary>
        /// Read next byte from stream without advancing stream position.
        /// </summary>
        /// <returns>Next byte, or -1 if end of stream is reached.</returns>
        int     PeekByte ();

        // Little-endian methods
        sbyte   ReadInt8 ();
        byte    ReadUInt8 ();
        short   ReadInt16 ();
        ushort  ReadUInt16 ();
        int     ReadInt24 ();
        int     ReadInt32 ();
        uint    ReadUInt32 ();
        long    ReadInt64 ();
        ulong   ReadUInt64 ();

        // Big-endian methods
        short   ReadInt16BE ();
        ushort  ReadUInt16BE ();
        int     ReadInt24BE ();
        int     ReadInt32BE ();
        uint    ReadUInt32BE ();
        long    ReadInt64BE ();
        ulong   ReadUInt64BE ();

        /// <summary>
        /// Read first <paramref="size"/> bytes of stream and return them in a copy-on-write array.
        /// </summary>
        CowArray<byte> ReadHeader (int size);

        /// <summary>
        /// Read zero-terminated string at most of <paramref name="length"/> bytes length from stream in
        /// specified encoding.
        /// Advances stream position forward by <paramref name="length"/> bytes, regardless of where zero byte
        /// was encountered.
        /// </summary>
        string ReadCString (int length, Encoding enc);
        /// <summary>
        /// Does ReadCString with default encoding.
        /// </summary>
        string ReadCString (int length);

        /// <summary>
        /// Read zero-terminated string from stream in specified encoding.
        /// Stream is positioned after a zero byte that terminates a string, or at end of stream, whichever
        /// comes first.
        /// </summary>
        string ReadCString (Encoding enc);
        /// <summary>
        /// Does ReadCString with default encoding.
        /// </summary>
        string ReadCString ();

        /// <summary>
        /// Read <paramref name="count"/> bytes starting from the current position into array and return it.
        /// If there's less than <paramref name="count"/> bytes left in the stream, array size will be
        /// smaller than count.
        /// </summary>
        byte[] ReadBytes (int count);
    }

    public class BinaryStream : Stream, IBinaryStream
    {
        Stream      m_source;
        bool        m_should_dispose;
        byte[]      m_buffer;
        int         m_buffer_pos;
        int         m_buffer_end;
        Lazy<uint>  m_signature;
        byte[]      m_header;
        int         m_header_size;
        Encoding    m_default_encoding;

        public string     Name { get; set; }
        public uint  Signature { get { return m_signature.Value; } }
        public Stream AsStream { get { return this; } }
        public Encoding DefaultEncoding 
        { 
            get { return m_default_encoding ?? Encodings.cp932; }
            set { m_default_encoding = value; }
        }

        public BinaryStream (Stream input, string name, bool leave_open = false)
        {
            m_buffer = new byte[0x10];
            m_buffer_pos = 0;
            m_buffer_end = 0;
            m_header_size = 0;
            Name = name ?? "";
            m_source = input;
            m_should_dispose = !leave_open;
            if (!m_source.CanSeek)
            {
                m_buffer_end = m_source.Read (m_buffer, 0, 4);
                uint signature = LittleEndian.ToUInt32 (m_buffer, 0);
                m_signature = new Lazy<uint> (() => signature);
            }
            else
            {
                m_signature = new Lazy<uint> (ReadSignature);
            }
        }

        public static IBinaryStream FromFile (string filename)
        {
            var stream = File.OpenRead (filename);
            return new BinaryStream (stream, filename);
        }

        public static IBinaryStream FromArray (byte[] data, string filename)
        {
            return new BinMemoryStream (data, filename);
        }

        public static IBinaryStream FromStream (Stream input, string filename)
        {
            var bin = input as IBinaryStream;
            if (null == bin)
            {
                var mem_stream = input as MemoryStream;
                if (mem_stream != null)
                {
                    bin = new BinMemoryStream (mem_stream, filename);
                    mem_stream.Dispose();
                }
                else
                    bin = new BinaryStream (input, filename);
            }
            else
                bin.Name = filename;
            return bin;
        }

        internal static int FindTerminator (byte[] buffer, int start, int length, Encoding encoding)
        {
            if (start < 0 || length < 0 || start + length > buffer.Length)
                return -1;

            if (encoding.IsSingleByte)
            {
                return Array.IndexOf<byte>(buffer, 0, start, length);
            }
            else if (encoding.Equals (Encoding.Unicode) || encoding.Equals (Encoding.BigEndianUnicode))
            {
                for (int i = start; i <= start + length - 2; i += 2)
                {
                    if (buffer[i] == 0 && buffer[i + 1] == 0)
                        return i;
                }
            }
            else if (encoding.Equals (Encoding.UTF32))
            {
                for (int i = start; i <= start + length - 4; i += 4)
                {
                    if (buffer[i] == 0 && buffer[i + 1] == 0 && 
                        buffer[i + 2] == 0 && buffer[i + 3] == 0)
                        return i;
                }
            }
            else
            {
                return Array.IndexOf<byte>(buffer, 0, start, length);
            }
            return -1;
        }

        uint ReadSignature ()
        {
            if (m_header_size >= 4)
            {
                return LittleEndian.ToUInt32 (m_header, 0);
            }
            var pos = Position;
            if (pos != 0)
                Position = 0;
            uint signature = ReadUInt32();
            Position = pos;
            return signature;
        }

        public CowArray<byte> ReadHeader (int size)
        {
            if (!CanSeek)
                throw new NotSupportedException ("Unseekable stream");
            if (m_header_size < size)
            {
                if (null == m_header || m_header.Length < size)
                    Array.Resize (ref m_header, (size + 0xF) & ~0xF);
                Position = m_header_size;
                m_header_size += Read (m_header, m_header_size, size - m_header_size);
            }
            if (size > m_header_size)
            {
                Position = m_header_size;
                throw new EndOfStreamException();
            }
            Position = size;
            return new CowArray<byte> (m_header, 0, size);
        }

        private int FillBuffer (int count)
        {
            int cached = m_buffer_end - m_buffer_pos;
            if (count <= cached)
                return count;
            if (m_buffer_pos + count > m_buffer.Length)
            {
                if (count > m_buffer.Length)
                {
                    var copy = new byte[(count + 0xF) & ~0xF];
                    if (cached > 0)
                        Buffer.BlockCopy (m_buffer, m_buffer_pos, copy, 0, cached);
                    m_buffer = copy;
                }
                else if (cached > 0)
                {
                    Buffer.BlockCopy (m_buffer, m_buffer_pos, m_buffer, 0, cached);
                }
                m_buffer_pos = 0;
                m_buffer_end = cached;
            }
            m_buffer_end += m_source.Read (m_buffer, m_buffer_end, count - cached);
            return Math.Min (count, m_buffer_end - m_buffer_pos);
        }

        public int PeekByte ()
        {
            if (1 != FillBuffer (1))
                return -1;
            return m_buffer[m_buffer_pos];
        }

        public sbyte ReadInt8 ()
        {
            return (sbyte)ReadUInt8();
        }

        public byte ReadUInt8 ()
        {
            if (1 != FillBuffer (1))
                throw new EndOfStreamException();
            return m_buffer[m_buffer_pos++];
        }

        public short ReadInt16 ()
        {
            if (2 != FillBuffer (2))
                throw new EndOfStreamException();
            short v = LittleEndian.ToInt16 (m_buffer, m_buffer_pos);
            m_buffer_pos += 2;
            return v;
        }

        public ushort ReadUInt16 ()
        {
            return (ushort)ReadInt16();
        }

        public int ReadInt24 ()
        {
            if (3 != FillBuffer (3))
                throw new EndOfStreamException();
            int v = m_buffer.ToInt24 (m_buffer_pos);
            m_buffer_pos += 3;
            return v;
        }

        public int ReadInt32 ()
        {
            if (4 != FillBuffer (4))
                throw new EndOfStreamException();
            int v = LittleEndian.ToInt32 (m_buffer, m_buffer_pos);
            m_buffer_pos += 4;
            return v;
        }

        public uint ReadUInt32 ()
        {
            return (uint)ReadInt32();
        }

        public long ReadInt64 ()
        {
            if (8 != FillBuffer (8))
                throw new EndOfStreamException();
            long v = LittleEndian.ToInt64 (m_buffer, m_buffer_pos);
            m_buffer_pos += 8;
            return v;
        }

        public ulong ReadUInt64 ()
        {
            return (ulong)ReadInt64();
        }

        // Big-endian methods
        public short ReadInt16BE ()
        {
            if (2 != FillBuffer (2))
                throw new EndOfStreamException();
            short v = (short)((m_buffer[m_buffer_pos] << 8) | m_buffer[m_buffer_pos + 1]);
            m_buffer_pos += 2;
            return v;
        }

        public ushort ReadUInt16BE ()
        {
            return (ushort)ReadInt16BE();
        }

        public int ReadInt24BE ()
        {
            if (3 != FillBuffer (3))
                throw new EndOfStreamException();
            int v = (m_buffer[m_buffer_pos] << 16) | 
                    (m_buffer[m_buffer_pos + 1] << 8) | 
                    m_buffer[m_buffer_pos + 2];
            m_buffer_pos += 3;
            return v;
        }

        public int ReadInt32BE ()
        {
            if (4 != FillBuffer (4))
                throw new EndOfStreamException();
            int v = (m_buffer[m_buffer_pos] << 24) | 
                    (m_buffer[m_buffer_pos + 1] << 16) | 
                    (m_buffer[m_buffer_pos + 2] << 8) | 
                    m_buffer[m_buffer_pos + 3];
            m_buffer_pos += 4;
            return v;
        }

        public uint ReadUInt32BE ()
        {
            return (uint)ReadInt32BE();
        }

        public long ReadInt64BE ()
        {
            if (8 != FillBuffer (8))
                throw new EndOfStreamException();
            long v = ((long)m_buffer[m_buffer_pos] << 56) | 
                     ((long)m_buffer[m_buffer_pos + 1] << 48) | 
                     ((long)m_buffer[m_buffer_pos + 2] << 40) | 
                     ((long)m_buffer[m_buffer_pos + 3] << 32) |
                     ((long)m_buffer[m_buffer_pos + 4] << 24) | 
                     ((long)m_buffer[m_buffer_pos + 5] << 16) | 
                     ((long)m_buffer[m_buffer_pos + 6] << 8) | 
                     m_buffer[m_buffer_pos + 7];
            m_buffer_pos += 8;
            return v;
        }

        public ulong ReadUInt64BE ()
        {
            return (ulong)ReadInt64BE();
        }

        public string ReadCString (int length)
        {
            return ReadCString (length, DefaultEncoding);
        }

        public string ReadCString (int length, Encoding enc)
        {
            length = FillBuffer (length);
            int terminator_pos = FindTerminator (m_buffer, m_buffer_pos, length, enc);
            int string_length = terminator_pos >= 0 ? terminator_pos - m_buffer_pos : length;
            string s = enc.GetString (m_buffer, m_buffer_pos, string_length);
            m_buffer_pos += length;
            return s;
        }

        public string ReadCString ()
        {
            return ReadCString (DefaultEncoding);
        }

        public string ReadCString (Encoding enc)
        {
            int count = 0;
            int term_size = enc.IsSingleByte ? 1 : 
                           (enc.Equals (Encoding.Unicode) || enc.Equals (Encoding.BigEndianUnicode)) ? 2 : 
                            enc.Equals (Encoding.UTF32) ? 4 : 1;

            int cached;
            bool found = false;

            for (;;)
            {
                cached = FillBuffer (count + term_size);
                if (cached < count + term_size)
                    break;

                int term_pos = FindTerminator (m_buffer, m_buffer_pos + count, term_size, enc);
                if (term_pos == m_buffer_pos + count)
                {
                    found = true;
                    break;
                }

                count += term_size;
            }

            var s = enc.GetString (m_buffer, m_buffer_pos, count);

            if (found)
                m_buffer_pos += count + term_size;
            else
                m_buffer_pos += cached;

            return s;
        }

        public byte[] ReadBytes (int count)
        {
            var buffer = new byte[count];
            int pos = 0;
            int cached = Math.Min (m_buffer_end - m_buffer_pos, count);
            if (cached > 0)
            {
                Buffer.BlockCopy (m_buffer, m_buffer_pos, buffer, 0, cached);
                pos = cached;
                m_buffer_pos += cached;
                count -= cached;
            }
            if (count > 0)
                pos += m_source.Read (buffer, pos, count);
            if (pos < buffer.Length)
            {
                var copy = new byte[pos];
                Buffer.BlockCopy (buffer, 0, copy, 0, copy.Length);
                buffer = copy;
            }
            return buffer;
        }

        #region IO.Stream Members
        public override bool CanRead  { get { return m_source.CanRead; } }
        public override bool CanSeek  { get { return m_source.CanSeek; } }
        public override bool CanWrite { get { return false; } }
        public override long Length   { get { return m_source.Length; } }
        public override long Position
        {
            get { return m_source.Position - (m_buffer_end - m_buffer_pos); }
            set
            {
                m_buffer_end = m_buffer_pos = 0;
                m_source.Position = value;
            }
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = 0;
            int cached = Math.Min (m_buffer_end - m_buffer_pos, count);
            if (cached > 0)
            {
                Buffer.BlockCopy (m_buffer, m_buffer_pos, buffer, offset, cached);
                m_buffer_pos += cached;
                offset += cached;
                count -= cached;
                read += cached;
            }
            if (count > 0)
            {
                read += m_source.Read (buffer, offset, count);
            }
            return read;
        }

        public override int ReadByte ()
        {
            int b;
            if (m_buffer_pos < m_buffer_end)
                b = m_buffer[m_buffer_pos++];
            else
                b = m_source.ReadByte();
            return b;
        }

        public override void Flush()
        {
            m_source.Flush();
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Current == origin)
                offset -= m_buffer_end - m_buffer_pos;
            m_buffer_end = m_buffer_pos = 0;
            return m_source.Seek (offset, origin);
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("BinaryStream.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("BinaryStream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException ("BinaryStream.WriteByte method is not supported");
        }
        #endregion

        #region IDisposable Members
        bool _disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (m_should_dispose)
                    m_source.Dispose();
            }
            _disposed = true;
            base.Dispose (disposing);
        }
        #endregion
    }

    public class BinMemoryStream : Stream, IBinaryStream
    {
        byte[]      m_source;
        int         m_start;
        int         m_length;
        int         m_position;
        uint        m_signature;
        Encoding    m_default_encoding;

        public string     Name { get; set; }
        public uint  Signature { get { return m_signature; } }
        public Stream AsStream { get { return this; } }
        public Encoding DefaultEncoding 
        { 
            get { return m_default_encoding ?? Encodings.cp932; }
            set { m_default_encoding = value; }
        }

        public BinMemoryStream (byte[] input, string name = "") : this (input, 0, input.Length, name)
        { }

        public BinMemoryStream (byte[] input, int pos, int length, string name = "")
        {
            m_source = input;
            m_start = pos;
            m_length = length;
            Init (name);
        }

        public BinMemoryStream (MemoryStream input, string name = "")
        {
            try
            {
                m_source = input.GetBuffer();
                if (null == m_source)
                    m_source = Array.Empty<byte>();
            }
            catch (UnauthorizedAccessException)
            {
                m_source = input.ToArray();
            }
            m_start = 0;
            m_length = (int)input.Length;
            Init (name);
        }

        void Init (string name)
        {
            m_position = 0;
            Name = name ?? "";
            if (m_length >= 4)
                m_signature = LittleEndian.ToUInt32 (m_source, m_start);
        }

        public CowArray<byte> ReadHeader (int size)
        {
            if (size > m_length)
            {
                m_position = m_length;
                throw new EndOfStreamException();
            }
            m_position = size;
            return new CowArray<byte> (m_source, m_start, size);
        }

        public int PeekByte ()
        {
            if (m_position >= m_length)
                return -1;
            return m_source[m_start+m_position];
        }

        public sbyte ReadInt8 ()
        {
            return (sbyte)ReadUInt8();
        }

        public byte ReadUInt8 ()
        {
            if (m_position >= m_length)
                throw new EndOfStreamException();
            return m_source[m_start+m_position++];
        }

        public short ReadInt16 ()
        {
            if (m_length - m_position < 2)
                throw new EndOfStreamException();
            short v = LittleEndian.ToInt16 (m_source, m_start+m_position);
            m_position += 2;
            return v;
        }

        public ushort ReadUInt16 ()
        {
            return (ushort)ReadInt16();
        }

        public int ReadInt24 ()
        {
            if (m_length - m_position < 3)
                throw new EndOfStreamException();
            int v = m_source.ToInt24 (m_start+m_position);
            m_position += 3;
            return v;
        }

        public int ReadInt32 ()
        {
            if (m_length - m_position < 4)
                throw new EndOfStreamException();
            int v = LittleEndian.ToInt32 (m_source, m_start+m_position);
            m_position += 4;
            return v;
        }

        public uint ReadUInt32 ()
        {
            return (uint)ReadInt32();
        }

        public long ReadInt64 ()
        {
            if (m_length - m_position < 8)
                throw new EndOfStreamException();
            long v = LittleEndian.ToInt64 (m_source, m_start+m_position);
            m_position += 8;
            return v;
        }

        public ulong ReadUInt64 ()
        {
            return (ulong)ReadInt64();
        }

        // Big-endian methods
        public short ReadInt16BE ()
        {
            if (m_length - m_position < 2)
                throw new EndOfStreamException();
            short v = (short)((m_source[m_start+m_position] << 8) | m_source[m_start+m_position + 1]);
            m_position += 2;
            return v;
        }

        public ushort ReadUInt16BE ()
        {
            return (ushort)ReadInt16BE();
        }

        public int ReadInt24BE ()
        {
            if (m_length - m_position < 3)
                throw new EndOfStreamException();
            int v = (m_source[m_start+m_position] << 16) | 
                    (m_source[m_start+m_position + 1] << 8) | 
                    m_source[m_start+m_position + 2];
            m_position += 3;
            return v;
        }

        public int ReadInt32BE ()
        {
            if (m_length - m_position < 4)
                throw new EndOfStreamException();
            int v = (m_source[m_start+m_position] << 24) | 
                    (m_source[m_start+m_position + 1] << 16) | 
                    (m_source[m_start+m_position + 2] << 8) | 
                    m_source[m_start+m_position + 3];
            m_position += 4;
            return v;
        }

        public uint ReadUInt32BE ()
        {
            return (uint)ReadInt32BE();
        }

        public long ReadInt64BE ()
        {
            if (m_length - m_position < 8)
                throw new EndOfStreamException();
            long v = ((long)m_source[m_start+m_position] << 56) | 
                     ((long)m_source[m_start+m_position + 1] << 48) | 
                     ((long)m_source[m_start+m_position + 2] << 40) | 
                     ((long)m_source[m_start+m_position + 3] << 32) |
                     ((long)m_source[m_start+m_position + 4] << 24) | 
                     ((long)m_source[m_start+m_position + 5] << 16) | 
                     ((long)m_source[m_start+m_position + 6] << 8) | 
                     m_source[m_start+m_position + 7];
            m_position += 8;
            return v;
        }

        public ulong ReadUInt64BE ()
        {
            return (ulong)ReadInt64BE();
        }

        public string ReadCString (int length)
        {
            return ReadCString (length, DefaultEncoding);
        }

        public string ReadCString (int length, Encoding enc)
        {
            int start = m_start+m_position;
            length = Math.Min (length, m_length - m_position);
            int eos_pos = BinaryStream.FindTerminator (m_source, start, length, enc);
            if (-1 == eos_pos) 
              eos_pos = start + length;

            m_position += length;
            return enc.GetString (m_source, start, eos_pos - start);
        }

        public string ReadCString ()
        {
            return ReadCString (DefaultEncoding);
        }

        public string ReadCString (Encoding enc)
        {
            int start = m_start+m_position;
            int length = m_length-m_position;
            int eos_pos = BinaryStream.FindTerminator (m_source, start, length, enc);
            int count;
            if (-1 == eos_pos)
            {
                count = length;
                m_position = m_length;
            }
            else
            {
                count = eos_pos - start;
                int eos_size = enc.IsSingleByte ? 1 : 
                              (enc.Equals (Encoding.Unicode) || enc.Equals (Encoding.BigEndianUnicode)) ? 2 : 
                               enc.Equals (Encoding.UTF32) ? 4 : 1;
                m_position += count + eos_size;
            }
            return enc.GetString (m_source, start, count);
        }

        public byte[] ReadBytes (int count)
        {
            count = Math.Min (count, m_length - m_position);
            var buffer = new byte[count];
            Buffer.BlockCopy (m_source, m_start+m_position, buffer, 0, count);
            m_position += count;
            return buffer;
        }

        #region IO.Stream Members
        public override bool CanRead  { get { return true; } }
        public override bool CanSeek  { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length   { get { return m_length; } }
        public override long Position
        {
            get { return m_position; }
            set { m_position = (int)Math.Max (Math.Min (m_length, value), 0); }
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            count = Math.Min (count, m_length - m_position);
            Buffer.BlockCopy (m_source, m_start+m_position, buffer, offset, count);
            m_position += count;
            return count;
        }

        public override int ReadByte ()
        {
            if (m_position < m_length)
                return m_source[m_start+m_position++];
            else
                return -1;
        }

        public override void Flush()
        {
        }

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

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("BinMemoryStream.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("BinMemoryStream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException ("BinMemoryStream.WriteByte method is not supported");
        }
        #endregion
    }

    /// <summary>
    /// Shared hex dump formatting utilities for both debug output and streaming.
    /// </summary>
    public static class HexDumpFormatter
    {
        /// <summary>
        /// Format a single hex dump line into the output buffer.
        /// Returns the number of bytes written to output.
        /// </summary>
        public static int FormatHexLine (byte[] data, int length, int offset, byte[] output)
        {
            var sb = new StringBuilder (80);

            sb.AppendFormat ("{0:X8}  ", offset);

            // Hex bytes
            for (int i = 0; i < 16; i++)
            {
                if (i < length)
                    sb.AppendFormat ("{0:X2} ", data[i]);
                else
                    sb.Append ("   ");
            }

            sb.Append (" | ");

            // ASCII
            for (int i = 0; i < length; i++)
            {
                byte b = data[i];
                if (b >= 0x20 && b < 0x7F)
                    sb.Append((char)b);
                else
                    sb.Append('.');
            }

            sb.Append("\n");

            string line = sb.ToString();
            byte[] lineBytes = Encoding.UTF8.GetBytes (line);
            Buffer.BlockCopy (lineBytes, 0, output, 0, lineBytes.Length);
            return lineBytes.Length;
        }

        /// <summary>
        /// Format hex dump of byte array with optional highlighting.
        /// </summary>
        public static string FormatHexDump (byte[] bytes, long baseOffset = 0, long highlightOffset = -1, string info = "", string format = "")
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty (info) || !string.IsNullOrEmpty (format))
            {
                if (!string.IsNullOrEmpty (format))
                    sb.Append ($"[{format}] ");
                if (!string.IsNullOrEmpty (info))
                    sb.Append ($"Hex dump for {info}");
                if (highlightOffset >= 0)
                    sb.Append ($" at position 0x{highlightOffset:X8}");
                sb.AppendLine (":");
            }

            for (int i = 0; i < bytes.Length; i += 16)
            {
                sb.AppendFormat ("{0:X8}  ", baseOffset + i);

                // Hex bytes
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < bytes.Length)
                    {
                        long currentOffset = baseOffset + i + j;
                        if (highlightOffset >= 0 && currentOffset == highlightOffset)
                            sb.Append ("[");
                        sb.AppendFormat ("{0:X2}", bytes[i + j]);
                        if (highlightOffset >= 0 && currentOffset == highlightOffset)
                            sb.Append ("]");
                        else
                            sb.Append (" ");
                    }
                    else
                        sb.Append ("   ");
                }

                sb.Append (" | ");

                // ASCII
                for (int j = 0; j < 16 && i + j < bytes.Length; j++)
                {
                    byte b = bytes[i + j];
                    if (b >= 0x20 && b < 0x7F)
                        sb.Append((char)b);
                    else
                        sb.Append('.');
                }

                sb.Append("\n");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Extension methods for IBinaryStream debug helpers.
    /// </summary>
    public static class BinaryStreamExtensions
    {
        /// <summary>
        /// Print hex dump of stream context to debug output with highlighted current position.
        /// </summary>
        public static void PrintHexDump (this IBinaryStream stream, string info, string format = "", uint context_size = 64)
        {
            try
            {
                if (!stream.CanSeek)
                {
                    byte[] rbytes = stream.ReadBytes ((int)context_size);
                    string rdump = HexDumpFormatter.FormatHexDump (rbytes, 0, -1, $"{info} [continuation]", format);
                    System.Diagnostics.Trace.WriteLine (rdump);
                    return;
                }

                long currentPos = stream.Position;
                long startPos = Math.Max (0, currentPos - context_size);
                long endPos = Math.Min (stream.Length, currentPos + context_size);

                stream.Position = startPos;
                byte[] bytes = stream.ReadBytes((int)(endPos - startPos));

                string dump = HexDumpFormatter.FormatHexDump (bytes, startPos, currentPos, info, format);
                System.Diagnostics.Trace.WriteLine (dump);

                stream.Position = currentPos;
            }
            catch (Exception ex) 
            { 
                 System.Diagnostics.Trace.WriteLine (ex);
            }
        }

        /// <summary>
        /// Get hex dump string of a stream.
        /// </summary>
        public static string GetHexDump (this IBinaryStream stream, string info = "", string format = "", uint context_size = 128)
        {
            try
            {
                long currentPos = stream.Position;
                long startPos = Math.Max (0, currentPos - context_size);
                long endPos = Math.Min (stream.Length, currentPos + context_size);

                stream.Position = startPos;
                var bytes = stream.ReadBytes((int)(endPos - startPos));
                stream.Position = currentPos;

                return HexDumpFormatter.FormatHexDump (bytes, startPos, currentPos, info, format);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Print hex dump of raw byte array to debug output.
        /// </summary>
        public static void PrintHexDump (this byte[] bytes, string info = "", string format = "", long baseOffset = 0, long highlightOffset = -1)
        {
            string dump = HexDumpFormatter.FormatHexDump (bytes, baseOffset, highlightOffset, info, format);
            System.Diagnostics.Trace.WriteLine (dump);
        }
    }
}