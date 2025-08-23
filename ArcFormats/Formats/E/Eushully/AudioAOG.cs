using System.ComponentModel.Composition;

namespace GameRes.Formats.Eushully
{
    [Export(typeof(AudioFormat))]
    public class AogAudio : AudioFormat
    {
        public override string         Tag { get { return "AOG/SYS3"; } }
        public override string Description { get { return "System3 engine audio format"; } }
        public override uint     Signature { get { return 0x47474F41; } } // 'AOGG'

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            if (!header.AsciiEqual (0x14, "OggS"))
                return null;
            var ogg = new StreamRegion (file.AsStream, 0x14);
            return new OggInput (ogg);
        }
    }
}
