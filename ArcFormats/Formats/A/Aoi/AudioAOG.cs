using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Aoi
{
    [Export(typeof(AudioFormat))]
    public class AogAudio : AudioFormat
    {
        public override string         Tag { get { return "AOG"; } }
        public override string Description { get { return "Aoi engine audio format"; } }
        public override uint     Signature { get { return 0x4F696F41; } } // 'AoiO'

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x3C);
            if (!header.AsciiEqual (0, "AoiOgg"))
                return null;
            Stream ogg;
            if (header.AsciiEqual (0x2C, "OggS"))
                ogg = new StreamRegion (file.AsStream, 0x2C);
            else if (header.AsciiEqual (0xC, "Decode") && header.AsciiEqual (0x38, "OggS"))
                ogg = new StreamRegion (file.AsStream, 0x38);
            else
                return null;
            return new OggInput (ogg);
        }
    }
}
