using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Brownie
{
    [Export(typeof(AudioFormat))]
    public class WavAudio : AudioFormat
    {
        public override string         Tag { get { return "WAV/BROWNIE"; } }
        public override string Description { get { return "Brownie obfuscated WAV file"; } }
        public override uint     Signature { get { return 0x58742367; } } // 'g#tX'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10).ToArray();
            header[0] = (byte)'R';
            header[1] = (byte)'I';
            header[2] = (byte)'F';
            header[3] = (byte)'F';
            for (int i = 4; i < 0x10; ++i)
                header[i] ^= 0x5C;
            if (!header.AsciiEqual (8, "WAVE"))
                return null;
            Stream input = new StreamRegion (file.AsStream, 0x10);
            input = new PrefixStream (header, input);
            return new WaveInput (input);
        }
    }
}
