using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Qlie
{
    internal class DpngMetaData : ImageMetaData
    {
        public int TileCount;
    }

    [Export(typeof(ImageFormat))]
    public class DpngFormat : ImageFormat
    {
        public override string         Tag { get { return "DPNG"; } }
        public override string Description { get { return "QLIE tiled image format"; } }
        public override uint     Signature { get { return 0x474E5044; } } // 'DPNG'

        public DpngFormat ()
        {
            Extensions = new string[] { "png" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 8;
            var info = new DpngMetaData { BPP = 32 };
            info.TileCount = file.ReadInt32();
            if (info.TileCount <= 0)
                return null;
            info.Width     = file.ReadUInt32();
            info.Height    = file.ReadUInt32();
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (DpngMetaData)info;
            var bitmap = new WriteableBitmap ((int)info.Width, (int)info.Height,
                ImageData.DefaultDpiX, ImageData.DefaultDpiY, PixelFormats.Pbgra32, null);
            long next_tile = 0x14;
            for (int i = 0; i < meta.TileCount; ++i)
            {
                stream.Position = next_tile;
                int x = stream.ReadInt32();
                int y = stream.ReadInt32();
                int width = stream.ReadInt32();
                int height = stream.ReadInt32();
                uint size = stream.ReadUInt32();
                stream.Seek (8, SeekOrigin.Current);
                next_tile = stream.Position + size;
                if (0 == size)
                    continue;
                using (var png = new StreamRegion (stream.AsStream, stream.Position, size, true))
                {
                    var decoder = new PngBitmapDecoder (png,
                        BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    var frame = new FormatConvertedBitmap (decoder.Frames[0], PixelFormats.Pbgra32, null, 0);
                    int stride = frame.PixelWidth * 4;
                    var pixels = new byte[stride * frame.PixelHeight];
                    frame.CopyPixels (pixels, stride, 0);
                    var rect = new Int32Rect (0, 0, frame.PixelWidth, frame.PixelHeight);
                    bitmap.WritePixels (rect, pixels, stride, x, y);
                }
            }
            bitmap.Freeze();
            return new ImageData (bitmap, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DpngFormat.Write not implemented");
        }
    }
}
