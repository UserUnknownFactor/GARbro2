namespace GameRes.Utility
{
    /// <summary>
    /// Base CRC32 implementation supporting different polynomials
    /// </summary>
    public abstract class Crc32Base : ICheckSum
    {
        protected uint m_crc;

        public virtual uint Value { get { return ~m_crc; } }

        protected Crc32Base (uint initial)
        {
            m_crc = initial;
        }

        public abstract void Update (byte[] data, int pos = 0, int length = 0);
    }

    /// <summary>
    /// Crc32 with normal polynomial representation.
    /// </summary>
    public sealed class Crc32Normal : Crc32Base
    {
        private static readonly uint[] m_crc_table = InitializeTable();

        public static uint[] Table { get { return m_crc_table; } }

        private static uint[] InitializeTable ()
        {
            const uint polynomial = 0x04C11DB7;
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n << 24;
                for (int k = 0; k < 8; k++)
                {
                    if (0 != (c & 0x80000000u))
                        c = polynomial ^ (c << 1);
                    else
                        c <<= 1;
                }
                table[n] = c;
            }
            return table;
        }

        public static uint UpdateCrc (uint init_crc, byte[] data, int pos, int length)
        {
            uint c = init_crc;
            for (int n = 0; n < length; n++)
                c = m_crc_table[(c >> 24) ^ data[pos + n]] ^ (c << 8);
            return c;
        }

        public static uint Compute (byte[] data, int pos = 0, int length = 0)
        {
            if (length == 0) length = data.Length;
            return ~UpdateCrc (0xffffffff, data, pos, length);
        }

        public Crc32Normal() : base (0xffffffff) { }

        public override void Update (byte[] data, int pos = 0, int length = 0)
        {
            if (length == 0) length = data.Length;
            m_crc = UpdateCrc (m_crc, data, pos, length);
        }
    }

    /// <summary>
    /// Crc32 with reversed polynomial (for ZIP/PNG/Delta files)
    /// </summary>
    public class Crc32Reversed : Crc32Base
    {
        private static readonly uint[] m_crc_table = InitializeTable();

        public static uint[] Table { get { return m_crc_table; } }

        private static uint[] InitializeTable()
        {
            const uint polynomial = 0xEDB88320;
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    if ((c & 1) != 0)
                        c = polynomial ^ (c >> 1);
                    else
                        c >>= 1;
                }
                table[n] = c;
            }
            return table;
        }

        public static uint UpdateCrc (uint init_crc, byte[] data, int pos, int length)
        {
            if (length == 0) length = data.Length;
            uint c = init_crc;
            for (int n = 0; n < length; n++)
                c = m_crc_table[(c ^ data[pos + n]) & 0xFF] ^ (c >> 8);
            return c;
        }

        public static uint Compute (byte[] data, int pos = 0, int length = 0)
        {
            if (length == 0) length = data.Length;
            return ~UpdateCrc (0xffffffff, data, pos, length);
        }

        public Crc32Reversed() : base (0xffffffff) { }

        public override void Update (byte[] data, int pos = 0, int length = 0)
        {
            if (length == 0) length = data.Length;
            m_crc = UpdateCrc (m_crc, data, pos, length);
        }
    }
}