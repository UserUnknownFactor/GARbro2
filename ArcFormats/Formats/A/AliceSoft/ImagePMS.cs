using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.AliceSoft
{
    internal class PmsMetaData : ImageMetaData
    {
        public uint DataOffset;
        public uint AlphaOffset;
    }

    [Export(typeof(ImageFormat))]
    public class PmsFormat : ImageFormat
    {
        public override string         Tag { get { return "PMS"; } }
        public override string Description { get { return "AliceSoft image format"; } }
        public override uint     Signature { get { return  0x014D50; } } // 'PM'
        public override bool      CanWrite { get { return  true; } }

        public PmsFormat ()
        {
            Signatures = new uint[] { 0x014D50, 0x024D50 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x30);
            var info = new PmsMetaData {
                BPP         = header[6],
                OffsetX     = header.ToInt32  (0x10),
                OffsetY     = header.ToInt32  (0x14),
                Width       = header.ToUInt32 (0x18),
                Height      = header.ToUInt32 (0x1C),
                DataOffset  = header.ToUInt32 (0x20),
                AlphaOffset = header.ToUInt32 (0x24),
            };
            if ((info.BPP != 16 && info.BPP != 8) || info.DataOffset < 0x30 || info.DataOffset >= file.Length)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var pms = new PmsReader (file, (PmsMetaData)info);
            var bitmap = pms.Unpack();
            bitmap.Freeze();
            return new ImageData (bitmap, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            if (image.BPP >= 24) WritePms16 (file, image);
            else
                throw new NotSupportedException ("PMS8 indexed format writing not implemented");
        }

        void WritePms16 (Stream file, ImageData image)
        {
            var bitmap = image.Bitmap;
            bool hasAlpha = bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Bgr32;

            // Convert to RGB565 + optional alpha
            int width = (int)image.Width;
            int height = (int)image.Height;
            var rgb565 = new ushort[width * height];
            byte[] alpha = hasAlpha ? new byte[width * height] : null;

            if (bitmap.Format == PixelFormats.Bgr565)
            {
                int stride = width * 2;
                var pixels = new byte[stride * height];
                bitmap.CopyPixels (pixels, stride, 0);
                Buffer.BlockCopy (pixels, 0, rgb565, 0, pixels.Length);
            }
            else
            {
                int srcBpp = bitmap.Format.BitsPerPixel / 8;
                int stride = width * srcBpp;
                var pixels = new byte[stride * height];

                if (bitmap.Format != PixelFormats.Bgra32 && bitmap.Format != PixelFormats.Bgr24)
                    bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgr24, null, 0);

                bitmap.CopyPixels (pixels, stride, 0);

                for (int i = 0, j = 0; i < width * height; i++)
                {
                    byte b = pixels[j++];
                    byte g = pixels[j++];
                    byte r = pixels[j++];

                    rgb565[i] = (ushort)((r & 0xF8) << 8 | (g & 0xFC) << 3 | (b >> 3));

                    if (srcBpp == 4)
                    {
                        if (alpha != null)
                            alpha[i] = pixels[j];
                        j++;
                    }
                }
            }

            var rgbData = CompressPms16 (rgb565, width, height);

            byte[] alphaData = null;
            if (alpha != null)
                alphaData = CompressPms8 (alpha, width, height);

            int dataOffset = 0x30; // Fixed header size
            int alphaOffset = hasAlpha ? dataOffset + rgbData.Length : 0;

            // Write PMS header
            using (var writer = new BinaryWriter (file))
            {
                writer.Write ((byte)'P');
                writer.Write ((byte)'M');
                writer.Write ((ushort)1);    // version
                writer.Write ((ushort)0x30); // header size
                writer.Write ((byte)16);     // bpp
                writer.Write ((byte)0);      // shadow bpp
                writer.Write ((byte)0);      // sprite flag
                writer.Write ((byte)0);      // padding
                writer.Write ((ushort)0);    // bank flag
                writer.Write ((int)0);       // reserved
                writer.Write ((int)0);       // x offset
                writer.Write ((int)0);       // y offset
                writer.Write ((uint)width);
                writer.Write ((uint)height);
                writer.Write ((uint)dataOffset);
                writer.Write ((uint)alphaOffset);
                writer.Write ((int)0);       // comment offset
                writer.Write ((int)0);       // reserved

                writer.Write (rgbData);
                if (alphaData != null)
                    writer.Write (alphaData);
            }
        }

        byte[] CompressPms16 (ushort[] pixels, int width, int height)
        {
            var output = new MemoryStream();

            for (int y = 0; y < height; y++)
            {
                int x = 0;
                while (x < width)
                {
                    int pos = y * width + x;

                    // Try copying from previous line
                    if (y > 0)
                    {
                        int matchLen = 0;
                        while (x + matchLen < width && matchLen < 257 &&
                               pixels[pos + matchLen] == pixels[pos - width + matchLen])
                        {
                            matchLen++;
                        }

                        if (matchLen >= 2)
                        {
                            output.WriteByte (0xFF);
                            output.WriteByte ((byte)(matchLen - 2));
                            x += matchLen;
                            continue;
                        }
                    }

                    // Try RLE for repeated pixels
                    if (x + 1 < width)
                    {
                        int runLen = 1;
                        ushort pixel = pixels[pos];

                        while (x + runLen < width && runLen < 258 &&
                               pixels[pos + runLen] == pixel)
                        {
                            runLen++;
                        }

                        if (runLen >= 3)
                        {
                            output.WriteByte (0xFD);
                            output.WriteByte ((byte)(runLen - 3));
                            output.WriteByte ((byte)pixel);
                            output.WriteByte ((byte)(pixel >> 8));
                            x += runLen;
                            continue;
                        }
                    }

                    // Write raw pixel
                    ushort val = pixels[pos];
                    if ((val & 0xFF) <= 0xF7)
                    {
                        output.WriteByte ((byte)val);
                        output.WriteByte ((byte)(val >> 8));
                    }
                    else
                    {
                        output.WriteByte (0xF8);
                        output.WriteByte ((byte)val);
                        output.WriteByte ((byte)(val >> 8));
                    }
                    x++;
                }
            }

            return output.ToArray();
        }

        byte[] CompressPms8 (byte[] pixels, int width, int height)
        {
            var output = new MemoryStream();

            for (int y = 0; y < height; y++)
            {
                int x = 0;
                while (x < width)
                {
                    int pos = y * width + x;

                    // Try copying from previous line
                    if (y > 0)
                    {
                        int matchLen = 0;
                        while (x + matchLen < width && matchLen < 258 &&
                               pixels[pos + matchLen] == pixels[pos - width + matchLen])
                        {
                            matchLen++;
                        }

                        if (matchLen >= 3)
                        {
                            output.WriteByte (0xFF);
                            output.WriteByte ((byte)(matchLen - 3));
                            x += matchLen;
                            continue;
                        }
                    }

                    // Try RLE
                    if (x + 1 < width)
                    {
                        int runLen = 1;
                        byte pixel = pixels[pos];

                        while (x + runLen < width && runLen < 259 &&
                               pixels[pos + runLen] == pixel)
                        {
                            runLen++;
                        }

                        if (runLen >= 4)
                        {
                            output.WriteByte (0xFD);
                            output.WriteByte ((byte)(runLen - 4));
                            output.WriteByte (pixel);
                            x += runLen;
                            continue;
                        }
                    }

                    // Write raw pixel
                    byte val = pixels[pos];
                    if (val <= 0xF7)
                    {
                        output.WriteByte (val);
                    }
                    else
                    {
                        output.WriteByte (0xF8);
                        output.WriteByte (val);
                    }
                    x++;
                }
            }

            return output.ToArray();
        }
    }

    internal class PmsReader
    {
        IBinaryStream   m_input;
        PmsMetaData     m_info;
        int             m_width;
        int             m_height;

        public PmsReader (IBinaryStream input, PmsMetaData info)
        {
            m_input  = input;
            m_info   = info;
            m_width  = (int)m_info.Width;
            m_height = (int)m_info.Height;
        }

        public BitmapSource Unpack ()
        {
            switch (m_info.BPP)
            {
            case 16:  return UnpackRgb();
            case 8:   return UnpackIndexed();
            default:
                throw new InvalidFormatException();
            }
        }

        BitmapSource UnpackIndexed ()
        {
            m_input.Position = m_info.AlphaOffset;
            var palette = ImageFormat.ReadPalette (m_input.AsStream, 0x100, PaletteFormat.Rgb);
            m_input.Position = m_info.DataOffset;
            var pixels = Unpack8bpp();
            return BitmapSource.Create (m_width, m_height, ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                                        PixelFormats.Indexed8, palette, pixels, m_width);
        }

        BitmapSource UnpackRgb ()
        {
            m_input.Position = m_info.DataOffset;
            var pixels = Unpack16bpp();
            var source = BitmapSource.Create (m_width, m_height, ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                                              PixelFormats.Bgr565, null, pixels, m_width*2);
            if (0 == m_info.AlphaOffset)
                return source;

            m_input.Position = m_info.AlphaOffset;
            var alpha = Unpack8bpp();
            source = new FormatConvertedBitmap (source, PixelFormats.Bgra32, null, 0);
            var output = new WriteableBitmap (source);
            output.Lock();
            unsafe
            {
                byte* buffer = (byte*)output.BackBuffer;
                int stride = output.BackBufferStride;
                int asrc = 0;
                for (int y = 0; y < m_height; ++y)
                {
                    for (int x = 3; x < stride; x += 4)
                    {
                        buffer[x] = alpha[asrc++];
                    }
                    buffer += stride;
                }
            }
            output.AddDirtyRect (new Int32Rect (0, 0, m_width, m_height));
            output.Unlock();
            return output;
        }

        ushort[] Unpack16bpp ()
        {
            var output = new ushort[m_width * m_height];
            int stride = m_width;

            for (int y = 0; y < m_height; ++y)
            for (int x = 0; x < m_width; )
            {
                int dst = y * stride + x;
                int count = 1;
                byte ctl = m_input.ReadUInt8();
                if (ctl < 0xF8)
                {
                    byte px = m_input.ReadUInt8();
                    output[dst] = (ushort)(ctl | (px << 8));
                }
                else if (ctl == 0xF8)
                {
                    output[dst] = m_input.ReadUInt16();
                }
                else if (ctl == 0xF9)
                {
                    count = m_input.ReadUInt8() + 1;
                    int p0 = m_input.ReadUInt8();
                    int p1 = m_input.ReadUInt8();
                    p0 = ((p0 & 0xE0) << 8) | ((p0 & 0x18) << 6) | ((p0 & 7) << 2);
                    p1 = ((p1 & 0xC0) << 5) | ((p1 & 0x3C) << 3) | (p1 & 3);
                    output[dst] = (ushort)(p0 | p1);
                    for (int i = 1; i < count; i++)
                    {
                        p1 = m_input.ReadUInt8();
                        p1 = ((p1 & 0xC0) << 5) | ((p1 & 0x3C) << 3) | (p1 & 3);
                        output[dst + i] = (ushort)(p0 | p1);
                    }
                }
                else if (ctl == 0xFA)
                {
                    output[dst] = output[dst - stride + 1];
                }
                else if (ctl == 0xFB)
                {
                    output[dst] = output[dst - stride - 1];
                }
                else if (ctl == 0xFC)
                {
                    count = (m_input.ReadUInt8() + 2) * 2;
                    ushort px0 = m_input.ReadUInt16();
                    ushort px1 = m_input.ReadUInt16();
                    for (int i = 0; i < count; i += 2)
                    {
                        output[dst + i    ] = px0;
                        output[dst + i + 1] = px1;
                    }
                }
                else if (ctl == 0xFD)
                {
                    count = m_input.ReadUInt8() + 3;
                    ushort px = m_input.ReadUInt16();
                    for (int i = 0; i < count; i++)
                    {
                        output[dst + i] = px;
                    }
                }
                else if (ctl == 0xFE)
                {
                    count = m_input.ReadUInt8() + 2;
                    int src = dst - stride * 2;
                    for (int i = 0; i < count; ++i)
                    {
                        output[dst+i] = output[src+i];
                    }
                }
                else // ctl == 0xFF
                {
                    count = m_input.ReadUInt8() + 2;
                    int src = dst - stride;
                    for (int i = 0; i < count; ++i)
                    {
                        output[dst+i] = output[src+i];
                    }
                }
                x += count;
            }
            return output;
        }

        byte[] Unpack8bpp ()
        {
            var output = new byte[m_width * m_height];
            int stride = m_width;

            for (int y = 0; y < m_height; y++)
            for (int x = 0; x < m_width; )
            {
                int dst = y * stride + x;
                int count = 1;
                byte ctl = m_input.ReadUInt8();
                if (ctl < 0xF8)
                {
                    output[dst] = ctl;
                }
                else if (ctl == 0xFF)
                {
                    count = m_input.ReadUInt8() + 3;
                    Binary.CopyOverlapped (output, dst - stride, dst, count);
                }
                else if (ctl == 0xFE)
                {
                    count = m_input.ReadUInt8() + 3;
                    Binary.CopyOverlapped (output, dst - stride * 2, dst, count);
                }
                else if (ctl == 0xFD)
                {
                    count = m_input.ReadUInt8() + 4;
                    byte px = m_input.ReadUInt8();
                    for (int i = 0; i < count; ++i)
                    {
                        output[dst + i] = px;
                    }
                }
                else if (ctl == 0xFC)
                {
                    count = (m_input.ReadUInt8() + 3) * 2;
                    byte px0 = m_input.ReadUInt8();
                    byte px1 = m_input.ReadUInt8();
                    for (int i = 0; i < count; i += 2)
                    {
                        output[dst + i    ] = px0;
                        output[dst + i + 1] = px1;
                    }
                }
                else // >= 0xF8 < 0xFC
                {
                    output[dst] = m_input.ReadUInt8();
                }
                x += count;
            }
            return output;
        }
    }
}
