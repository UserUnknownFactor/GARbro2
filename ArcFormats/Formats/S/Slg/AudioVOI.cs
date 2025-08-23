using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Slg
{
    [Export(typeof(AudioFormat))]
    public class VoiAudio : AudioFormat
    {
        public override string         Tag { get { return "VOI"; } }
        public override string Description { get { return "SLG system obfuscated Ogg audio"; } }
        public override uint     Signature { get { return 0; } } // 'OggS'

        public override SoundInput TryOpen (IBinaryStream file)
        {
            file.Position = 0x1E;
            int offset = file.ReadByte();
            if (offset <= 0)
                return null;
            file.Position = 0x20 + offset;
            if (!(file.ReadByte() == 'O' && file.ReadByte() == 'g' &&
                  file.ReadByte() == 'g' && file.ReadByte() == 'S'))
                return null;
            return new OggInput (new StreamRegion (file.AsStream, 0x20+offset));
        }
    }
}
