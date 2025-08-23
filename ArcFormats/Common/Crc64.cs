namespace GameRes.Utility
{
    public sealed class Crc64
    {
        const ulong Polynomial = 0x42F0E1EBA9EA3693ul;

        private static readonly ulong[] crc_table = InitializeTable (Polynomial);

        public static ulong[] Table { get { return crc_table; } }

        private static ulong[] InitializeTable (ulong poly)
        {
            var table = new ulong[256];
            for (uint i = 0; i < 256; ++i)
            {
                ulong crc = (ulong)i << 56;
                for (int j = 0; j < 8; ++j)
                {
                    if ((crc >> 63) != 0)
                        crc = (crc << 1) ^ poly;
                    else
                        crc <<= 1;
                }
                table[i] = crc;
            }
            return table;
        }
   
        public static ulong UpdateCrc (ulong crc, byte[] buf, int pos, int len)
        {
            for (int n = 0; n < len; n++)
                crc = crc_table[((crc >> 56) ^ buf[pos+n]) & 0xFF] ^ (crc << 8);
            return crc;
        }
   
        public static ulong Compute (byte[] buf, int pos, int len)
        {
            return ~UpdateCrc (~0ul, buf, pos, len);
        }

        private ulong m_crc = ~0ul;
        public  ulong Value { get { return ~m_crc; } }

        public void Update (byte[] buf, int pos, int len)
        {
            m_crc = UpdateCrc (m_crc, buf, pos, len);
        }
    }
}
