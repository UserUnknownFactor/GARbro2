using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.BlackCyc
{
    [Export(typeof(AudioFormat))]
    public class VawAudio : AudioFormat
    {
        public override string         Tag { get { return "VAW"; } }
        public override string Description { get { return "Black Cyc audio format"; } }
        public override uint     Signature { get { return  0; } }

        private const int header_end = 0x40;

        public VawAudio ()
        {
            Extensions = new string[] { "vaw", "wgq" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = ResourceHeader.Read (file);
            if (null == header)
                return null;

            AudioFormat format;
            int offset;
            switch (header.PackType) {
            case 0:
                if (4 != file.Read (header.Bytes, 0, 4))
                    return null;
                if (!Binary.AsciiEqual (header.Bytes, "RIFF"))
                    return null;
                format = Wav;
                offset = header_end;
                break;
            case 1:
                return Unpack (file);
            case 2:
                file.Seek(0x6C, SeekOrigin.Begin);
                format = OggAudio.Instance;
                offset = ((byte)'O' == file.ReadByte()) ? 0x6C : 0x6E;
                break;
            case 6: 
                if (!Binary.AsciiEqual (header.Bytes, 0x10, "OGG ")) 
                    return null;
                format = OggAudio.Instance;
                offset = header_end;
                break;
            default:
                return null;
            }

            var input = new StreamRegion (file.AsStream, offset, file.Length-offset);
            return format.TryOpen (new BinaryStream (input, file.Name));
        }

        public override void Write (SoundInput source, Stream output)
        {
            throw new System.NotImplementedException ("EdimFormat.Write not implemenented");
        }

        SoundInput Unpack (IBinaryStream input)
        {
            input.Position = header_end;
            var header = new byte[0x24];
            if (0x14 != input.Read (header, 0, 0x14))
                return null;

            int fmt_size = LittleEndian.ToInt32 (header, 0x10);
            if (fmt_size + input.Position > input.Length)
                return null;

            int header_size = fmt_size + 0x14;
            if (header_size > header.Length)
                Array.Resize (ref header, header_size);
            if (fmt_size != input.Read (header, 0x14, fmt_size))
                return null;

            int riff_size = LittleEndian.ToInt32 (header, 4) + 8;
            int data_size = riff_size - header_size;
            var pcm = new MemoryStream (riff_size);
            try
            {
                pcm.Write (header, 0, header_size);
                using (var output = new BinaryWriter (pcm, Encoding.Default, true))
                using (var bits = new LsbBitStream (input.AsStream, true))
                {
                    int written = 0;
                    short sample = 0;
                    while (written < data_size)
                    {
                        int c = bits.GetBits (4);
                        if (-1 == c)
                            c = 0;
                        int code = 0;
                        if (c > 0)
                            code = bits.GetBits (c) << (32 - c);
                        code >>= 32 - c;
                        int sign = code >> 31;
                        code ^= 0x4000 >> (15 - c);
                        code -= sign;
                        sample += (short)code;
                        output.Write (sample);
                        written += 2;
                    }
                }
                pcm.Position = 0;
                var sound = Wav.TryOpen (new BinMemoryStream (pcm, input.Name));
                if (sound != null)
                    input.Dispose();
                else
                    pcm.Dispose();
                return sound;
            }
            catch
            {
                pcm.Dispose();
                throw;
            }
        }
    }
}
