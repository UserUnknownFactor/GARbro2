using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// [970525][Uran] High School Terra Story

namespace GameRes.Formats.Uran
{
    public class DarMetaData : ImageMetaData
    {
        public byte Version;
        public int  FrameCount;
        public long FrameOffset;
        public int  RowSize;
    }

    [Export(typeof(ImageFormat))]
    public class DarFormat : ImageFormat
    {
        public override string         Tag { get { return "DAR"; } }
        public override string Description { get { return "Uran image format"; } }
        public override uint     Signature { get { return 0x3A524144; } } // 'DAR:'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (12);
            byte version = header[5];
            int frame_count = header.ToUInt16 (6);
            if (header[4] != '8' || version > 1 || frame_count < 1)
                return null;
            file.Position = 0x40C;
            int header_size = 8;
            if (version != 0)
                header_size = file.ReadUInt16();
            long frame_offset = file.Position;
            ushort width    = file.ReadUInt16(); // +0
            ushort height   = file.ReadUInt16(); // +1
            ushort row_size = file.ReadUInt16(); // +2
            file.ReadUInt16(); // +3
            int bpp = 8;
            if (header_size >= 14)
            {
                file.ReadInt16(); // +4
                file.ReadInt16(); // +5
                file.ReadUInt16(); // +6 height
                if (header_size >= 16)
                {
                    int count = file.ReadUInt8(); // [14]
                    if (count + 18 <= header_size)
                    {
                        file.Seek (count - 1, SeekOrigin.Current);
                        bpp = file.ReadUInt8(); // [count+14]
                    }
                }
            }
            return new DarMetaData {
                Width = width,
                Height = height,
                BPP = bpp,
                Version = version,
                FrameCount = frame_count,
                FrameOffset = frame_offset + header_size,
                RowSize = row_size,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new DarReader (file, (DarMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DarFormat.Write not implemented");
        }
    }

    internal class DarReader
    {
        IBinaryStream   m_input;
        DarMetaData     m_info;

        public ImageMetaData    Info { get { return m_info; } }
        public BitmapPalette Palette { get; private set; }

        public DarReader (IBinaryStream input, DarMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        public ImageData Unpack ()
        {
            if (8 == m_info.BPP)
            {
                m_input.Position = 0x0C;
                Palette = ImageFormat.ReadPalette (m_input.AsStream);
            }
            int depth = m_info.BPP / 8;
            long row_pos = m_info.FrameOffset;
            int stride = m_info.iWidth * depth;
            var pixels = new byte[stride * m_info.iHeight];
            int dst = pixels.Length - stride;
            while (dst >= 0)
            {
                m_input.Position = row_pos;
                int x = m_input.ReadInt16();
                int row_length = m_input.ReadUInt16();
                if (row_length != 0)
                    m_input.Read (pixels, dst + x, row_length);
                row_pos += m_info.RowSize;
                dst -= stride;
            }
            PixelFormat format = depth == 1 ? PixelFormats.Indexed8 : PixelFormats.Bgr24;
            return ImageData.Create (m_info, format, Palette, pixels, stride);
        }
    }
}
