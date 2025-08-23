using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.BeF
{
    [Export(typeof(AudioFormat))]
    public class VzyAudio : AudioFormat
    {
        public override string         Tag { get { return "VZY"; } }
        public override string Description { get { return "Obfuscated WAVE audio"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x12);
            if (!header.AsciiEqual ("\0\0\0\0"))
                return null;
            int riff_length = header.ToInt32 (4);
            if (file.Length != riff_length+8)
                return null;
            if (!header.AsciiEqual (8, "\0\0\0\0\0\0\0\0"))
                return null;
            int header_length = header.ToUInt16 (0x10);
            if (header_length < 0x10 || header_length > riff_length)
                return null;
            header = file.ReadHeader (0x18+header_length);
            if (!header.AsciiEqual (0x14+header_length, "data"))
                return null;
            var header_bytes = new byte[0x10] {
                (byte)'R', (byte)'I', (byte)'F', (byte)'F', header[4], header[5], header[6], header[7],
                (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' '
            };
            Stream riff = new StreamRegion (file.AsStream, 0x10);
            riff = new PrefixStream (header_bytes, riff);
            var wav = new BinaryStream (riff, file.Name);
            try
            {
                return Wav.TryOpen (wav);
            }
            catch
            {
                wav.Dispose();
                throw;
            }
        }
    }
}
