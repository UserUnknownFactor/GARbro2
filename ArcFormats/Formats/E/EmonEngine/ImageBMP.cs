using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;

namespace GameRes.Formats.EmonEngine
{
    internal class EmMetaData : ImageMetaData
    {
        public int Colors;
        public int Stride;
        public int LzssFrameSize;
        public int LzssInitPos;
        public int DataOffset;
    }

    internal class EmeImageDecoder : BinaryImageDecoder
    {
        public EmeImageDecoder (IBinaryStream input, EmMetaData info) : base (input, info)
        {
        }

        protected override ImageData GetImageData ()
        {
            var meta = (EmMetaData)Info;
            m_input.Position = meta.DataOffset;
            BitmapPalette palette = null;
            if (meta.Colors != 0)
                palette = ImageFormat.ReadPalette (m_input.AsStream, Math.Max (meta.Colors, 3), PaletteFormat.RgbX);
            var pixels = new byte[meta.Stride * (int)meta.Height];
            if (meta.LzssFrameSize != 0)
            {
                using (var lzss = new LzssStream (m_input.AsStream, LzssMode.Decompress, true))
                {
                    lzss.Config.FrameSize = meta.LzssFrameSize;
                    lzss.Config.FrameInitPos = meta.LzssInitPos;
                    if (pixels.Length != lzss.Read (pixels, 0, pixels.Length))
                        throw new EndOfStreamException();
                }
            }
            else
            {
                if (pixels.Length != m_input.Read (pixels, 0, pixels.Length))
                    throw new EndOfStreamException();
            }
            if (7 == meta.BPP)
                return ImageData.Create (Info, PixelFormats.Gray8, palette, pixels, meta.Stride);

            PixelFormat format;
            if (32 == meta.BPP)
                format = PixelFormats.Bgr32;
            else if (24 == meta.BPP)
                format = PixelFormats.Bgr24;
            else
                format = PixelFormats.Indexed8;
            return ImageData.CreateFlipped (Info, format, palette, pixels, meta.Stride);
        }
    }
}
