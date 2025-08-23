using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Ikura
{
    [Export(typeof(ImageFormat))]
    public class GgsFormat : ImageFormat
    {
        public override string         Tag { get { return "GGS"; } }
        public override string Description { get { return "D.O. image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".ggs"))
                return null;
            int x = file.ReadInt16();
            int y = file.ReadInt16();
            uint w = file.ReadUInt16();
            uint h = file.ReadUInt16();
            if (0 == w || 0 == h || w > 0x4000 || h > 0x4000 || x < 0 || y < 0)
                return null;
            return new ImageMetaData { Width = w, Height = h, OffsetX = x, OffsetY = y, BPP = 24 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GgsReader (file, info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GgsFormat.Write not implemented");
        }
    }

    internal class GgsReader
    {
        IBinaryStream       m_input;
        byte[]              m_output;

        public PixelFormat Format { get { return PixelFormats.Bgr24; } }

        public GgsReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_output = new byte[3 * info.Width * info.Height];
        }

        public byte[] Unpack ()
        {
            m_input.Position = 8;
            for (int channel = 0; channel < 3; ++channel)
            {
                int dst = channel;
                while (dst < m_output.Length)
                {
                    int count;
                    byte ctl = m_input.ReadUInt8();
                    if (0 == ctl--)
                    {
                        count = m_input.ReadUInt8();
                        byte v = m_input.ReadUInt8();
                        while (count --> 0)
                        {
                            m_output[dst] = v;
                            dst += 3;
                        }
                    }
                    else if (0 == ctl--)
                    {
                        count = m_input.ReadUInt8();
                        int offset = m_input.ReadUInt8();
                        while (count --> 0)
                        {
                            m_output[dst] = m_output[dst-offset];
                            dst += 3;
                        }
                    }
                    else if (0 == ctl--)
                    {
                        count = m_input.ReadUInt8();
                        int offset = m_input.ReadUInt16();
                        while (count --> 0)
                        {
                            m_output[dst] = m_output[dst-offset];
                            dst += 3;
                        }
                    }
                    else if (0 == ctl--)
                    {
                        dst += m_input.ReadUInt8();
                    }
                    else if (0 == ctl)
                    {
                        dst += m_input.ReadUInt16();
                    }
                    else
                    {
                        count = ctl;
                        while (count --> 0)
                        {
                            m_output[dst] = m_input.ReadUInt8();
                            dst += 3;
                        }
                    }
                }
            }
            return m_output;
        }
    }
}
