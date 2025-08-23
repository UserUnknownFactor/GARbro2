using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.AnotherRoom
{
    [Export(typeof(AudioFormat))]
    public class WazAudio : AudioFormat
    {
        public override string         Tag { get { return "WAZ"; } }
        public override string Description { get { return "LZSS-compressed WAV audio"; } }
        public override uint     Signature { get { return 0x464952FF; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            using (var lzss = new LzssStream (file.AsStream, LzssMode.Decompress, true))
            {
                var header = new byte[8];
                if (lzss.Read (header, 0, 8) != 8)
                    return null;
                int length = header.ToInt32 (4);
                var wav = new MemoryStream (length+8);
                wav.Write (header, 0, 8);
                lzss.CopyTo (wav);
                var bin = BinaryStream.FromStream (wav, file.Name);
                var sound = Wav.TryOpen (bin);
                if (sound != null)
                    file.Dispose();
                return sound;
            }
        }
    }
}
