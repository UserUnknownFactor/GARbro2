using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Tama
{
    [Export(typeof(AudioFormat))]
    public class EsdAudio : AudioFormat
    {
        public override string         Tag { get { return "ESD"; } }
        public override string Description { get { return "TamaSoft ADV system audio"; } }
        public override uint     Signature { get { return 0x20445345; } } // 'ESD '

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            var format = new WaveFormat
            {
                FormatTag = 1,
                Channels = header.ToUInt16 (0x10),
                SamplesPerSecond = header.ToUInt32 (8),
                BitsPerSample = header.ToUInt16 (0xC),
            };
            format.BlockAlign = (ushort)(format.Channels * format.BitsPerSample / 8);
            format.AverageBytesPerSecond = format.SamplesPerSecond * format.BlockAlign;
            var pcm = new StreamRegion (file.AsStream, 0x20);
            return new RawPcmInput (pcm, format);
        }
    }
}
