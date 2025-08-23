using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Unknown
{
    [Export(typeof(AudioFormat))]
    public class MsfAudio : AudioFormat
    {
        public override string         Tag { get { return "MSF"; } }
        public override string Description { get { return "'Unknown' PCM audio format"; } }
        public override uint     Signature { get { return 0x2046534D; } } // 'MSF '
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadBytes (0x30);
            ushort key = (ushort)(101 * header.ToUInt16 (4) + 778);
            for (int i = 0; i < header.Length; i += 2)
            {
                header[i  ] ^= (byte)key;
                header[i+1] ^= (byte)(key >> 8);
            }
            var format = new WaveFormat {
                FormatTag       = header.ToUInt16 (0x1C),
                Channels        = header.ToUInt16 (0x1E),
                SamplesPerSecond = header.ToUInt32 (0x20),
                AverageBytesPerSecond = header.ToUInt32 (0x24),
                BlockAlign      = header.ToUInt16 (0x28),
                BitsPerSample   = header.ToUInt16 (0x2A),
            };
            uint pcm_size = header.ToUInt32 (0x18);
            var pcm = new StreamRegion (file.AsStream, 0x30, pcm_size);
            return new RawPcmInput (pcm, format);
        }
    }
}
