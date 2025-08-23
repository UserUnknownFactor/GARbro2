using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Ffa
{
    [Export(typeof(AudioFormat))]
    public class Wa2Audio : AudioFormat
    {
        public override string         Tag { get { return "WA2"; } }
        public override string Description { get { return "FFA System PCM audio format"; } }
        public override uint     Signature { get { return 0x4D435041; } } // 'APCM'

        public override SoundInput TryOpen (IBinaryStream file)
        {
            return new Wa2Input (file);
        }
    }

    public class Wa2Input : SoundInput
    {
        public override string SourceFormat { get { return "raw"; } }

        public override int SourceBitrate
        {
            get { return (int)Format.AverageBytesPerSecond * 8; }
        }

        public Wa2Input (IBinaryStream file) : base (null)
        {
            var header = file.ReadHeader (0x2C);
            if (!header.AsciiEqual (8, "WAVEfmt "))
                throw new InvalidFormatException();

            var format = new WaveFormat();
            format.FormatTag                = header.ToUInt16 (0x14);
            format.Channels                 = header.ToUInt16 (0x16);
            format.SamplesPerSecond         = header.ToUInt32 (0x18);
            format.AverageBytesPerSecond    = header.ToUInt32 (0x1C);
            format.BlockAlign               = header.ToUInt16 (0x20);
            format.BitsPerSample            = header.ToUInt16 (0x22);
            format.ExtraSize                = 0;
            this.Format = format;

            uint pcm_size = header.ToUInt32 (0x28);
            var pcm = new byte[pcm_size];
            Decode (file.AsStream, pcm);
            Source = new MemoryStream (pcm);
            this.PcmSize = pcm_size;
            file.Dispose();
        }

        void Decode (Stream input, byte[] output)
        {
            int sample = 0;
            bool nibble_read = false;
            short v7 = 0x7F;
            int input_byte = 0;
            int dst = 0;
            while (dst < output.Length)
            {
                byte nibble;
                if (!nibble_read)
                {
                    input_byte = input.ReadByte();
                    if (-1 == input_byte)
                        break;
                    nibble = (byte)(input_byte >> 4);
                }
                else
                {
                    nibble = (byte)(input_byte & 0xF);
                }
                nibble_read = !nibble_read;
                int v11 = (ushort)v7;
                int diff = (short)(v11 * (byte)(2 * (nibble & 7) + 1) >> 3);
                if (0 != (nibble & 8))
                {
                    sample -= diff;
                    if (sample < -32768)
                        sample = -32768;
                }
                else
                {
                    sample += diff;
                    if (sample > 0x7FFF)
                        sample = 0x7FFF;
                }
                ushort v13 = (ushort)(v11 * Wa1Reader.SampleTable[nibble] >> 6);
                if (v13 > 0x7F)
                {
                    v7 = 0x6000;
                    if (v13 < 0x6001)
                        v7 = (short)v13;
                }
                else
                {
                    v7 = 0x7F;
                }
                LittleEndian.Pack ((ushort)sample, output, dst);
                dst += 2;
            }
        }

        #region IO.Stream methods
        public override long Position
        {
            get { return Source.Position; }
            set { Source.Position = value; }
        }

        public override bool CanSeek { get { return Source.CanSeek; } }

        public override long Seek (long offset, SeekOrigin origin)
        {
            return Source.Seek (offset, origin);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return Source.Read (buffer, offset, count);
        }

        public override int ReadByte ()
        {
            return Source.ReadByte();
        }
        #endregion
    }
}
