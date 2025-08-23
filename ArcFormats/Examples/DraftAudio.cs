using System.ComponentModel.Composition;

namespace GameRes.Formats.Unknown
{
    [Export(typeof(AudioFormat))]
    public class xxxAudio : AudioFormat
    {
        public override string         Tag => "xxx";
        public override string Description => "Unknown audio resource";
        public override uint     Signature => 0;
        public override bool      CanWrite => false;

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var format = new WaveFormat {
                FormatTag = 1,
                Channels = 1,
                SamplesPerSecond = 44100,
                BlockAlign = 2,
                BitsPerSample = 16,
            };
            format.SetBPS();
            return new RawPcmInput (file.AsStream, format);
        }
    }
}
