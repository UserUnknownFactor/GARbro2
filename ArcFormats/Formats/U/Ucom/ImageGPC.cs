using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Ucom
{
    internal class GpcMetaData : ImageMetaData
    {
        public int  PaletteColors;
    }

    [Export(typeof(ImageFormat))]
    public class GpcFormat : ImageFormat
    {
        public override string         Tag { get { return "GPC"; } }
        public override string Description { get { return "For/Ucom image format"; } }
        public override uint     Signature { get { return  0x00285047; } } // 'GP('
        public override bool      CanWrite { get { return  false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x26);
            int header_length = header.ToInt32  (2);
            uint width        = header.ToUInt32 (6);
            uint height       = header.ToUInt32 (0xA);
            int bpp           = header.ToUInt16 (0x10);
            if (width > 10000 || height > 10000 || (bpp != 8 && bpp != 24 && bpp != 32))
                return null;

            int colors = 0;
            if (bpp == 8)
            {
                colors = header.ToInt32 (0x22);
                if (colors == 0)
                    colors = 0x100;
            }

            return new GpcMetaData {
                Width = width,
                Height = height,
                BPP = bpp,
                PaletteColors = colors,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var gpc = new GpcReader (file, (GpcMetaData)info);
            gpc.Unpack();
            return ImageData.Create (info, gpc.Format, gpc.Palette, gpc.Data, gpc.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            var writer = new GpcWriter (file, image);
            writer.Write();
        }
    }

    internal sealed class GpcReader
    {
        IBinaryStream   m_input;
        int             m_width;
        int             m_height;
        int             m_stride;
        int             m_colors;
        int             m_pixel_size;
        byte[]          m_output;

        public BitmapPalette Palette { get; private set; }
        public PixelFormat    Format { get; private set; }
        public byte[]           Data { get { return m_output; } }
        public int            Stride { get { return m_stride; } }

        public GpcReader (IBinaryStream input, GpcMetaData info)
        {
            m_input = input;
            m_width  = (int)info.Width;
            m_height = (int)info.Height;
            m_pixel_size = info.BPP / 8;
            m_colors = info.PaletteColors;
            m_stride = (m_width * m_pixel_size + 3) & ~3;
            m_output = new byte[m_height * m_stride];

            if (m_pixel_size == 1)      Format = PixelFormats.Indexed8;
            else if (m_pixel_size == 3) Format = PixelFormats.Bgr24;
            else                        Format = PixelFormats.Bgra32;
        }

        public void Unpack ()
        {
            m_input.Position = 0x2A;
            if (1 == m_pixel_size)
                Palette = ImageFormat.ReadPalette (m_input.AsStream, m_colors);
            int gap = m_stride - m_width * m_pixel_size;
            for (int dst_row = m_output.Length - m_stride; dst_row >= 0; dst_row -= m_stride)
            {
                int dst = dst_row;
                int x = 0;
                while (x < m_width)
                {
                    byte ctl  = m_input.ReadUInt8();
                    int count = (ctl >> 1) + 1;
                    int pixel_count = m_pixel_size * count;
                    if (0 == (ctl & 1))
                        m_input.Read (m_output, dst, pixel_count);
                    else
                    {
                        m_input.Read (m_output, dst, m_pixel_size);
                        Binary.CopyOverlapped (m_output, dst, dst+m_pixel_size, pixel_count - m_pixel_size);
                    }
                    dst += pixel_count;
                    x   += count;
                }
                if (gap != 0)
                    m_input.Read (m_output, dst, gap);
            }
        }
    }

    internal sealed class GpcWriter
    {
        Stream          m_output;
        ImageData       m_image;
        int             m_width;
        int             m_height;
        int             m_stride;
        int             m_pixel_size;
        BitmapPalette   m_palette;
        PixelFormat     m_format;
        byte[]          m_pixels;

        public GpcWriter (Stream output, ImageData image)
        {
            m_output = output;
            m_image = image;
            m_width = (int)image.Width;
            m_height = (int)image.Height;

            var bitmap = image.Bitmap;
            m_format = bitmap.Format;
            m_palette = bitmap.Palette;

            if (m_format == PixelFormats.Indexed8 || m_format == PixelFormats.Gray8)
                m_pixel_size = 1;
            /*else if (m_format == PixelFormats.Bgr24 || m_format == PixelFormats.Rgb24 || 
                     m_format == PixelFormats.Bgr32)  // Bgr32/Rgb32 -> 24-bit
                m_pixel_size = 3;*/
            else /*if (m_format == PixelFormats.Bgra32 || m_format == PixelFormats.Pbgra32)*/
                m_pixel_size = 4;
            /*else
                throw new System.NotSupportedException ($"Unsupported pixel format: {m_format}");*/
            m_stride = (m_width * m_pixel_size + 3) & ~3;
        }

        public void Write ()
        {
            m_pixels = GetPixelData();

            byte[] compressedData;
            using (var ms = new MemoryStream())
            {
                WriteCompressedData (ms);
                compressedData = ms.ToArray();
            }

            WriteHeader (compressedData.Length);

            if (m_pixel_size == 1)
                WritePalette();

            m_output.Write (compressedData, 0, compressedData.Length);
        }

        private void WriteHeader (int compressedSize)
        {
            using (var writer = new BinaryWriter (m_output, System.Text.Encoding.ASCII, true))
            {
                // Signature 'GP('  + header length
                writer.Write ((uint)0x00285047); // 0x47, 0x50, 0x28, 0x00
                writer.Write ((ushort)0);

                writer.Write ((uint)m_width);
                writer.Write ((uint)m_height);

                writer.Write ((ushort)1);
                writer.Write ((ushort)(m_pixel_size * 8));
                writer.Write ((uint)0); // always 0

                int uncompressedSize = m_width * m_height * 4;
                writer.Write (uncompressedSize); // TODO: why 24bpp gives unknown same values here?
                writer.Write ((uint)0);

                writer.Write ((uint)0);

                if (m_pixel_size == 1)
                {
                    int colors = m_palette?.Colors.Count ?? 256;
                    if (colors == 256) colors = 0;
                    writer.Write (colors);
                }
                else
                    writer.Write ((int)0);

                writer.Write ((int)0);
            }
        }

        private void WritePalette ()
        {
            using (var writer = new BinaryWriter (m_output, System.Text.Encoding.ASCII, true))
            {
                if (m_palette != null)
                {
                    int palette_colors = System.Math.Min (m_palette.Colors.Count, 256);

                    for (int i = 0; i < palette_colors; i++)
                    {
                        var color = m_palette.Colors[i];
                        writer.Write (color.B);
                        writer.Write (color.G);
                        writer.Write (color.R);
                        writer.Write (color.A);
                    }

                    for (int i = palette_colors; i < 256; i++)
                    {
                        writer.Write ((byte)0);
                        writer.Write ((byte)0);
                        writer.Write ((byte)0);
                        writer.Write ((byte)255);
                    }
                }
                else
                {
                    for (int i = 0; i < 256; i++)
                    {
                        writer.Write ((byte)i);
                        writer.Write ((byte)i);
                        writer.Write ((byte)i);
                        writer.Write ((byte)255);
                    }
                }
            }
        }

        private byte[] GetPixelData ()
        {
            var bitmap = m_image.Bitmap;
            var target_format = GetTargetFormat();

            if (bitmap.Format != target_format)
                bitmap = new FormatConvertedBitmap (bitmap, target_format, null, 0);

            int total_size = m_height * m_stride;
            var pixels = new byte[total_size];
            bitmap.CopyPixels (pixels, m_stride, 0);

            return pixels;
        }

        private PixelFormat GetTargetFormat ()
        {
            switch (m_pixel_size)
            {
                case 1: return PixelFormats.Indexed8;
                case 3: return PixelFormats.Bgr24;
                case 4: return PixelFormats.Bgra32;
                default:
                    throw new System.InvalidOperationException();
            }
        }

        private void WriteCompressedData (Stream output)
        {
            using (var writer = new BinaryWriter (output, System.Text.Encoding.ASCII, true))
            {
                // Process rows from bottom to top
                for (int y = m_height - 1; y >= 0; y--)
                {
                    int row_offset = y * m_stride;
                    int x = 0;

                    while (x < m_width)
                    {
                        int remaining = m_width - x;
                        int max_count = System.Math.Min (128, remaining);

                        // Look for RLE opportunity
                        int run_length = 1;

                        if (x + 1 < m_width)
                        {
                            while (run_length < max_count)
                            {
                                bool same = true;
                                for (int i = 0; i < m_pixel_size; i++)
                                {
                                    if (m_pixels[row_offset + x * m_pixel_size + i] != 
                                        m_pixels[row_offset + (x + run_length) * m_pixel_size + i])
                                    {
                                        same = false;
                                        break;
                                    }
                                }
                                if (!same)
                                    break;
                                run_length++;
                            }
                        }

                        if (run_length >= 2)
                        {
                            // RLE: control byte with bit 0 set
                            byte ctl = (byte)((run_length - 1) << 1 | 1);
                            writer.Write (ctl);

                            // Write pixel data once
                            for (int i = 0; i < m_pixel_size; i++)
                                writer.Write (m_pixels[row_offset + x * m_pixel_size + i]);

                            x += run_length;
                        }
                        else
                        {
                            // Raw: find how many non-repeating pixels
                            int raw_count = 1;

                            while (raw_count < max_count && (x + raw_count) < m_width)
                            {
                                if ((x + raw_count + 1) < m_width)
                                {
                                    bool same = true;
                                    for (int i = 0; i < m_pixel_size; i++)
                                    {
                                        if (m_pixels[row_offset + (x + raw_count) * m_pixel_size + i] != 
                                            m_pixels[row_offset + (x + raw_count + 1) * m_pixel_size + i])
                                        {
                                            same = false;
                                            break;
                                        }
                                    }
                                    if (same)
                                        break;
                                }
                                raw_count++;
                            }

                            // Raw: control byte with bit 0 clear
                            byte ctl = (byte)((raw_count - 1) << 1);
                            writer.Write (ctl);

                            for (int j = 0; j < raw_count; j++)
                            for (int i = 0; i < m_pixel_size; i++)
                                writer.Write (m_pixels[row_offset + (x + j) * m_pixel_size + i]);

                            x += raw_count;
                        }
                    }

                    // Write row padding
                    int gap = m_stride - m_width * m_pixel_size;
                    if (gap > 0)
                    {
                        for (int i = 0; i < gap; i++)
                            writer.Write ((byte)0);
                    }
                }
            }
        }
    }
}
