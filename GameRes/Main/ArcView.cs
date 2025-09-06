using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace GameRes
{
    /// <summary>
    /// Provides encoding utilities and common encoding instances.
    /// </summary>
    public static class Encodings
    {
        /// <summary>
        /// Japanese Windows code page 932 (Shift-JIS) encoding.
        /// </summary>
        public static readonly Encoding cp932 = Encoding.GetEncoding (932);

        /// <summary>
        /// Creates a clone of the encoding with fatal fallback behavior for encoding/decoding errors.
        /// </summary>
        /// <param name="enc">The encoding to clone.</param>
        /// <returns>A new encoding instance with exception fallback behavior.</returns>
        public static Encoding WithFatalFallback (this Encoding enc)
        {
            var encoding = enc.Clone() as Encoding;
            encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
            encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
            return encoding;
        }

        /// <summary>
        /// Determines whether the encoding is UTF-16 (little or big endian).
        /// </summary>
        /// <param name="enc">The encoding to check.</param>
        /// <returns>True if the encoding is UTF-16; otherwise, false.</returns>
        public static bool IsUtf16 (this Encoding enc)
        {
            // enc.WindowsCodePage property might throw an exception for some encodings, while
            // CodePage is just a direct field access.
            return (enc.CodePage == 1200 || enc.CodePage == 1201); // enc is UnicodeEncoding
        }
    }

    /// <summary>
    /// Extension methods for Stream operations.
    /// </summary>
    public static class StreamExtension
    {
        /// <summary>
        /// Reads bytes from the stream until a delimiter is encountered or end of stream is reached.
        /// </summary>
        /// <param name="file">The stream to read from.</param>
        /// <param name="delim">The delimiter byte to stop at.</param>
        /// <param name="enc">The encoding to use for converting bytes to string.</param>
        /// <returns>The string read from the stream.</returns>
        public static string ReadStringUntil (this Stream file, byte delim, Encoding enc)
        {
            byte[] buffer = new byte[16];
            int size = 0;
            for (;;)
            {
                int b = file.ReadByte();
                if (-1 == b || delim == b)
                    break;
                if (buffer.Length == size)
                {
                    Array.Resize (ref buffer, checked (size / 2 * 3));
                }
                buffer[size++] = (byte)b;
            }
            return enc.GetString (buffer, 0, size);
        }

        /// <summary>
        /// Reads a null-terminated string from the stream using the specified encoding.
        /// </summary>
        /// <param name="file">The stream to read from.</param>
        /// <param name="enc">The encoding to use for converting bytes to string.</param>
        /// <returns>The null-terminated string read from the stream.</returns>
        public static string ReadCString (this Stream file, Encoding enc)
        {
            return ReadStringUntil (file, 0, enc);
        }

        /// <summary>
        /// Reads a null-terminated string from the stream using CP932 encoding.
        /// </summary>
        /// <param name="file">The stream to read from.</param>
        /// <returns>The null-terminated string read from the stream.</returns>
        public static string ReadCString (this Stream file)
        {
            return ReadStringUntil (file, 0, Encodings.cp932);
        }
    }

    /// <summary>
    /// Extension methods for MemoryMappedViewAccessor operations.
    /// </summary>
    public static class MappedViewExtension
    {
        private static SYSTEM_INFO info;

        static MappedViewExtension()
        {
            GetSystemInfo (ref info);
        }

        /// <summary>
        /// Gets an unsafe pointer to the memory mapped view at the specified offset.
        /// </summary>
        /// <param name="view">The memory mapped view accessor.</param>
        /// <param name="offset">The offset within the view.</param>
        /// <returns>A pointer to the memory location.</returns>
        public static unsafe byte* GetPointer (this MemoryMappedViewAccessor view, long offset)
        {
            var num = offset % info.dwAllocationGranularity;
            byte* ptr = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer (ref ptr);
            ptr += num;
            return ptr;
        }

        [DllImport ("kernel32.dll", SetLastError = false)]
        internal static extern void GetSystemInfo (ref SYSTEM_INFO lpSystemInfo);

        [StructLayout (LayoutKind.Sequential)]
        internal struct SYSTEM_INFO
        {
            internal int dwOemId;
            internal int dwPageSize;
            internal IntPtr lpMinimumApplicationAddress;
            internal IntPtr lpMaximumApplicationAddress;
            internal IntPtr dwActiveProcessorMask;
            internal int dwNumberOfProcessors;
            internal int dwProcessorType;
            internal int dwAllocationGranularity;
            internal short wProcessorLevel;
            internal short wProcessorRevision;
        }
    }

    /// <summary>
    /// Provides memory-mapped file access for archive files with efficient random access capabilities.
    /// </summary>
    public class ArcView : IDisposable
    {
        private MemoryMappedFile m_map;
        private bool disposed = false;

        /// <summary>
        /// Default page size for memory mapping operations.
        /// </summary>
        public const long PageSize = 4096;

        /// <summary>
        /// Gets the maximum offset (size) of the mapped file.
        /// </summary>
        public long MaxOffset { get; private set; }

        /// <summary>
        /// Gets the default view frame for this archive.
        /// </summary>
        public Frame View { get; private set; }

        /// <summary>
        /// Gets the name of the mapped file.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Creates a new ArcView from a file path.
        /// </summary>
        /// <param name="name">The path to the file to map.</param>
        public ArcView (string name)
        {
            using (var fs = new FileStream (name, FileMode.Open, FileAccess.Read, 
                                           FileShare.ReadWrite | FileShare.Delete))
            {
                Name = name;
                MaxOffset = fs.Length;
                InitFromFileStream (fs, 0);
            }
        }

        /// <summary>
        /// Creates a new ArcView from a stream.
        /// </summary>
        /// <param name="input">The input stream to map.</param>
        /// <param name="name">The name to associate with this view.</param>
        /// <param name="length">The length of data to map from the stream.</param>
        public ArcView (Stream input, string name, long length)
        {
            Name = name;
            MaxOffset = length;
            if (input is FileStream)
                InitFromFileStream (input as FileStream, length);
            else
                InitFromStream (input, length);
        }

        private void InitFromFileStream (FileStream fs, long length)
        {
            m_map = MemoryMappedFile.CreateFromFile (fs, null, length,
                MemoryMappedFileAccess.Read, null, HandleInheritability.None, true);
            try
            {
                View = new Frame (this);
            }
            catch
            {
                m_map.Dispose(); // dispose on error only
                throw;
            }
        }

        private void InitFromStream (Stream input, long length)
        {
            m_map = MemoryMappedFile.CreateNew (null, length, MemoryMappedFileAccess.ReadWrite,
                MemoryMappedFileOptions.None, null, HandleInheritability.None);
            try
            {
                using (var view = m_map.CreateViewAccessor (0, length, MemoryMappedFileAccess.Write))
                {
                    CopyStreamToView (input, view, length);
                }
                View = new Frame (this);
            }
            catch
            {
                m_map.Dispose();
                throw;
            }
        }

        private unsafe void CopyStreamToView (Stream input, MemoryMappedViewAccessor view, long length)
        {
            const int BufferSize = 81920;
            var buffer = new byte[BufferSize];

            byte* ptr = view.GetPointer (0);
            try
            {
                uint total = 0;
                while (total < length)
                {
                    int toRead = (int)Math.Min (BufferSize, length - total);
                    int read = input.Read (buffer, 0, toRead);
                    if (0 == read)
                        break;

                    read = (int)Math.Min (read, length - total);
                    Marshal.Copy (buffer, 0, (IntPtr)(ptr + total), read);
                    total += (uint)read;
                }
                MaxOffset = total;
            }
            finally
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }

        /// <summary>
        /// Creates a new frame for accessing the mapped file.
        /// </summary>
        /// <returns>A new Frame instance.</returns>
        public Frame CreateFrame()
        {
            return new Frame (View);
        }

        /// <summary>
        /// Creates a new ArcView from a byte array.
        /// </summary>
        /// <param name="data">The byte array containing the data.</param>
        /// <param name="name">The name to associate with this view.</param>
        /// <returns>A new ArcView instance.</returns>
        public static ArcView CreateView (byte[] data, string name = "")
        {
            var stream = new MemoryStream (data, false);
            return new ArcView (stream, name, data.Length);
        }

        /// <summary>
        /// Creates a new ArcView from an existing ArcView at a specific offset.
        /// </summary>
        /// <param name="offset">The offset within the current view.</param>
        /// <param name="size">The size of the new view.</param>
        /// <param name="name">File name of the new view.</param>
        /// <returns>A new ArcView instance.</returns>
        public ArcView CreateView (long offset, uint size, string name = null)
        {
            if (offset < 0 || offset > MaxOffset)
                throw new ArgumentOutOfRangeException (nameof(offset));

            if (offset + size > MaxOffset)
                size = (uint)(MaxOffset - offset);

            var stream = this.CreateStream (offset, size);
            name = name ?? this.Name;
            return new ArcView (stream, name, size);
        }

        /// <summary>
        /// Creates a stream for reading the entire mapped file.
        /// </summary>
        /// <returns>A new ArcViewStream instance.</returns>
        public ArcViewStream CreateStream()
        {
            return new ArcViewStream (this);
        }

        /// <summary>
        /// Creates a stream for reading from a specific offset to the end of the file.
        /// </summary>
        /// <param name="offset">The starting offset.</param>
        /// <returns>A new ArcViewStream instance.</returns>
        public ArcViewStream CreateStream (long offset)
        {
            var size = this.MaxOffset - offset;
            if (size > uint.MaxValue)
                throw new ArgumentOutOfRangeException ("offset", "Too large memory mapped stream");
            return new ArcViewStream (this, offset, (uint)size);
        }

        /// <summary>
        /// Creates a stream for reading a specific portion of the mapped file.
        /// </summary>
        /// <param name="offset">The starting offset.</param>
        /// <param name="size">The size of the portion to read.</param>
        /// <param name="name">Optional name for the stream.</param>
        /// <returns>A new ArcViewStream instance.</returns>
        public ArcViewStream CreateStream (long offset, long size, string name = null)
        {
            return new ArcViewStream (this, offset, size, name);
        }

        /// <summary>
        /// Creates a view accessor for a specific portion of the mapped file.
        /// </summary>
        /// <param name="offset">The starting offset.</param>
        /// <param name="size">The size of the portion to access.</param>
        /// <returns>A new MemoryMappedViewAccessor instance.</returns>
        public MemoryMappedViewAccessor CreateViewAccessor (long offset, long size)
        {
            return m_map.CreateViewAccessor (offset, size, MemoryMappedFileAccess.Read);
        }

        #region IDisposable Members
        public void Dispose()
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
                    View.Dispose();
                    m_map.Dispose();
                }
                disposed = true;
                m_map = null;
            }
        }
        #endregion

        /// <summary>
        /// Represents a view frame for accessing a portion of the memory-mapped file.
        /// </summary>
        public class Frame : IDisposable
        {
            private ArcView m_arc;
            private MemoryMappedViewAccessor m_view;
            private long m_offset;
            private long m_size;
            private unsafe byte* m_mem;
            private bool disposed = false;

            /// <summary>
            /// Gets the current offset of this frame within the mapped file.
            /// </summary>
            public long Offset { get { return m_offset; } }

            /// <summary>
            /// Gets the reserved size of this frame.
            /// </summary>
            public long Reserved { get { return m_size; } }

            /// <summary>
            /// Creates a new frame starting at the beginning of the archive.
            /// </summary>
            /// <param name="arc">The parent ArcView.</param>
            public Frame (ArcView arc)
            {
                m_arc = arc;
                m_offset = 0;
                m_size = (uint)Math.Min (ArcView.PageSize, m_arc.MaxOffset);
                m_view = m_arc.CreateViewAccessor (m_offset, m_size);
                unsafe { m_mem = m_view.GetPointer (m_offset); }
            }

            /// <summary>
            /// Creates a copy of an existing frame.
            /// </summary>
            /// <param name="other">The frame to copy.</param>
            public Frame (Frame other)
            {
                m_arc = other.m_arc;
                m_offset = 0;
                m_size = (uint)Math.Min (ArcView.PageSize, m_arc.MaxOffset);
                m_view = m_arc.CreateViewAccessor (m_offset, m_size);
                unsafe { m_mem = m_view.GetPointer (m_offset); }
            }

            /// <summary>
            /// Creates a new frame for a specific portion of the archive.
            /// </summary>
            /// <param name="arc">The parent ArcView.</param>
            /// <param name="offset">The starting offset.</param>
            /// <param name="size">The size of the frame.</param>
            public Frame (ArcView arc, long offset, long size)
            {
                m_arc = arc;
                m_offset = Math.Min (offset, m_arc.MaxOffset);
                m_size = (uint)Math.Min (size, m_arc.MaxOffset - m_offset);
                m_view = m_arc.CreateViewAccessor (m_offset, m_size);
                unsafe { m_mem = m_view.GetPointer (m_offset); }
            }

            /// <summary>
            /// Reserves a portion of the mapped file, potentially remapping if necessary.
            /// </summary>
            /// <param name="offset">The starting offset to reserve.</param>
            /// <param name="size">The size to reserve.</param>
            /// <returns>The actual number of bytes reserved.</returns>
            public uint Reserve (long offset, long size)
            {
                if (offset < m_offset || offset + size > m_offset + m_size)
                {
                    RemapView (offset, size);
                }
                return (uint)(m_offset + m_size - offset);
            }

            private void RemapView (long offset, long size)
            {
                if (offset > m_arc.MaxOffset)
                    throw new ArgumentOutOfRangeException ("offset", "Too large offset specified for memory mapped file view.");
                if (disposed)
                    throw new ObjectDisposedException (null);

                if (size < ArcView.PageSize)
                    size = (uint)ArcView.PageSize;
                if (size > m_arc.MaxOffset - offset)
                    size = (uint)(m_arc.MaxOffset - offset);

                var old_view = m_view;
                m_view = m_arc.CreateViewAccessor (offset, size);
                old_view.SafeMemoryMappedViewHandle.ReleasePointer();
                old_view.Dispose();
                m_offset = offset;
                m_size = size;
                unsafe { m_mem = m_view.GetPointer (m_offset); }
            }

            /// <summary>
            /// Reserves a portion of the mapped file and throws if the requested size cannot be reserved.
            /// </summary>
            /// <param name="offset">The starting offset to reserve.</param>
            /// <param name="size">The size to reserve.</param>
            /// <exception cref="ArgumentException">Thrown when not enough bytes can be reserved.</exception>
            public void StrictReserve (long offset, long size)
            {
                if (Reserve (offset, size) < size)
                    throw new ArgumentException ("Not enough bytes to read in the memory mapped file view.", "offset");
            }

            /// <summary>
            /// Compares bytes at the specified offset with the provided data.
            /// </summary>
            /// <param name="offset">The offset to start comparison.</param>
            /// <param name="data">The data to compare against.</param>
            /// <returns>True if the bytes match; otherwise, false.</returns>
            public bool BytesEqual (long offset, byte[] data)
            {
                if (Reserve (offset, (uint)data.Length) < (uint)data.Length)
                    return false;
                unsafe
                {
                    byte* ptr = m_mem + (offset - m_offset);
                    for (int i = 0; i < data.Length; ++i)
                    {
                        if (ptr[i] != data[i])
                            return false;
                    }
                    return true;
                }
            }

            /// <summary>
            /// Compares ASCII string at the specified offset with the provided string.
            /// </summary>
            /// <param name="offset">The offset to start comparison.</param>
            /// <param name="data">The ASCII string to compare against.</param>
            /// <returns>True if the strings match; otherwise, false.</returns>
            public bool AsciiEqual (long offset, string data)
            {
                if (Reserve (offset, (uint)data.Length) < (uint)data.Length)
                    return false;
                unsafe
                {
                    byte* ptr = m_mem + (offset - m_offset);
                    for (int i = 0; i < data.Length; ++i)
                    {
                        if (ptr[i] != data[i])
                            return false;
                    }
                    return true;
                }
            }

            /// <summary>
            /// Reads bytes from the specified offset into a buffer.
            /// </summary>
            /// <param name="offset">The offset to start reading from.</param>
            /// <param name="buf">The buffer to read into.</param>
            /// <param name="buf_offset">The offset within the buffer to start writing.</param>
            /// <param name="count">The number of bytes to read.</param>
            /// <returns>The actual number of bytes read.</returns>
            public int Read (long offset, byte[] buf, long buf_offset, long count)
            {
                if (buf == null)
                    throw new ArgumentNullException ("buf", "Buffer cannot be null.");
                if (buf_offset < 0)
                    throw new ArgumentOutOfRangeException ("buf_offset", "Buffer offset should be non-negative.");
                if (disposed)
                    throw new ObjectDisposedException (null);

                int total = (int)Math.Min (Reserve (offset, count), count);
                if (buf.Length - buf_offset < total)
                    throw new ArgumentException ("Buffer offset and length are out of bounds.");
                UnsafeCopy (offset, buf, (int)buf_offset, total);
                return total;
            }

            private unsafe void UnsafeCopy (long offset, byte[] buf, int buf_offset, int count)
            {
                Marshal.Copy((IntPtr)(m_mem + (offset - m_offset)), buf, buf_offset, count);
            }

            /// <summary>
            /// Reads bytes from the specified offset and returns them as a new array.
            /// </summary>
            /// <param name="offset">The offset to start reading from.</param>
            /// <param name="count">The number of bytes to read.</param>
            /// <returns>A byte array containing the read data.</returns>
            public byte[] ReadBytes (long offset, long count)
            {
                count = Math.Min (count, Reserve (offset, count));
                var data = new byte[count];
                if (count != 0)
                    UnsafeCopy (offset, data, 0, data.Length);
                return data;
            }

            /// <summary>
            /// Reads a single byte from the specified offset.
            /// </summary>
            /// <param name="offset">The offset to read from.</param>
            /// <returns>The byte value at the specified offset.</returns>
            public byte ReadByte (long offset)
            {
                StrictReserve (offset, 1);
                unsafe { return m_mem[offset - m_offset]; }
            }

            /// <summary>
            /// Reads a signed byte from the specified offset.
            /// </summary>
            /// <param name="offset">The offset to read from.</param>
            /// <returns>The signed byte value at the specified offset.</returns>
            public sbyte ReadSByte (long offset)
            {
                StrictReserve (offset, 1);
                unsafe { return (sbyte)m_mem[offset - m_offset]; }
            }

            /// <summary>
            /// Reads an unsigned 16-bit integer from the specified offset.
            /// </summary>
            /// <param name="offset">The offset to read from.</param>
            /// <returns>The unsigned 16-bit integer value.</returns>
            public ushort ReadUInt16 (long offset)
            {
                StrictReserve (offset, 2);
                unsafe { return *(ushort*)(m_mem + offset - m_offset); }
            }

            /// <summary>
            /// Reads a signed 16-bit integer from the specified offset.
            /// </summary>
            /// <param name="offset">The offset to read from.</param>
            /// <returns>The signed 16-bit integer value.</returns>
            public short ReadInt16 (long offset)
            {
                StrictReserve (offset, 2);
                unsafe { return *(short*)(m_mem + offset - m_offset); }
            }

            /// <summary>
            /// Reads an unsigned 32-bit integer from the specified offset.
            /// </summary>
            /// <param name="offset">The offset to read from.</param>
            /// <returns>The unsigned 32-bit integer value.</returns>
            public uint ReadUInt32 (long offset)
            {
                StrictReserve (offset, 4);
                unsafe { return *(uint*)(m_mem + offset - m_offset); }
            }

            /// <summary>
            /// Reads a signed 32-bit integer from the specified offset.
            /// </summary>
            /// <param name="offset">The offset to read from.</param>
            /// <returns>The signed 32-bit integer value.</returns>
            public int ReadInt32 (long offset)
            {
                StrictReserve (offset, 4);
                unsafe { return *(int*)(m_mem + offset - m_offset); }
            }

            /// <summary>
            /// Reads an unsigned 64-bit integer from the specified offset.
            /// </summary>
            /// <param name="offset">The offset to read from.</param>
            /// <returns>The unsigned 64-bit integer value.</returns>
            public ulong ReadUInt64 (long offset)
            {
                StrictReserve (offset, 8);
                unsafe { return *(ulong*)(m_mem + offset - m_offset); }
            }

            /// <summary>
            /// Reads a signed 64-bit integer from the specified offset.
            /// </summary>
            /// <param name="offset">The offset to read from.</param>
            /// <returns>The signed 64-bit integer value.</returns>
            public long ReadInt64 (long offset)
            {
                StrictReserve (offset, 8);
                unsafe { return *(long*)(m_mem + offset - m_offset); }
            }

            /// <summary>
            /// Reads a string from the specified offset with the given encoding.
            /// </summary>
            /// <param name="offset">The offset to start reading from.</param>
            /// <param name="size">The maximum size to read.</param>
            /// <param name="enc">The encoding to use.</param>
            /// <returns>The decoded string.</returns>
            public string ReadString (long offset, uint size, Encoding enc)
            {
                size = Math.Min (size, Reserve (offset, size));
                if (0 == size)
                    return string.Empty;
                unsafe
                {
                    byte* s = m_mem + (offset - m_offset);
                    uint string_length = 0;
                    if (enc.IsUtf16()) // for UTF-16 encodings stop marker is 2-bytes long
                    {
                        ushort* u = (ushort*)s;
                        while (string_length + 1 < size && 0 != u[string_length >> 1])
                        {
                            string_length += 2;
                        }
                    }
                    else
                    {
                        while (string_length < size && 0 != s[string_length])
                        {
                            ++string_length;
                        }
                    }
                    return new string((sbyte*)s, 0, (int)string_length, enc);
                }
            }

            /// <summary>
            /// Reads a string from the specified offset using CP932 encoding.
            /// </summary>
            /// <param name="offset">The offset to start reading from.</param>
            /// <param name="size">The maximum size to read.</param>
            /// <returns>The decoded string.</returns>
            public string ReadString (long offset, uint size)
            {
                return ReadString (offset, size, Encodings.cp932);
            }

            /// <summary>
            /// Gets an unsafe pointer wrapper for this frame.
            /// </summary>
            /// <returns>A ViewPointer instance.</returns>
            internal unsafe ViewPointer GetPointer()
            {
                return new ViewPointer (m_view, m_offset);
            }

            #region IDisposable Members
            public void Dispose()
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
                        unsafe
                        {
                            if (m_mem != null)
                            {
                                m_view.SafeMemoryMappedViewHandle.ReleasePointer();
                                m_mem = null;
                            }
                        }
                        m_view.Dispose();
                    }
                    m_arc = null;
                    m_view = null;
                    m_size = 0;
                    disposed = true;
                }
            }
            #endregion
        }

        /// <summary>
        /// A BinaryReader implementation for ArcView streams.
        /// </summary>
        public class Reader : System.IO.BinaryReader
        {
            /// <summary>
            /// Creates a new Reader instance.
            /// </summary>
            /// <param name="stream">The stream to read from.</param>
            public Reader (Stream stream) : base (stream, Encoding.ASCII, true)
            {
            }
        }
    }

    /// <summary>
    /// Provides an unsafe wrapper around unmanaged memory mapped view pointer.
    /// </summary>
    public unsafe class ViewPointer : IDisposable
    {
        private MemoryMappedViewAccessor m_view;
        private byte* m_ptr;
        private bool _disposed = false;

        /// <summary>
        /// Creates a new ViewPointer instance.
        /// </summary>
        /// <param name="view">The memory mapped view accessor.</param>
        /// <param name="offset">The offset within the view.</param>
        public ViewPointer (MemoryMappedViewAccessor view, long offset)
        {
            m_view = view;
            m_ptr = m_view.GetPointer (offset);
        }

        /// <summary>
        /// Gets the pointer value.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when accessing a disposed pointer.</exception>
        public byte* Value
        {
            get
            {
                if (!_disposed)
                    return m_ptr;
                else
                    throw new ObjectDisposedException (null, "Access to disposed ViewPointer object failed.");
            }
        }

        #region IDisposable Members
        public void Dispose()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    m_view.SafeMemoryMappedViewHandle.ReleasePointer();
                }
                _disposed = true;
            }
        }
        #endregion
    }
}
