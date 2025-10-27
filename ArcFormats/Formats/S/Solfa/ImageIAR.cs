using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Solfa
{
    internal class IarMetaData : ImageMetaData
    {
        public int  Stride;
        public int  PaletteSize;
        public int  ImageSize;
    }

    // NOTE: internal format
    [Export(typeof(ImageFormat))]
    public class IarFormat : ImageFormat
    {
        public override string         Tag { get { return "IAR"; } }
        public override string Description { get { return "SAS engine compressed image format"; } }
        public override uint     Signature { get { return  0x00524149; } } // 'IAR'

        public IarFormat ()
        {
            Extensions = new string[] { "" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x28);
            if (!header.AsciiEqual (4, "SAS5"))
                return null;
            return new IarMetaData
            {
                Width       = header.ToUInt32 (0x08),
                Height      = header.ToUInt32 (0x0C),
                OffsetX     = -header.ToInt32 (0x10),
                OffsetY     = -header.ToInt32 (0x14),
                BPP         = header.ToInt32  (0x18),
                Stride      = header.ToInt32  (0x1C),
                PaletteSize = header.ToInt32  (0x20),
                ImageSize   = header.ToInt32  (0x24),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (IarMetaData)info;
            PixelFormat format;
            if (32 == meta.BPP)             format = PixelFormats.Bgra32;
            else if (24 == meta.BPP)        format = PixelFormats.Bgr24;
            else if (0 == meta.PaletteSize) format = PixelFormats.Gray8;
            else                            format = PixelFormats.Indexed8;

            stream.Position = 0x28;
            BitmapPalette palette = null;
            if (meta.PaletteSize > 0)
                palette = ReadPalette (stream.AsStream, meta.PaletteSize);
            var pixels = stream.ReadBytes (meta.ImageSize);
            return ImageData.Create (info, format, palette, pixels, meta.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("IarFormat.Write not implemented");
        }

        static BitmapPalette ReadPalette (Stream input, int palette_size)
        {
            var palette_data = new byte[palette_size];
            if (palette_data.Length != input.Read (palette_data, 0, palette_data.Length))
                throw new EndOfStreamException();
            palette_size = Math.Min (0x400, palette_size);
            int color_size = palette_size / 0x100;
            var palette = new Color[0x100];
            for (int i = 0; i < palette.Length; ++i)
            {
                int c = i * color_size;
                palette[i] = Color.FromRgb (palette_data[c+2], palette_data[c+1], palette_data[c]);
            }
            return new BitmapPalette (palette);
        }
    }
}
