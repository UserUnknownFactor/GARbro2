using System.ComponentModel.Composition;
using System.IO;
using System.Security.Cryptography;

namespace GameRes.Formats.RealLive
{
    [Export(typeof(AudioFormat))]
    public class OwpAudio : AudioFormat
    {
        public override string         Tag { get { return "OWP"; } }
        public override string Description { get { return "RealLive engine obfuscated OGG audio"; } }
        public override uint     Signature { get { return 0x6A5E5E76; } } // 'OggS' ^ 0x39

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var input = new XoredStream (file.AsStream, 0x39);
            return new OggInput (input);
        }
    }
}
