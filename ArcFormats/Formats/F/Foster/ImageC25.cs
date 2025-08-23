using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Foster
{
    /// <summary>
    /// ShiinaRio S25 predecessor.
    /// </summary>
    [Export(typeof(ImageFormat))]
    public class C25Format : C24Format
    {
        public override string         Tag { get { return "C25"; } }
        public override string Description { get { return "BeF game engine image format"; } }
        public override uint     Signature { get { return 0x00353243; } } // 'C25'
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (12);
            int count = header.ToInt32 (4);
            if (count <= 0)
                return null;
            return ReadMetaData (file, header.ToUInt32 (8), 32);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var reader = new C25Decoder (file, (C24MetaData)info, true))
                return reader.Image;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("C25Format.Write not implemented");
        }
    }

    internal class C25Decoder : CDecoderBase
    {
        public C25Decoder (IBinaryStream file, C24MetaData info, bool leave_open = false)
            : base (file, info, PixelFormats.Bgra32, leave_open)
        {
        }

        protected override void Unpack ()
        {
            var rows = ReadRows();
            int dst = 0;
            int width = (int)m_info.Width;
            foreach (uint row_offset in rows)
            {
                m_input.Position = row_offset;
                for (int x = 0; x < width; )
                {
                    int count = m_input.ReadUInt8();
                    if (count > 0x7F)
                    {
                        int bpp = 3;
                        count -= 0x80;
                        if (count >= 0x70)
                        {
                            bpp = 4;
                            count -= 0x70;
                        }
                        if (0 == count)
                            count = m_input.ReadUInt16();
                        for (int i = 0; i < count; ++i)
                        {
                            m_input.Read (m_output, dst, bpp);
                            dst += bpp;
                            if (3 == bpp)
                                m_output[dst++] = 0xFF;
                        }
                    }
                    else
                    {
                        if (0 == count)
                            count = m_input.ReadUInt16();
                        dst += count * 4;
                    }
                    x += count;
                }
            }
        }
    }
}
