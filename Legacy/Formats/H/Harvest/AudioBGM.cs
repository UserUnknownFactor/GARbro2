using System.ComponentModel.Composition;

namespace GameRes.Formats.MyHarvest
{
    [Export(typeof(AudioFormat))]
    public class BgmAudio : AudioFormat
    {
        public override string         Tag => "BGM/HARVEST";
        public override string Description => "MyHarvest audio resource";
        public override uint     Signature => 0x304D4742; // 'BMG0'
        public override bool      CanWrite => false;

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x1C);
            if (!header.AsciiEqual (0x14, "dar\0"))
                return null;
            var format = new WaveFormat {
                FormatTag               = header.ToUInt16 (4),
                Channels                = header.ToUInt16 (6),
                SamplesPerSecond        = header.ToUInt32 (8),
                AverageBytesPerSecond   = header.ToUInt32 (0xC),
                BlockAlign              = header.ToUInt16 (0x10),
                BitsPerSample           = header.ToUInt16 (0x12),
            };
            uint pcm_size = header.ToUInt32 (0x18);
            var region = new StreamRegion (file.AsStream, 0x1C, pcm_size);
            return new RawPcmInput (region, format);
        }
    }
}
