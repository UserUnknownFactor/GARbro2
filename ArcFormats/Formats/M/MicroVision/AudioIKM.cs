using System.ComponentModel.Composition;

namespace GameRes.Formats.MicroVision
{
    [Export(typeof(AudioFormat))]
    public class IkmAudio : AudioFormat
    {
        public override string         Tag { get { return "IKM"; } }
        public override string Description { get { return "MicroVision audio format (Ogg/vorbis)"; } }
        public override uint     Signature { get { return 0x004D4B49; } } // 'IKM'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x40);
            uint length = header.ToUInt32 (0x24);
            var ogg = new StreamRegion (file.AsStream, file.Length-length, length);
            return new OggInput (ogg);
        }
    }
}
