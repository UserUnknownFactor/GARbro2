using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.C4
{
    [Export(typeof(AudioFormat))]
    public class VmdAudio : AudioFormat
    {
        public override string         Tag { get { return "VMD"; } }
        public override string Description { get { return "C4 engine MP3 audio"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        const byte Key = 0xE5;

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (3);
            if (0xFF != (header[0] ^ Key) || 0xE2 != ((header[1] ^ Key) & 0xE6) ||
                0xF0 == ((header[2] ^ Key) & 0xF0))
                return null;
            file.Position = 0;
            var input = new XoredStream (file.AsStream, Key);
            return new Mp3Input (input);
        }
    }
}
