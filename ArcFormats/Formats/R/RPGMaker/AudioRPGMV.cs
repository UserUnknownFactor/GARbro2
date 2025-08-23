using System.ComponentModel.Composition;

namespace GameRes.Formats.RPGMaker
{
    [Export(typeof(AudioFormat))]
    public sealed class RpgmvoAudio : AudioFormat
    {
        public override string         Tag { get { return "RPGMVO"; } }
        public override string Description { get { return "RPG Maker MV/MZ audio format (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0x4D475052; } } // 'RPGMV'
        public override bool      CanWrite { get { return false; } }

        public RpgmvoAudio ()
        {
            Extensions = new[] { "rpgmvo", "ogg_" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            if (header[4] != 'V')
                return null;

            var key = RpgmvDecryptor.LastKey ?? RpgmvDecryptor.FindKeyFor (file.Name);
            if (null == key)
                return null;

            for (int i = 0; i < 4; ++i)
                header[0x10+i] ^= key[i];
            if (!header.AsciiEqual (0x10, "OggS"))
            {
                RpgmvDecryptor.LastKey = null;
                return null;
            }

            RpgmvDecryptor.LastKey = key;
            var ogg = RpgmvDecryptor.DecryptStream (file, key);
            return OggAudio.Instance.TryOpen (ogg);
        }
    }
}
