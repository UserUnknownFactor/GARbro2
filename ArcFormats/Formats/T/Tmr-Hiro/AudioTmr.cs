using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.TmrHiro
{
    [Export(typeof(AudioFormat))]
    public class TmrHiroAudio : AudioFormat
    {
        public override string         Tag { get { return "WAV/TMR-HIRO"; } }
        public override string Description { get { return "Tmr-Hiro wave audio"; } }
        public override uint     Signature { get { return 0; } }

        public TmrHiroAudio ()
        {
            Extensions = new string[] { "" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (file.ReadByte() != 0x44)
                return null;
            file.Position = 4;
            if (file.ReadByte() != 0)
                return null;
            int length = file.ReadInt32();
            if (length != file.Length - 9)
                return null;
            var format = new WaveFormat
            {
                FormatTag                = 1,
                Channels                 = 2,
                SamplesPerSecond         = 44100,
                BitsPerSample            = 16,
                BlockAlign               = 4,
                AverageBytesPerSecond    = 44100*4,
            };
            var pcm = new StreamRegion (file.AsStream, 9, length);
            return new RawPcmInput (pcm, format);
        }
    }
}
