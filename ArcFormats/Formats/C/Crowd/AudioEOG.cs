using System;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Crowd
{
    [Export(typeof(AudioFormat))]
    public class EogAudio : AudioFormat
    {
        public override string         Tag { get { return "EOG"; } }
        public override string Description { get { return "Crowd engine audio format (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0x004D5243; } } // 'CRM'

        public EogAudio ()
        {
            Extensions = new string[] { "eog", "amb" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var ogg = new StreamRegion (file.AsStream, 8);
            return new OggInput (ogg);
            // in case of exception ogg stream is left undisposed
        }

        public override void Write (SoundInput source, Stream output)
        {
            throw new System.NotImplementedException ("EogFormat.Write not implemenented");
        }
    }
}
