using System.ComponentModel.Composition;

namespace GameRes.Formats.Artel
{
    [Export(typeof(AudioFormat))]
    public class MuwAudio : AudioFormat
    {
        public override string         Tag { get { return "MUW"; } }
        public override string Description { get { return "Artel ADVG engine audio file"; } }
        public override uint     Signature { get { return 0x46464952; } } // 'RIFF'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x24);
            if (!header.AsciiEqual (0, "RIFF") || !header.AsciiEqual (8, "PCMWFMT "))
                return null;
            uint data_pos = header.ToUInt32 (0x10) + 0x14;
            file.Position = data_pos;
            if (file.ReadUInt32() != 0x61746164) // 'data'
                return null;
            uint data_size = file.ReadUInt32();
            var format = new WaveFormat {
                FormatTag = header.ToUInt16 (0x14),
                Channels = header.ToUInt16 (0x16),
                SamplesPerSecond = header.ToUInt32 (0x18),
                AverageBytesPerSecond = header.ToUInt32 (0x1C),
                BlockAlign = header.ToUInt16 (0x20),
                BitsPerSample = header.ToUInt16 (0x22),
            };
            var pcm = new StreamRegion (file.AsStream, data_pos+8, data_size);
            return new RawPcmInput (pcm, format);
        }
    }
}
