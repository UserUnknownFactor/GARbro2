using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.uGOS
{
    internal class TxtMetaData : ImageMetaData
    {
        public IEnumerable<Tile> Tiles;
    }

    internal class Tile
    {
        public string   FileName;
        public int  X;
        public int  Y;
    }

    [Export(typeof(ImageFormat))]
    public class TxtFormat : ImageFormat
    {
        public override string         Tag { get { return "TXT/uGOS"; } }
        public override string Description { get { return "μ-GameOperationSystem tiled bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public TxtFormat ()
        {
            Extensions = Array.Empty<string>();
        }

        static readonly Regex HeaderRe = new Regex (@"^\s*(\d+),\s*(\d+),\s*(\d+)$");
        static readonly Regex TileRe   = new Regex (@"^.+@(\d+),(\d+)\..*$");

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length > 0x1000 || !file.Name.HasExtension (".txt"))
                return null;
            using (var input = new StreamReader (file.AsStream, Encodings.cp932, true))
            {
                var header = input.ReadLine();
                if (string.IsNullOrEmpty (header))
                    return null;
                var match = HeaderRe.Match (header);
                if (!match.Success)
                    return null;
                uint width  = UInt32.Parse (match.Groups[1].Value);
                uint height = UInt32.Parse (match.Groups[2].Value);
                int tile_size = Int32.Parse (match.Groups[3].Value);
                if (tile_size <= 0)
                    return null;
                var tiles = new List<Tile>();
                string line;
                while ((line = input.ReadLine()) != null)
                {
                    match = TileRe.Match (line);
                    if (!match.Success)
                        return null;
                    int x = Int32.Parse (match.Groups[2].Value);
                    int y = Int32.Parse (match.Groups[1].Value);
                    var tile = new Tile {
                        FileName = match.Groups[0].Value,
                        X = x * tile_size,
                        Y = y * tile_size,
                    };
                    tiles.Add (tile);
                }
                if (0 == tiles.Count)
                    return null;
                return new TxtMetaData {
                    Width = width,
                    Height = height,
                    BPP = 32,
                    Tiles = tiles,
                };
            }
        }

        static readonly ResourceInstance<ImageFormat> DetFormat = new ResourceInstance<ImageFormat> ("BMP/uGOS");

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (TxtMetaData)info;
            var bitmap = new WriteableBitmap (meta.iWidth, meta.iHeight, ImageData.DefaultDpiX,
                                              ImageData.DefaultDpiY, PixelFormats.Bgra32, null);
            var dir = VFS.GetDirectoryName (meta.FileName);
            foreach (var tile in meta.Tiles)
            {
                var filename = VFS.CombinePath (dir, tile.FileName);
                using (var input = VFS.OpenBinaryStream (filename))
                {
                    var tile_info = DetFormat.Value.ReadMetaData (input) as DetBmpMetaData;
                    if (null == tile_info)
                        throw new InvalidFormatException ("Invalid uGOS tile bitmap.");
                    if (tile.X >= meta.iWidth || tile.Y >= meta.iHeight)
                        continue;
                    var reader = new DetBmpFormat.Reader (input, tile_info);
                    reader.Unpack();
                    int width = Math.Min (tile_info.iWidth, meta.iWidth - tile.X);
                    int height = Math.Min (tile_info.iHeight, meta.iHeight - tile.Y);
                    var src_rect = new Int32Rect (0, 0, width, height);
                    bitmap.WritePixels (src_rect, reader.Data, reader.Stride, tile.X, tile.Y);
                }
            }
            var flipped = new TransformedBitmap (bitmap, new ScaleTransform { ScaleY = -1 });
            flipped.Freeze();
            return new ImageData (flipped, meta);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("TxtFormat.Write not implemented");
        }
    }
}
