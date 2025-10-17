using System;
using System.Collections.Generic;
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

    internal class DpngTileInfo
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public uint DataSize;
        public byte[] Data;
    }

    [Export(typeof(ImageFormat))]
    public class DpngFormat : ImageFormat
    {
        public override string         Tag { get { return "DPNG"; } }
        public override string Description { get { return "QLIE tiled image format"; } }
        public override uint     Signature { get { return  0x474E5044; } } // 'DPNG'
        public override bool      CanWrite { get { return  true; } }

        private const int MaxTileWidth = 512;
        private const int MaxTileHeight = 512;

        public DpngFormat ()
        {
            Extensions = new string[] { "png", "dpng" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);

            var info = new DpngMetaData { BPP = 32 };
            info.TileCount = header.ToInt32 (8);
            if (!ArchiveFormat.IsSaneCount (info.TileCount, 10000))
                return null;
            info.Width     = header.ToUInt32 (0xC);
            info.Height    = header.ToUInt32 (0x10);

            if (info.Width == 0 || info.Height == 0 || info.Width > 16000 || info.Height > 16000)
                return null;

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
                int x      = stream.ReadInt32();
                int y      = stream.ReadInt32();
                int width  = stream.ReadInt32();
                int height = stream.ReadInt32();
                uint size  = stream.ReadUInt32();
                stream.Seek (8, SeekOrigin.Current); // skip unknown fields
                next_tile = stream.Position + size;

                if (size == 0)
                    continue;

                using (var png = new StreamRegion (stream.AsStream, stream.Position, size, true))
                {
                    var decoder = new PngBitmapDecoder (png,
                        BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count > 0)
                    {
                        var frame = new FormatConvertedBitmap (decoder.Frames[0], PixelFormats.Pbgra32, null, 0);
                        int stride = frame.PixelWidth * 4;
                        var pixels = new byte[stride * frame.PixelHeight];
                        frame.CopyPixels (pixels, stride, 0);
                        var rect = new Int32Rect (0, 0, frame.PixelWidth, frame.PixelHeight);
                        bitmap.WritePixels (rect, pixels, stride, x, y);
                    }
                }
            }

            bitmap.Freeze();
            return new ImageData (bitmap, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            var bitmap = image.Bitmap;
            if (bitmap.Format != PixelFormats.Pbgra32 && bitmap.Format != PixelFormats.Bgra32)
            {
                bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);
            }

            var tiles = GenerateTiles (bitmap);

            using (var writer = new BinaryWriter (file, System.Text.Encoding.ASCII, true))
            {
                writer.Write (new byte[] { (byte)'D', (byte)'P', (byte)'N', (byte)'G' });
                writer.Write ((uint)0); // unknown1
                writer.Write (tiles.Count);
                writer.Write (bitmap.PixelWidth);
                writer.Write (bitmap.PixelHeight);

                // Write tile entries and data
                foreach (var tile in tiles)
                {
                    writer.Write (tile.X);
                    writer.Write (tile.Y);
                    writer.Write (tile.Width);
                    writer.Write (tile.Height);
                    writer.Write (tile.DataSize);
                    writer.Write ((uint)0); // unknown1
                    writer.Write ((uint)0); // unknown2
                    writer.Write (tile.Data);
                }
            }
        }

        private List<DpngTileInfo> GenerateTiles (BitmapSource bitmap)
        {
            var tiles = new List<DpngTileInfo>();
            int imageWidth = bitmap.PixelWidth;
            int imageHeight = bitmap.PixelHeight;

            // Divide image into tiles
            for (int y = 0; y < imageHeight; y += MaxTileHeight)
            {
                for (int x = 0; x < imageWidth; x += MaxTileWidth)
                {
                    int tileWidth = Math.Min (MaxTileWidth, imageWidth - x);
                    int tileHeight = Math.Min (MaxTileHeight, imageHeight - y);

                    // Extract tile from source image
                    var tileRect = new Int32Rect (x, y, tileWidth, tileHeight);
                    var tileBitmap = new CroppedBitmap (bitmap, tileRect);

                    // Check if tile has any non-transparent pixels
                    if (!IsTileEmpty (tileBitmap))
                    {
                        byte[] pngData = EncodeTileAsPng (tileBitmap);

                        tiles.Add (new DpngTileInfo
                        {
                            X = x,
                            Y = y,
                            Width = tileWidth,
                            Height = tileHeight,
                            DataSize = (uint)pngData.Length,
                            Data = pngData
                        });
                    }
                }
            }

            // If no tiles were generated (completely transparent image), create at least one
            if (tiles.Count == 0)
            {
                var emptyTile = new WriteableBitmap (1, 1, 96, 96, PixelFormats.Pbgra32, null);
                byte[] pngData = EncodeTileAsPng (emptyTile);
                tiles.Add (new DpngTileInfo
                {
                    X = 0,
                    Y = 0,
                    Width = 1,
                    Height = 1,
                    DataSize = (uint)pngData.Length,
                    Data = pngData
                });
            }

            return tiles;
        }

        private bool IsTileEmpty (BitmapSource tile)
        {
            int stride = tile.PixelWidth * 4;
            var pixels = new byte[stride * tile.PixelHeight];
            tile.CopyPixels (pixels, stride, 0);

            // Check if all pixels are transparent
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0) // A
                    return false;
            }
            return true;
        }

        private byte[] EncodeTileAsPng (BitmapSource tile)
        {
            using (var stream = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add (BitmapFrame.Create (tile));
                encoder.Save (stream);
                return stream.ToArray();
            }
        }
    }
}
