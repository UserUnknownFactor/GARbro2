using System.ComponentModel.Composition;

namespace GameRes.Formats.Leaf
{
    [Export(typeof(AudioFormat))]
    public sealed class P16Audio : AudioFormat
    {
        public override string         Tag { get { return "P16"; } }
        public override string Description { get { return "Leaf PCM audio format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("P16"))
                return null;
            var format = new WaveFormat {
                FormatTag   = 1,
                Channels    = 1,
                SamplesPerSecond = 44100,
                AverageBytesPerSecond = 88200,
                BlockAlign  = 2,
                BitsPerSample = 16,
            };
            return new RawPcmInput (file.AsStream, format);
        }
    }
}
