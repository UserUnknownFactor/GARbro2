using System;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Sviu
{
    [Export(typeof(AudioFormat))]
    public class KogAudio : AudioFormat
    {
        public override string         Tag { get { return "KOG"; } }
        public override string Description { get { return "SVIU System audio format (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (file.Signature != 0)
                return null;
            var header = file.ReadHeader (8);
            int header_size = header.ToInt32 (4);
            file.Position = header_size;
            uint signature = file.ReadUInt32();
            if (signature != OggAudio.Instance.Signature)
                return null;
            var ogg = new StreamRegion (file.AsStream, header_size);
            return new OggInput (ogg);
        }
    }
}
