using System;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Pan
{
    [Export(typeof(AudioFormat))]
    public class NsfAudio : AudioFormat
    {
        public override string         Tag { get { return "NSF"; } }
        public override string Description { get { return "Pan engine PCM audio"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public NsfAudio ()
        {
            Signatures = new uint[] { 0x010001, 0x020001, 0 };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("NSF"))
                return null;
            var header = file.ReadHeader (0x10);
            var format = new WaveFormat {
                FormatTag           = header.ToUInt16 (0),
                Channels            = header.ToUInt16 (2),
                SamplesPerSecond    = header.ToUInt32 (4),
                AverageBytesPerSecond = header.ToUInt16 (8),
                BlockAlign          = header.ToUInt16 (12),
                BitsPerSample       = header.ToUInt16 (14),
            };
            if (format.FormatTag != 1 ||
                format.SamplesPerSecond * format.Channels * format.BitsPerSample / 8 != format.AverageBytesPerSecond)
                return null;
            var pcm = new StreamRegion (file.AsStream, 0x10);
            return new RawPcmInput (pcm, format);
        }
    }
}
