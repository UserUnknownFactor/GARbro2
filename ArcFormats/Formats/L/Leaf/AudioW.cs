using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Leaf
{
    [Export(typeof(AudioFormat))]
    public class WAudio : AudioFormat
    {
        public override string         Tag { get { return "W/Leaf"; } }
        public override string Description { get { return "Leaf PCM audio"; } }
        public override uint     Signature { get { return 0; } }

        public WAudio ()
        {
            Extensions = new string[] { "w" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x12);
            var format = new WaveFormat
            {
                FormatTag           = 1,
                Channels            = header[0],
                SamplesPerSecond    = header.ToUInt16 (2),
                AverageBytesPerSecond = header.ToUInt32 (6),
                BlockAlign          = header[1],
                BitsPerSample       = header.ToUInt16 (4),
            };
            uint pcm_size = header.ToUInt32 (0xA);
            if (0 == pcm_size || 0 == format.AverageBytesPerSecond || format.BitsPerSample < 8
                || 0 == format.Channels || format.Channels > 8
                || (format.BlockAlign * format.SamplesPerSecond != format.AverageBytesPerSecond)
                || (pcm_size + 0x12 != file.Length))
                return null;
            var pcm = new StreamRegion (file.AsStream, 0x12, pcm_size);
            return new RawPcmInput (pcm, format);
        }
    }
}
