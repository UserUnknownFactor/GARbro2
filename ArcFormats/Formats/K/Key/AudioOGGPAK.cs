using System.ComponentModel.Composition;

namespace GameRes.Formats.Key
{
    [Export(typeof(AudioFormat))]
    public class OggPakAudio : AudioFormat
    {
        public override string         Tag { get { return "OGGPAK"; } }
        public override string Description { get { return "Key audio resource"; } }
        public override uint     Signature { get { return 0x5047474F; } } // 'OGGPAK'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0xF);
            if (!header.AsciiEqual ("OGGPAK"))
                return null;
            uint length = header.ToUInt32 (0xB);
            var input = new StreamRegion (file.AsStream, 0xF, length);
            return new OggInput (input);
        }
    }
}
