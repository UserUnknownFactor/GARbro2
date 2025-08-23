using System.ComponentModel.Composition;

namespace GameRes.Formats.Regrips
{
    [Export(typeof(AudioFormat))]
    public class WrgAudio : AudioFormat
    {
        public override string         Tag { get { return "WRG/AUDIO"; } }
        public override string Description { get { return "Regrips encrypted WAVE file"; } }
        public override uint     Signature { get { return  0xB9B9B6AD; } } // XOR'ed "RIFF"
        public override bool      CanWrite { get { return  false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var input = new XoredStream (file.AsStream, 0xFF);
            return Wav.TryOpen (new BinaryStream (input, file.Name));
        }
    }

    [Export(typeof(AudioFormat))]
    public class MrgAudio : AudioFormat
    {
        public override string         Tag { get { return "MRG/AUDIO"; } }
        public override string Description { get { return "Regrips encrypted MP3 file"; } }
        public override uint     Signature { get { return  0; } }
        public override bool      CanWrite { get { return  false; } }

        static readonly ResourceInstance<AudioFormat> Mp3Format = new ResourceInstance<AudioFormat> ("MP3");

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (4);
            if (header.Length < 4 || file.Signature == 0x0047524D)
                return null;

            // Check MP3 signatures:
            bool isXoredMp3 = false;
            // ID3v2: "ID3" (0x494433) XOR 0xFF = 0xB6DBCC
            if (header[0] == 0xB6 && header[1] == 0xDB && header[2] == 0xCC)
                isXoredMp3 = true;
            // MPEG sync: 0xFFF* or 0xFFE* XOR 0xFF = 0x000* or 0x001*
            else if (header[0] == 0x00 && (header[1] & 0xE0) == 0x00)
            {
                byte xoredByte = (byte)(header[1] ^ 0xFF);
                if ((xoredByte & 0xE0) == 0xE0) // Should be 111xxxxx after XOR back
                    isXoredMp3 = true;
            }

            if (!isXoredMp3)
                return null;

            file.Position = 0;
            var input = new XoredStream (file.AsStream, 0xFF);
            return Mp3Format.Value.TryOpen (new BinaryStream (input, file.Name));
        }
    }
}