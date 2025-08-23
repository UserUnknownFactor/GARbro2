using System.ComponentModel.Composition;

namespace GameRes.Formats.MyHarvest
{
    [Export(typeof(AudioFormat))]
    public class SedAudio : AudioFormat
    {
        public override string         Tag => "SED/HARVEST";
        public override string Description => "MyHarvest audio resource";
        public override uint     Signature => 0x14553; // 'SE'
        public override bool      CanWrite => false;

        public SedAudio ()
        {
            Signatures = new[] { 0x14553u, 0u };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            if (!header.AsciiEqual (0, "SE") || !header.AsciiEqual (0x12, "da"))
                return null;
            var format = new WaveFormat {
                FormatTag               = header.ToUInt16 (2),
                Channels                = header.ToUInt16 (4),
                SamplesPerSecond        = header.ToUInt32 (6),
                AverageBytesPerSecond   = header.ToUInt32 (0xA),
                BlockAlign              = header.ToUInt16 (0xE),
                BitsPerSample           = header.ToUInt16 (0x10),
            };
            uint pcm_size = header.ToUInt32 (0x14);
            var region = new StreamRegion (file.AsStream, 0x18, pcm_size);
            return new RawPcmInput (region, format);
        }
    }
}
