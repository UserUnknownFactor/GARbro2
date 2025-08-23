using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Unity
{
    /// <summary>
    /// AssetReader provides access to a serialized stream of Unity assets.
    /// </summary>
    internal sealed class AssetReader : IDisposable
    {
        IBinaryStream   m_input;
        int             m_format;
        string          m_name;
        long            m_initial_pos;

        const int MAX_STRING_LENGTH = 0x100000;

        public Stream Source { get { return m_input.AsStream; } }
        public int    Format { get { return m_format; } }
        public long Position {
            get { return m_input.Position; }
            set { m_input.Position = value; }
        }
        public long   Origin { get { return m_initial_pos; } }
        public string   Name { get { return m_name; } }

        public AssetReader (Stream input, string name) : this (BinaryStream.FromStream (input, name))
        {
            m_name = name;
        }

        public AssetReader (IBinaryStream input)
        {
            m_input = input;
            m_initial_pos = input.Position;
            SetupReaders (0, false);
        }

        public Action       Align;
        public Func<ushort> ReadUInt16;
        public Func<short>  ReadInt16;
        public Func<uint>   ReadUInt32;
        public Func<int>    ReadInt32;
        public Func<long>   ReadInt64;
        public Func<long>   ReadUInt64;
        public Func<long>   ReadId;
        public Func<long>   ReadOffset;

        public void SetupReaders (Asset asset)
        {
            SetupReaders (asset.Format, asset.IsLittleEndian);
        }

        /// <summary>
        /// Setup reader endianness accordingly.
        /// </summary>
        public void SetupReaders (int format, bool is_little_endian)
        {
            m_format = format;
            if (is_little_endian)
            {
                ReadUInt16 = () => m_input.ReadUInt16();
                ReadUInt32 = () => m_input.ReadUInt32();
                ReadInt16  = () => m_input.ReadInt16();
                ReadInt32  = () => m_input.ReadInt32();
                ReadInt64  = () => m_input.ReadInt64();
                ReadUInt64 = () => (long)m_input.ReadUInt64();
            }
            else
            {
                ReadUInt16 = () => Binary.BigEndian (m_input.ReadUInt16());
                ReadUInt32 = () => Binary.BigEndian (m_input.ReadUInt32());
                ReadInt16  = () => Binary.BigEndian (m_input.ReadInt16());
                ReadInt32  = () => Binary.BigEndian (m_input.ReadInt32());
                ReadInt64  = () => Binary.BigEndian (m_input.ReadInt64());
                ReadUInt64 = () => (long)Binary.BigEndian (m_input.ReadUInt64());
            }

            if (m_format >= 14 || m_format == 9)
            {
                Align = () => {
                    long pos = m_input.Position;
                    if (0 != (pos & 3))
                        m_input.Position = (pos + 3) & ~3L;
                };
            }
            else
            {
                Align = () => {};
            }

            if (m_format >= 14)
                ReadId = ReadInt64;
            else
                ReadId = () => ReadInt32();

            if (m_format >= 22)
                ReadOffset = ReadInt64;
            else
                ReadOffset = () => ReadUInt32();
        }

        /// <summary>
        /// Set asset ID length.  If <paramref name="long_id"/> is <c>true</c> IDs are 64-bit, otherwise 32-bit.
        /// </summary>
        public void SetupReadId (bool long_ids)
        {
            if (long_ids)
                ReadId = ReadInt64;
            else
                ReadId = () => ReadInt32();
        }

        public void Skip (int count)
        {
            m_input.Seek (count, SeekOrigin.Current);
        }

        /// <summary>
        /// Read bytes into specified buffer.
        /// </summary>
        public int Read (byte[] buffer, int offset, int count)
        {
            return m_input.Read (buffer, offset, count);
        }

        /// <summary>
        /// Read null-terminated UTF8 string.
        /// </summary>
        public string ReadCString ()
        {
            return m_input.ReadCString (Encoding.UTF8);
        }

        /// <summary>
        /// Read UTF8 string prefixed with length.
        /// </summary>
        public string ReadString ()
        {
            int length = ReadInt32();
            if (0 == length)
                return string.Empty;
            if (length < 0 || length > MAX_STRING_LENGTH)
                throw new InvalidFormatException();
            var bytes = ReadBytes (length);
            return Encoding.UTF8.GetString (bytes);
        }

        /// <summary>
        /// Read <paramref name="length"/> bytes from stream and return them in a byte array.
        /// May return less than <paramref name="length"/> bytes if end of file was encountered.
        /// </summary>
        public byte[] ReadBytes (int length)
        {
            return m_input.ReadBytes (length);
        }

        /// <summary>
        /// Read unsigned 8-bits byte from a stream.
        /// </summary>
        public byte ReadByte ()
        {
            return m_input.ReadUInt8();
        }

        /// <summary>
        /// Read byte and interpret is as a bool value, non-zero resulting in <c>true</c>.
        /// </summary>
        public bool ReadBool ()
        {
            return ReadByte() != 0;
        }

        /// <summary>
        /// Read array of elements prefixed with count.
        /// </summary>
        public T[] ReadArray<T>(Func<T> readElement)
        {
            int count = ReadInt32();
            var array = new T[count];
            for (int i = 0; i < count; ++i)
                array[i] = readElement();
            return array;
        }

        /// <summary>
        /// Read array of primitive types prefixed with count.
        /// </summary>
        public T[] ReadPrimitiveArray<T>() where T : struct
        {
            int count = ReadInt32();
            var array = new T[count];

            if (typeof(T) == typeof(int))
            {
                for (int i = 0; i < count; ++i)
                    array[i] = (T)(object)ReadInt32();
            }
            else if (typeof(T) == typeof(uint))
            {
                for (int i = 0; i < count; ++i)
                    array[i] = (T)(object)ReadUInt32();
            }
            else if (typeof(T) == typeof(short))
            {
                for (int i = 0; i < count; ++i)
                    array[i] = (T)(object)ReadInt16();
            }
            else if (typeof(T) == typeof(ushort))
            {
                for (int i = 0; i < count; ++i)
                    array[i] = (T)(object)ReadUInt16();
            }
            else if (typeof(T) == typeof(long))
            {
                for (int i = 0; i < count; ++i)
                    array[i] = (T)(object)ReadInt64();
            }
            else if (typeof(T) == typeof(float))
            {
                for (int i = 0; i < count; ++i)
                    array[i] = (T)(object)ReadFloat();
            }
            else if (typeof(T) == typeof(byte))
            {
                for (int i = 0; i < count; ++i)
                    array[i] = (T)(object)ReadByte();
            }
            else if (typeof(T) == typeof(bool))
            {
                for (int i = 0; i < count; ++i)
                    array[i] = (T)(object)ReadBool();
            }
            else
            {
                throw new NotSupportedException($"Type {typeof(T)} is not supported for array reading");
            }

            return array;
        }

        public int[] ReadInt32Array()
        {
            return ReadPrimitiveArray<int>();
        }

        public uint[] ReadUInt32Array()
        {
            return ReadPrimitiveArray<uint>();
        }

        public float[] ReadFloatArray()
        {
            return ReadPrimitiveArray<float>();
        }

        public string[] ReadStringArray()
        {
            return ReadArray(ReadString);
        }

        public string[] ReadCStringArray()
        {
            return ReadArray(ReadCString);
        }

        [StructLayout(LayoutKind.Explicit)]
        struct Union
        {
            [FieldOffset (0)]
            public uint u;
            [FieldOffset(0)]
            public float f;
        }

        /// <summary>
        /// Read float value from a stream.
        /// </summary>
        public float ReadFloat ()
        {
            var buf = new Union();
            buf.u = ReadUInt32();
            return buf.f;
        }

        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize (this);
        }
    }
}
