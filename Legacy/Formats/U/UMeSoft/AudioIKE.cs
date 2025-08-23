using System.ComponentModel.Composition;

namespace GameRes.Formats.UMeSoft
{
    [Export(typeof(AudioFormat))]
    public sealed class IkeAudio : AudioFormat
    {
        public override string         Tag { get { return "WAV/IKE"; } }
        public override string Description { get { return "ike-compressed WAVE audio"; } }
        public override uint     Signature { get { return 0x6B69899D; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x13);
            if (!header.AsciiEqual (2, "ike") || !header.AsciiEqual (0xF, "RIFF"))
                return null;
            int unpacked_size = IkeReader.DecodeSize (header[10], header[11], header[12]);
            var wav = IkeReader.CreateStream (file, unpacked_size);
            var sound = Wav.TryOpen (wav);
            if (sound != null)
                file.Dispose();
            else
                wav.Dispose();
            return sound;
        }
    }
}


