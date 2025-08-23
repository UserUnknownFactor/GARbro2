using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Ags32i
{
    [Export(typeof(AudioFormat))]
    public class AgsAudio : AudioFormat
    {
        public override string         Tag { get { return "WAV/AGS32I"; } }
        public override string Description { get { return "AGS32i engine encrypted wave audio"; } }
        public override uint     Signature { get { return 0x66424047; } }
        public override bool      CanWrite { get { return false; } }

        public AgsAudio ()
        {
            Signatures = new uint[] { 0x66424047, 0 } ;
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            uint key = file.Signature ^ 0x46464952u;
            if (0 == key)
                return null;
            Stream input = new InputCryptoStream (file.AsStream, new Ags32Transform (key));
            input = new SeekableStream (input);
            var header = new byte[12];
            input.Read (header, 0, 12);
            if (!header.AsciiEqual (8, "WAVE"))
                return null;
            input.Position = 0;
            return new WaveInput (input);
        }
    }
}
