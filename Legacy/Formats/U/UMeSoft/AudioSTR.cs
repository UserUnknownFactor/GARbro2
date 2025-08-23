using System.ComponentModel.Composition;

namespace GameRes.Formats.UMeSoft
{
    [Export(typeof(AudioFormat))]
    public sealed class WstrAudio : AudioFormat
    {
        public override string         Tag { get { return "STR"; } }
        public override string Description { get { return "U-Me Soft audio file"; } }
        public override uint     Signature { get { return 0x52545357; } } // 'WSTR'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            var format = new WaveFormat {
                FormatTag = 1,
                Channels = header.ToUInt16 (4),
                SamplesPerSecond = header.ToUInt32 (8),
                BitsPerSample = header.ToUInt16 (6),
            };
            format.BlockAlign = (ushort)(format.Channels * format.BitsPerSample / 8);
            format.SetBPS();
            var input = new StreamRegion (file.AsStream, 0x20);
            return new RawPcmInput (input, format);
        }
    }
}
