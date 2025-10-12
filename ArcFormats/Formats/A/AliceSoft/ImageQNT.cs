using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.AliceSoft
{
    public class QntMetaData : ImageMetaData
    {
        public uint RGBSize;
        public uint AlphaSize;
        public int  HeaderSize;
    }

    [Export(typeof(ImageFormat))]
    public class QntFormat : ImageFormat
    {
        public override string         Tag { get { return "QNT"; } }
        public override string Description { get { return "AliceSoft System image format"; } }
        public override uint     Signature { get { return  0x544e51; } } // 'QNT'
        public override bool      CanWrite { get { return  true; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x30);
            int version = header.ToInt32 (4);
            if (version < 0 || version > 2)
                return null;
            if (0 == version)
            {
                return new QntMetaData {
                    Width      = header.ToUInt32 (0x10),
                    Height     = header.ToUInt32 (0x14),
                    OffsetX    = header.ToInt32  (0x08),
                    OffsetY    = header.ToInt32  (0x0C),
                    BPP        = header.ToInt32  (0x18),
                    RGBSize    = header.ToUInt32 (0x20),
                    AlphaSize  = header.ToUInt32 (0x24),
                    HeaderSize = 0x30,
                };
            }
            int header_size = header.ToInt32 (8);
            uint width = header.ToUInt32 (0x14);
            uint height = header.ToUInt32 (0x18);
            if (0 == width || 0 == height)
                return null;
            return new QntMetaData {
                Width      = width,
                Height     = height,
                OffsetX    = header.ToInt32  (0x0c),
                OffsetY    = header.ToInt32  (0x10),
                BPP        = header.ToInt32  (0x1c),
                RGBSize    = header.ToUInt32 (0x24),
                AlphaSize  = header.ToUInt32 (0x28),
                HeaderSize = header_size,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = new Reader (stream.AsStream, (QntMetaData)info);
            reader.Unpack();
            int stride = (int)info.Width * (reader.BPP / 8);
            PixelFormat format = 24 == reader.BPP ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
            return ImageData.Create (info, format, null, reader.Data, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            var bitmap = image.Bitmap;
            if (bitmap.Format != PixelFormats.Bgra32)
                bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);

            int width = (int)image.Width;
            int height = (int)image.Height;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels (pixels, stride, 0);

            ApplyDeltaFilter (pixels, width, height);

            int w = (width + 1) & ~1;
            int h = (height + 1) & ~1;
            var rgb_data = new byte[w * h * 3];
            int dst = 0;

            // Interleave in 2x2 blocks
            for (int c = 0; c < 3; c++) // RGB order
            for (int y = 0; y < h; y += 2)
            for (int x = 0; x < w; x += 2)
            {
                rgb_data[dst++] = y < height && x < width ? pixels[(y * width + x) * 4 + c] : (byte)0;
                rgb_data[dst++] = y + 1 < height && x < width ? pixels[((y + 1) * width + x) * 4 + c] : (byte)0;
                rgb_data[dst++] = y < height && x + 1 < width ? pixels[(y * width + x + 1) * 4 + c] : (byte)0;
                rgb_data[dst++] = y + 1 < height && x + 1 < width ? pixels[((y + 1) * width + x + 1) * 4 + c] : (byte)0;
            }

            // Extract and prepare alpha
            var alpha_data = new byte[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                alpha_data[y * w + x] = y < height && x < width ? pixels[(y * width + x) * 4 + 3] : (byte)0;
            }

            byte[] compressed_rgb, compressed_alpha;
            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream (ms, CompressionMode.Compress, CompressionLevel.Level9))
                    z.Write (rgb_data, 0, rgb_data.Length);
                compressed_rgb = ms.ToArray();
            }

            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream (ms, CompressionMode.Compress, CompressionLevel.Level9))
                    z.Write (alpha_data, 0, alpha_data.Length);
                compressed_alpha = ms.ToArray();
            }

            // Write header
            using (var writer = new BinaryWriter (file))
            {
                writer.Write (0x544E51);   // 'QNT\0'
                writer.Write ((int)1);     // version
                writer.Write ((int)0x2C);  // header size
                writer.Write ((int)0);     // x offset
                writer.Write ((int)0);     // y offset
                writer.Write ((uint)width);
                writer.Write ((uint)height);
                writer.Write ((int)24);    // bpp
                writer.Write ((int)1);     // reserved
                writer.Write ((uint)compressed_rgb.Length);
                writer.Write ((uint)compressed_alpha.Length);

                writer.Write (compressed_rgb);
                writer.Write (compressed_alpha);
            }
        }

        void ApplyDeltaFilter (byte[] pixels, int width, int height)
        {
            // Apply predictive filter (reverse order)
            for (int y = height - 1; y > 0; y--)
            {
                for (int x = width - 1; x > 0; x--)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        int idx = (y * width + x) * 4 + c;
                        int up = pixels[((y - 1) * width + x) * 4 + c];
                        int left = pixels[(y * width + x - 1) * 4 + c];
                        pixels[idx] = (byte)(((up + left) >> 1) - pixels[idx]);
                    }
                }
                // First pixel of row
                for (int c = 0; c < 4; c++)
                {
                    int idx = y * width * 4 + c;
                    pixels[idx] = (byte)(pixels[(y - 1) * width * 4 + c] - pixels[idx]);
                }
            }
            // First row
            for (int x = width - 1; x > 0; x--)
            {
                for (int c = 0; c < 4; c++)
                {
                    int idx = x * 4 + c;
                    pixels[idx] = (byte)(pixels[(x - 1) * 4 + c] - pixels[idx]);
                }
            }
        }

        internal class Reader : IBaseImageReader
        {
            byte[]  m_input;
            byte[]  m_alpha;
            byte[]  m_output;
            int     m_bpp;
            int     m_width;
            int     m_height;

            public byte[] Data { get { return m_output; } }
            public int     BPP { get { return m_bpp*8; } }

            public Reader (Stream stream, QntMetaData info)
            {
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                int w = (m_width + 1) & ~1;
                int h = (m_height + 1) & ~1;
                int rgb_size = h * w * 3;
                m_bpp = info.AlphaSize != 0 ? 4 : 3;
                m_input = new byte[rgb_size];
                stream.Position = info.HeaderSize;
                var alpha_pos = info.HeaderSize + info.RGBSize;
                using (var zstream = new ZLibStream (stream, CompressionMode.Decompress, true))
                    if (rgb_size != zstream.Read (m_input, 0, rgb_size))
                        throw new InvalidFormatException ("Unexpected end of file");
                if (info.AlphaSize != 0)
                {
                    int alpha_size = w * m_height;
                    m_alpha = new byte[alpha_size];
                    stream.Position = alpha_pos;
                    using (var zstream = new ZLibStream (stream, CompressionMode.Decompress, true))
                        if (alpha_size != zstream.Read (m_alpha, 0, alpha_size))
                            throw new InvalidFormatException ("Unexpected end of file");
                }
                m_output = new byte[info.Width*info.Height*m_bpp];
            }

            public void Unpack ()
            {
                int src = 0;
                int dst;
                int stride = m_bpp * m_width;
                for (int channel = 0; channel < 3; ++channel)
                {
                    dst = channel;
                    for (int y = m_height >> 1; y != 0; --y)
                    {
                        for (int x = 0; x < m_width; ++x)
                        {
                            m_output[dst] = m_input[src++];
                            m_output[dst+stride] = m_input[src++];
                            dst += m_bpp;
                        }
                        dst += stride;
                        src += 2 * (m_width & 1);
                    }
                    if (0 != (m_height & 1))
                    {
                        for (int x = 0; x < m_width; ++x)
                        {
                            m_output[dst] = m_input[src];
                            src += 2;
                            dst += m_bpp;
                        }
                        src += 2 * (m_width & 1);
                    }
                }
                if (3 != m_bpp)
                {
                    src = 0;
                    dst = 3;
                    for (int y = 0; y < m_height; ++y)
                    {
                        for (int x = 0; x < m_width; ++x)
                        {
                            m_output[dst] = m_alpha[src++];
                            dst += 4;
                        }
                        src += m_width & 1;
                    }
                }
                dst = m_bpp;
                int i;
                for (i = stride-m_bpp; i != 0; --i)
                {
                    int b = m_output[dst-m_bpp] - m_output[dst];
                    m_output[dst++] = (byte)b;
                }
                for (int j = m_height - 1; j != 0; --j)
                {
                    for (i = 0; i != m_bpp; ++i)
                    {
                        m_output[dst] = (byte)(m_output[dst-stride] - m_output[dst]);
                        ++dst;
                    }
                    for (i = stride-m_bpp; i != 0; --i)
                    {
                        int b = ((int)m_output[dst-stride] + m_output[dst-m_bpp]) >> 1;
                        b -= m_output[dst];
                        m_output[dst++] = (byte)b;
                    }
                }
            }
        }

    }
}
