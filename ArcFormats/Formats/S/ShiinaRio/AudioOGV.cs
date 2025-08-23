using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.ShiinaRio
{
    [Export(typeof(AudioFormat))]
    public class OgvAudio : AudioFormat
    {
        public override string         Tag { get { return "OGV"; } }
        public override string Description { get { return "ShiinaRio audio format (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0x0056474f; } } // 'OGV'
        
        public override SoundInput TryOpen (IBinaryStream file)
        {
            file.Position = 0xc;
            var header = new byte[8];
            if (8 != file.Read (header, 0, 8))
                return null;
            if (!Binary.AsciiEqual (header, 0, "fmt "))
                return null;
            uint offset = LittleEndian.ToUInt32 (header, 4);
            file.Seek (offset, SeekOrigin.Current);
            if (8 != file.Read (header, 0, 8))
                return null;
            if (!Binary.AsciiEqual (header, 0, "data"))
                return null;

            var input = new StreamRegion (file.AsStream, file.Position);
            return new OggInput (input);
            // input is left undisposed in case of exception.
        }
    }
}
