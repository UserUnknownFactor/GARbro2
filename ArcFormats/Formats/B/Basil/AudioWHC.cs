using System.ComponentModel.Composition;

namespace GameRes.Formats.Basil
{
    [Export(typeof(AudioFormat))]
    public class WhcAudio : AudioFormat
    {
        public override string         Tag { get { return "WHC"; } }
        public override string Description { get { return "Basil audio resource"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public WhcAudio ()
        {
            Signatures = new uint[] { 0x020001, 0x010001 };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".whc"))
                return null;
            var header = file.ReadHeader (0x12);
            var format = new WaveFormat {
                FormatTag = header.ToUInt16 (0),
                Channels = header.ToUInt16 (2),
                SamplesPerSecond = header.ToUInt32 (4),
                AverageBytesPerSecond = header.ToUInt32 (8),
                BlockAlign = header.ToUInt16 (0xC),
                BitsPerSample = header.ToUInt16 (0xE),
            };
            if (format.FormatTag != 1 || format.Channels < 1 || format.Channels > 2)
                return null;
            var pcm = new StreamRegion (file.AsStream, 0x12);
            return new RawPcmInput (pcm, format);
        }
    }
}
