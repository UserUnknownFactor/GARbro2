using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

// [031219][Project-μ] Gin no Hebi Kuro no Tsuki
// [040528][Lakshmi] Mabuta Tojireba Soko ni...

namespace GameRes.Formats.ProjectMu
{
    [Export(typeof(ImageFormat))]
    public class GamFormat : ImageFormat
    {
        public override string         Tag { get { return "GAM"; } }
        public override string Description { get { return "Project-μ image format"; } }
        public override uint     Signature { get { return 0x4D4147; } } // 'GAM'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            using (var input = OpenGamStream (file))
                return Bmp.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = OpenGamStream (file))
                return Bmp.Read (input, info);
        }

        IBinaryStream OpenGamStream (IBinaryStream input)
        {
            input.Position = 8;
            var unpacked = new PackedStream<GamDecompressor> (input.AsStream, true);
            return new BinaryStream (unpacked, input.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GamFormat.Write not implemented");
        }
    }

    internal class GamDecompressor : Decompressor
    {
        Stream      m_input;

        public override void Initialize (Stream input)
        {
            m_input = input;
        }

        protected override IEnumerator<int> Unpack ()
        {
            var frame = new byte[0x100];
            int frame_pos = 0;
            int bits = 1;
            for (;;)
            {
                if (1 == bits)
                {
                    int b = m_input.ReadByte();
                    if (-1 == b)
                        yield break;
                    bits = b;
                    b = m_input.ReadByte();
                    if (-1 == b)
                        yield break;
                    bits |= b << 8 | 0x10000;
                }
                if (0 == (bits & 1))
                {
                    int b = m_input.ReadByte();
                    if (-1 == b)
                        yield break;
                    frame[frame_pos++ & 0xFF] = (byte)b;
                    m_buffer[m_pos++] = (byte)b;
                    if (0 == --m_length)
                        yield return m_pos;
                }
                else
                {
                    int offset = m_input.ReadByte();
                    if (-1 == offset)
                        yield break;
                    int count = m_input.ReadByte();
                    if (-1 == count)
                        yield break;
                    while (count --> 0)
                    {
                        byte v = frame[(frame_pos - offset) & 0xFF];
                        frame[frame_pos++ & 0xFF] = v;
                        m_buffer[m_pos++] = v;
                        if (0 == --m_length)
                            yield return m_pos;
                    }
                }
                bits >>= 1;
            }
        }
    }
}
