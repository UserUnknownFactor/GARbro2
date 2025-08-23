using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.TechnoBrain
{
    [Export(typeof(AudioFormat))]
    public class WapeAudio : AudioFormat
    {
        public override string         Tag { get { return "WAPE"; } }
        public override string Description { get { return "TechnoBrain's compressed audio"; } }
        public override uint     Signature { get { return 0; } } // 'RIFF'
        public override bool      CanWrite { get { return false; } }

        public WapeAudio ()
        {
            Extensions = new string[] { "wav" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x2C);
            if (!header.AsciiEqual ("RIFF") || !header.AsciiEqual (8, "WAPEfmt ")
                || !header.AsciiEqual (0x24, "data"))
                return null;
            var reader = new WapeDecoder (file);
            var pcm = reader.ConvertToPcm();
            var format = new WaveFormat
            {
                FormatTag           = header.ToUInt16 (0x14),
                Channels            = header.ToUInt16 (0x16),
                SamplesPerSecond    = header.ToUInt32 (0x18),
                AverageBytesPerSecond = header.ToUInt32 (0x1C),
                BlockAlign          = header.ToUInt16 (0x20),
                BitsPerSample       = header.ToUInt16 (0x22),
            };
            file.Dispose();
            return new RawPcmInput (pcm, format);
        }
    }

    internal sealed class WapeDecoder
    {
        IBinaryStream   m_input;
        int             m_cur_bit;
        byte            m_cur_byte;

        public WapeDecoder (IBinaryStream input)
        {
            m_input = input;
        }

        public Stream ConvertToPcm ()
        {
            m_input.Position = 0x2C;
            m_cur_bit = 0;
            int pcm_size = m_input.ReadInt32();
            var pcm = new byte[pcm_size];
            int dst = 0;
            while (dst < pcm_size)
            {
                if (0 == GetBits (1))
                {
                    pcm[dst++] = (byte)GetBits (7);
                }
                else if (0 == GetBits (1))
                {
                    int sample = GetBits (2);
                    switch (sample)
                    {
                    case 0xC0:
                        sample = pcm[dst-1] + 4;
                        break;
                    case 0x80:
                        sample = pcm[dst-1] + 2;
                        break;
                    case 0x40:
                        sample = pcm[dst-1] - 2;
                        break;
                    case 0x00:
                        sample = pcm[dst-1] - 4;
                        break;
                    }
                    pcm[dst++] = (byte)sample;
                }
                else if (0 == GetBits (1))
                {
                    int sample = GetBits (2);
                    switch (sample)
                    {
                    case 0xC0:
                        sample = pcm[dst-1] + 8;
                        break;
                    case 0x80:
                        sample = pcm[dst-1] + 6;
                        break;
                    case 0x40:
                        sample = pcm[dst-1] - 6;
                        break;
                    case 0x00:
                        sample = pcm[dst-1] - 8;
                        break;
                    }
                    pcm[dst++] = (byte)sample;
                }
                else
                {
                    int count = GetBits (5) >> 3;
                    if (0x1F == count)
                        count = GetBits (8) + 0x1F;
                    ++count;
                    Binary.CopyOverlapped (pcm, dst - 1, dst, count);
                    dst += count;
                }
                if (0xFE == pcm[dst-1])
                    pcm[dst-1] = 0xFF;
            }
            return new MemoryStream (pcm);
        }

        static readonly byte[] mask_table = { 1, 2, 4, 8, 0x10, 0x20, 0x40, 0x80 };

        int GetBits (int count)
        {
            int v = 0;
            for (int i = 0; i < count; ++i)
            {
                if (--m_cur_bit < 0)
                {
                    m_cur_byte = m_input.ReadUInt8();
                    m_cur_bit = 7;
                }
                if (0 != (m_cur_byte & mask_table[m_cur_bit]))
                    v |= mask_table[7 - i];
            }
            return v;
        }
    }
}
