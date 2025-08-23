using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Unknown
{
    [Export(typeof(ImageFormat))]
    public class TgxFormat : ImageFormat
    {
        public override string         Tag { get { return "TGX"; } }
        public override string Description { get { return "TGX Image Format"; } }
        public override uint     Signature { get { return  0; } }

        public TgxFormat()
        {
            Extensions = new string[] { "tgx" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (16);
            if (!header.AsciiEqual (0, "TGX"))
                return null;

            return new ImageMetaData
            {
                Width   = header.ToUInt32 (4),
                Height  = header.ToUInt32 (8),
                BPP     = header.ToInt32 (12),
                OffsetX = 0,
                OffsetY = 0
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new TgxReader (file, info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("TGX writing not supported");
        }

        internal class TgxReader
        {
            IBinaryStream m_input;
            ImageMetaData m_info;
            byte[] m_output;

            public TgxReader (IBinaryStream input, ImageMetaData info)
            {
                m_input = input;
                m_info = info;
            }

            public ImageData Unpack()
            {
                m_input.Position = 16;

                int stride = (int)m_info.Width * 4;
                m_output = new byte[stride * (int)m_info.Height];

                // Simple RLE decompression
                int dst = 0;
                while (dst < m_output.Length)
                {
                    byte cmd = m_input.ReadUInt8();

                    if ((cmd & 0x80) != 0)
                    {
                        // Repeat
                        int count = (cmd & 0x7F) + 1;
                        byte b = m_input.ReadUInt8();
                        byte g = m_input.ReadUInt8();
                        byte r = m_input.ReadUInt8();
                        byte a = m_input.ReadUInt8();

                        for (int i = 0; i < count && dst < m_output.Length; ++i)
                        {
                            m_output[dst++] = b;
                            m_output[dst++] = g;
                            m_output[dst++] = r;
                            m_output[dst++] = a;
                        }
                    }
                    else
                    {
                        // Copy
                        int count = cmd + 1;
                        for (int i = 0; i < count && dst < m_output.Length; ++i)
                        {
                            m_output[dst++] = m_input.ReadUInt8();
                            m_output[dst++] = m_input.ReadUInt8();
                            m_output[dst++] = m_input.ReadUInt8();
                            m_output[dst++] = m_input.ReadUInt8();
                        }
                    }
                }

                return ImageData.Create (m_info, PixelFormats.Bgra32, null, m_output);
            }
        }
    }
}
