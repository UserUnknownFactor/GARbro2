using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.SysD
{
    [Export(typeof(AudioFormat))]
    public class DwvAudio : AudioFormat
    {
        public override string         Tag { get { return "DWV"; } }
        public override string Description { get { return "SYSD engine PCM audio"; } }
        public override uint     Signature { get { return 0x5744; } } // 'DW'
        public override bool      CanWrite { get { return false; } }

        public DwvAudio ()
        {
            Signatures = new uint[] { 0x5744, 0 };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x1C);
            if (!header.AsciiEqual ("DW") || header.ToUInt32 (4) != file.Length)
                return null;
            var format = new WaveFormat {
                FormatTag       = header.ToUInt16 (8),
                Channels        = header.ToUInt16 (0xA),
                SamplesPerSecond = header.ToUInt32 (0xC),
                AverageBytesPerSecond = header.ToUInt32 (0x10),
                BlockAlign      = header.ToUInt16 (0x14),
            };
            format.BitsPerSample = (ushort)(format.AverageBytesPerSecond * 8 / format.SamplesPerSecond / format.Channels);
            uint pcm_size = header.ToUInt32 (0x18);
            var pcm = new StreamRegion (file.AsStream, file.Position, pcm_size);
            return new RawPcmInput (pcm, format);
        }
    }
}
