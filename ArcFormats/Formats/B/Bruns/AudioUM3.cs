using System;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats
{
    [Export(typeof(AudioFormat))]
    public class Um3Audio : AudioFormat
    {
        public override string         Tag { get { return "UM3"; } }
        public override string Description { get { return "UltraMarine3 audio format (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0xAC9898B0; } } // ~'OggS'

        public override SoundInput TryOpen (IBinaryStream file)
        {
            return new OggInput (new Um3Stream (file.AsStream));
        }
    }

    internal class Um3Stream : InputProxyStream
    {
        public Um3Stream (Stream main, bool leave_open = false)
            : base (main, leave_open)
        {
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            var pos = Position;
            int read = BaseStream.Read (buffer, offset, count);
            if (pos < 0x800 && read > 0)
            {
                int enc_count = Math.Min (0x800 - (int)pos, read);
                for (int i = 0; i < enc_count; ++i)
                {
                    buffer[offset+i] ^= 0xFF;
                }
            }
            return read;
        }

        public override int ReadByte ()
        {
            var pos = Position;
            int b = BaseStream.ReadByte();
            if (-1 != b && pos < 0x800)
                b ^= 0xFF;
            return b;
        }
    }
}
