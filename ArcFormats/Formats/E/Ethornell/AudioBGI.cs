using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.BGI
{
    [Export(typeof(AudioFormat))]
    public class BgiAudio : AudioFormat
    {
        public override string         Tag { get { return "BW"; } }
        public override string Description { get { return "BGI/Ethornell engine audio (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0; } }

        public BgiAudio ()
        {
            Signatures = new uint[] { 0x40, 0 };
            Extensions = new string[] { "bw", "", "_bw" };
        }
        
        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            if (!header.AsciiEqual (4, "bw  "))
                return null;
            uint offset = header.ToUInt32 (0);
            if (offset >= file.Length)
                return null;

            var input = new StreamRegion (file.AsStream, offset);
            return new OggInput (input);
            // input is left undisposed in case of exception.
        }
    }
}
