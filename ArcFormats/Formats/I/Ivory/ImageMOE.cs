using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Ivory
{
    [Export(typeof(ImageFormat))]
    public class MoeFormat : ImageFormat
    {
        public override string         Tag { get { return "MOE"; } }
        public override string Description { get { return "Ivory image format"; } }
        public override uint     Signature { get { return 0; } }

        public MoeFormat ()
        {
            Extensions = new string[] { "moe", "shw" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var wh = stream.Signature;
            uint width  = wh & 0xFFFF;
            uint height = wh >> 16;
            if (0 == width || width > 800 || 0 == height || height > 600)
                return null;
            int bpp = stream.Name.HasExtension (".SHW") ? 8 : 24;
            stream.Position = 4;
            if (!IsValidInput (stream.AsStream, width, height, bpp / 8))
                return null;
            return new ImageMetaData { Width = width, Height = height, BPP = bpp };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 4;
            int pixel_size = info.BPP / 8;
            var pixels = new byte[pixel_size * (int)info.Width * (int)info.Height];
            int dst = 0;
            while (dst < pixels.Length)
            {
                int count = stream.ReadByte();
                if (-1 == count)
                    throw new EndOfStreamException();
                if (0 != (count & 0x80))
                {
                    count = Math.Min (pixel_size * (count & 0x7F), pixels.Length - dst);
                    stream.Read (pixels, dst, count);
                    dst += count;
                }
                else
                {
                    count *= pixel_size;
                    stream.Read (pixels, dst, pixel_size);
                    Binary.CopyOverlapped (pixels, dst, dst+pixel_size, count-pixel_size);
                    dst += count;
                }
            }
            if (24 == info.BPP)
                return ImageData.Create (info, PixelFormats.Bgr24, null, pixels);

            const int MaxAlpha = 0x10;
            var colors = new Color[MaxAlpha+1];
            for (int i = 0; i <= MaxAlpha; ++i)
            {
                byte g = (byte)(i * 0xFF / MaxAlpha);
                colors[i] = Color.FromRgb (g, g, g);
            }
            var palette = new BitmapPalette (colors);
            return ImageData.Create (info, PixelFormats.Indexed8, palette, pixels);
        }

        /// <summary>
        /// Try to interpret input stream as a compressed image.
        /// </summary>
        bool IsValidInput (Stream input, uint width, uint height, int pixel_size)
        {
            int total = (int)width * (int)height;
            // Other formats will be incorrectly recognized as this format, try to correct it.
            if (total * pixel_size < input.Length / 10)
                return false;
            int dst = 0;
            while (dst < total)
            {
                int count = input.ReadByte();
                if (-1 == count)
                    return false;
                if (0 != (count & 0x80))
                {
                    count = Math.Min (count & 0x7F, total - dst);
                    input.Seek (count * pixel_size, SeekOrigin.Current);
                }
                else
                {
                    input.Seek (pixel_size, SeekOrigin.Current);
                }
                dst += count;
                if (dst > total)
                    return false;
            }
            return true;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MoeFormat.Write not implemented");
        }
    }
}
