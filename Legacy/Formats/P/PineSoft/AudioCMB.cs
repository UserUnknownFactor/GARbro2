using System.ComponentModel.Composition;

namespace GameRes.Formats.PineSoft
{
    [Export(typeof(AudioFormat))]
    public class CmbAudio : AudioFormat
    {
        public override string         Tag { get { return "CMB/PCM"; } }
        public override string Description { get { return "PineSoft PCM audio"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public CmbAudio ()
        {
            Extensions = new string[] { "" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            int data_size = header.ToInt32 (0);
            int header_size = header.ToInt32 (4);
            if (data_size + header_size + 8 != file.Length || header[8] != 1)
                return null;
            var format = new WaveFormat {
                FormatTag = header.ToUInt16 (8),
                Channels = header.ToUInt16 (0xA),
                SamplesPerSecond = header.ToUInt32 (0xC),
                AverageBytesPerSecond = header.ToUInt32 (0x10),
                BlockAlign = header.ToUInt16 (0x14),
                BitsPerSample = header.ToUInt16 (0x16),
            };
            if (format.Channels != 1 && format.Channels != 2)
                return null;
            var pcm = new StreamRegion (file.AsStream, 8 + header_size);
            return new RawPcmInput (pcm, format);
        }
    }
}
