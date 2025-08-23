using System.ComponentModel.Composition;

namespace GameRes.Formats.Uncanny
{
    [Export(typeof(AudioFormat))]
    public class CwvAudio : AudioFormat
    {
        public override string         Tag { get { return "CWV"; } }
        public override string Description { get { return "Uncanny encrypted WAV audio"; } }
        public override uint     Signature { get { return 0xA06F8BF7; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadBytes (0x10);
            Decrypt (header, 0, 0x10);
            if (!header.AsciiEqual (8, "WAVEfmt "))
                return null;
            file.Position = 0;
            var wave = file.ReadBytes ((int)file.Length);
            Decrypt (wave, 0, wave.Length);
            var input = new BinMemoryStream (wave);
            var sound = Wav.TryOpen (input);
            if (sound != null)
                file.Dispose();
            return sound;
        }

        static void Decrypt (byte[] data, int pos, int length)
        {
            uint key = 0x4B5AB4A5;
            for (int i = 0; i < length; ++i)
            {
                byte x = (byte)(key ^ data[pos+i]);
                data[pos+i] = x;
                key = ((key << 9) | (key >> 23) & 0x1F0) ^ x;
            }
        }
    }
}


