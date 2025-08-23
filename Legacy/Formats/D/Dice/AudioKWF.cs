using System;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Dice
{
    [Export(typeof(AudioFormat))]
    public class KwfAudio : AudioFormat
    {
        public override string         Tag { get { return "KWF"; } }
        public override string Description { get { return "DiceSystem audio format"; } }
        public override uint     Signature { get { return 0x3046574B; } } // 'KWF0'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x40);
            int method = header.ToInt32 (4);
            if (method != 3)
                throw new NotImplementedException();
            var format = new WaveFormat {
                FormatTag       = header.ToUInt16 (0x28),
                Channels        = header.ToUInt16 (0x2A),
                SamplesPerSecond = header.ToUInt32 (0x2C),
                AverageBytesPerSecond = header.ToUInt32 (0x30),
                BlockAlign      = header.ToUInt16 (0x34),
                BitsPerSample   = header.ToUInt16 (0x36),
            };
            var pcm = new StreamRegion (file.AsStream, 0x40);
            return new RawPcmInput (pcm, format);
        }
    }
}
