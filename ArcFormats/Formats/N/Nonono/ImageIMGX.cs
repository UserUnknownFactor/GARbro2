using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Nonono
{
    internal class ImgXDecoder : IImageDecoder
    {
        LsbBitStream        m_input;
        uint                m_unpacked_size;
        ImageMetaData       m_info;
        ImageData           m_image;

        public Stream            Source { get { return m_input.Input; } }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get { return m_info ?? GetImageInfo(); } }
        public ImageData          Image { get { return m_image ?? (m_image = GetImage()); } }

        public ImgXDecoder (IBinaryStream input)
        {
            input.Position = 4;
            uint unpacked_size = ~input.ReadUInt32();
            m_unpacked_size = unpacked_size >> 16 | unpacked_size << 16;
            m_input = new LsbBitStream (input.AsStream);
        }

        ImageData GetImage ()
        {
            var bitmap = Unpack();
            m_info = new ImageMetaData {
                Width  = bitmap.ToUInt32 (4),
                Height = bitmap.ToUInt32 (8),
                BPP = bitmap.ToUInt16 (0xE),
            };
            int header_size = bitmap.ToInt32 (0);
            BitmapPalette palette = null;
            int stride = m_info.iWidth * m_info.BPP / 8;
            var pixels = new byte[stride * m_info.iHeight];
            using (var input = new MemoryStream (bitmap, header_size, bitmap.Length - header_size))
            {
                if (8 == m_info.BPP)
                {
                    int num_colors = bitmap.ToInt32 (0x20);
                    if (0 == num_colors)
                        num_colors = 0x100;
                    palette = ImageFormat.ReadPalette (input, num_colors);
                }
                input.Read (pixels, 0, pixels.Length);
            }
            PixelFormat format = 8 == m_info.BPP ? PixelFormats.Indexed8
                               : 24 == m_info.BPP ? PixelFormats.Bgr24
                               : PixelFormats.Bgr32;
            return ImageData.Create (m_info, format, palette, pixels, stride);
        }

        ImageMetaData GetImageInfo ()
        {
            GetImage();
            return m_info;
        }

        struct Node
        {
            public int  Child;
            public byte Value;
        }

        public byte[] Unpack ()
        {
            var output = new byte[m_unpacked_size];
            m_input.Input.Position = 8;
            var tree = new Node[0x88CF];
            var buffer = new byte[0x88CF];
            int out_pos = 0;

            int root_node = 259;
            int code_length = 9;
            int last_code = m_input.GetBits (code_length);
            if (-1 == last_code || 256 == last_code)
                return output;
            byte last_symbol = output[out_pos++] = (byte)last_code;
            while (out_pos < output.Length)
            {
                int code;
                for (;;)
                {
                    code = m_input.GetBits (code_length);
                    if (-1 == code)
                        return output;
                    if (code != 257)
                        break;
                    ++code_length;
                }
                if (code == 256)
                    break;
                if (code == 258)
                {
                    root_node = 259;
                    code_length = 9;
                    last_code = m_input.GetBits (code_length);
                    if (-1 == last_code || 256 == last_code)
                        return output;
                    last_symbol = output[out_pos++] = (byte)last_code;
                    continue;
                }
                int symbol = code;
                int ptr = 0;
                if (code >= root_node)
                {
                    symbol = last_code;
                    buffer[ptr++] = last_symbol;
                }
                while (symbol > 255)
                {
                    buffer[ptr++] = tree[symbol].Value;
                    symbol = tree[symbol].Child;
                }
                last_symbol = buffer[ptr] = (byte)symbol;
                while (ptr >= 0)
                {
                    output[out_pos++] = buffer[ptr--];
                }
                tree[root_node].Child = last_code;
                tree[root_node].Value = last_symbol;
                ++root_node;
                last_code = code;
            }
            return output;
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
    }
}
