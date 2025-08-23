using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Selen
{
    [Export(typeof(AudioFormat))]
    public class EdimAudio : Mp3Audio
    {
        public override string         Tag { get { return "EDIM"; } }
        public override string Description { get { return "Macromedia Director audio format (MP3)"; } }
        public override uint     Signature { get { return 0x40010000; } }
        public override bool      CanWrite { get { return false; } }

        public EdimAudio ()
        {
            Signatures = new uint[] { 0x40010000, 0x64010000 };
        }
        
        public override SoundInput TryOpen (IBinaryStream file)
        {
            uint offset = 4 + Binary.BigEndian (file.Signature);
            var mp3 = new StreamRegion (file.AsStream, offset);
            return base.TryOpen (new BinaryStream (mp3, file.Name));
        }

        public override void Write (SoundInput source, Stream output)
        {
            throw new System.NotImplementedException ("EdimFormat.Write not implemenented");
        }
    }
}
