using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Foster
{
    internal class C24MetaData : ImageMetaData
    {
        public uint DataOffset;
    }

    /// <summary>
    /// ShiinaRio S25 predecessor.
    /// </summary>
    [Export(typeof(ImageFormat))]
    public class C24Format : ImageFormat
    {
        public override string         Tag { get { return "C24"; } }
        public override string Description { get { return "Foster game engine image format"; } }
        public override uint     Signature { get { return 0x00343243; } } // 'C24'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (12);
            int count = header.ToInt32 (4);
            if (count <= 0)
                return null;
            return ReadMetaData (file, header.ToUInt32 (8), 24);
        }

        internal C24MetaData ReadMetaData (IBinaryStream file, long offset, int bpp)
        {
            file.Position = offset;
            var info = new C24MetaData { BPP = bpp };
            info.Width = file.ReadUInt32();
            info.Height = file.ReadUInt32();
            info.OffsetX = file.ReadInt32();
            info.OffsetY = file.ReadInt32();
            info.DataOffset = (uint)file.Position;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var reader = new C24Decoder (file, (C24MetaData)info, true))
                return reader.Image;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("C24Format.Write not implemented");
        }
    }

    internal abstract class CDecoderBase : IImageDecoder
    {
        protected IBinaryStream m_input;
        protected C24MetaData   m_info;
        protected byte[]        m_output;
        private   ImageData     m_image;
        private   bool          m_should_dispose;

        public Stream            Source { get { m_input.Position = 0; return m_input.AsStream; } }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get { return m_info; } }
        public PixelFormat       Format { get; private set; }

        public ImageData Image
        {
            get
            {
                if (null == m_image)
                {
                    Unpack();
                    m_image = ImageData.Create (m_info, Format, null, m_output);
                }
                return m_image;
            }
        }

        public CDecoderBase (IBinaryStream file, C24MetaData info, PixelFormat format, bool leave_open = false)
        {
            m_input = file;
            m_info = info;
            m_output = new byte[(info.BPP / 8) * (int)m_info.Width * (int)m_info.Height];
            m_should_dispose = !leave_open;
            Format = format;
        }

        protected uint[] ReadRows ()
        {
            m_input.Position = m_info.DataOffset;
            var rows = new uint[m_info.Height];
            for (int i = 0; i < rows.Length; ++i)
                rows[i] = m_input.ReadUInt32();
            return rows;
        }

        protected abstract void Unpack ();

        #region IDisposable Members
        bool m_disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing && m_should_dispose)
                {
                    m_input.Dispose();
                }
                m_disposed = true;
            }
        }
        #endregion
    }

    internal sealed class C24Decoder : CDecoderBase
    {
        public C24Decoder (IBinaryStream file, C24MetaData info, bool leave_open = false)
            : base (file, info, PixelFormats.Bgr24, leave_open)
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
                bool rle = false;
                for (int x = 0; x < width; )
                {
                    int count = m_input.ReadUInt8();
                    if (!rle)
                    {
                        if (0xFF == count)
                            count = m_input.ReadUInt16();
                        int byte_count = count * 3;
                        for (int i = 0; i < byte_count; ++i)
                            m_output[dst++] = 0xFF;
                    }
                    else
                    {
                        if (0 == count)
                            count = m_input.ReadUInt16();
                        int byte_count = count * 3;
                        m_input.Read (m_output, dst, byte_count);
                        dst += byte_count;
                    }
                    x += count;
                    rle = !rle;
                }
            }
        }
    }
}
