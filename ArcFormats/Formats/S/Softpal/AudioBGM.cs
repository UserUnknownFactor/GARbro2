using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Softpal
{
    [Export(typeof(AudioFormat))]
    public class BgmAudio : AudioFormat
    {
        public override string         Tag { get { return "BGM/SOFTPAL"; } }
        public override string Description { get { return "Softpal BGM format (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0x204D4742; } } // 'BGM '

        public BgmAudio ()
        {
            Extensions = new string[] { "ogg" };
        }
        
        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10); // header contains music loop timing
            if (!header.AsciiEqual (0xC, "OggS"))
                return null;
            var input = new StreamRegion (file.AsStream, 12);
            return new OggInput (input);
            // input is [intentionally] left undisposed in case of exception.
        }
    }
}
