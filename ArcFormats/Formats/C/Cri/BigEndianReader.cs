using System;
using System.IO;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Cri
{
    public class BigEndianReader : IDisposable
    {
        BinaryReader    m_input;
        byte[]          m_buffer = new byte[8];

        public long Position
        {
            get { return m_input.BaseStream.Position; }
            set { m_input.BaseStream.Position = value; }
        }

        public Stream BaseStream { get { return m_input.BaseStream; } }

        public BigEndianReader (Stream input) : this (input, Encoding.UTF8, false)
        {
        }

        public BigEndianReader (Stream input, Encoding enc, bool leave_open = false)
        {
            m_input = new BinaryReader (input, enc, leave_open);
        }

        public int Read (byte[] buffer, int index, int count)
        {
            return m_input.Read (buffer, index, count);
        }

        public void Skip (int amount)
        {
            m_input.BaseStream.Seek (amount, SeekOrigin.Current);
        }

        public byte ReadByte ()
        {
            return m_input.ReadByte();
        }

        public sbyte ReadSByte ()
        {
            return m_input.ReadSByte();
        }

        public short ReadInt16 ()
        {
            return Binary.BigEndian (m_input.ReadInt16());
        }

        public ushort ReadUInt16 ()
        {
            return Binary.BigEndian (m_input.ReadUInt16());
        }

        public int ReadInt32 ()
        {
            return Binary.BigEndian (m_input.ReadInt32());
        }

        public uint ReadUInt32 ()
        {
            return Binary.BigEndian (m_input.ReadUInt32());
        }

        public long ReadInt64 ()
        {
            return Binary.BigEndian (m_input.ReadInt64());
        }

        public ulong ReadUInt64 ()
        {
            return Binary.BigEndian (m_input.ReadUInt64());
        }

        public float ReadSingle ()
        {
            if (4 != m_input.Read (m_buffer, 0, 4))
                throw new EndOfStreamException();
            if (BitConverter.IsLittleEndian)
                Array.Reverse (m_buffer, 0, 4);
            return BitConverter.ToSingle (m_buffer, 0);
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                    m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }
}
