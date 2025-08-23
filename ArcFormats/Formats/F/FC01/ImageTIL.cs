using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.FC01
{
    internal class TilMetaData : ImageMetaData
    {
        public int  TileWidth;
        public int  TileHeight;
    }

    [Export(typeof(ImageFormat))]
    public class TilFormat : ImageFormat
    {
        public override string         Tag { get { return "TIL"; } }
        public override string Description { get { return "AGSI tiled image format"; } }
        public override uint     Signature { get { return 0x304C4954; } } // 'TIL0'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            return new TilMetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                TileWidth  = header.ToInt32 (0x0C),
                TileHeight = header.ToInt32 (0x10),
                BPP = 32,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (TilMetaData)info;
            int tile_w = (int)meta.Width / meta.TileWidth;
            int tile_h = (int)meta.Height / meta.TileHeight;
            uint tile_position = 0x14;
            int stride = (int)meta.Width * 4;
            var pixels = new byte[stride * (int)meta.Height];
            int tile_y = 0;
            int tile_stride = stride * meta.TileHeight;
            for (int i = 0; i < tile_h; ++i)
            {
                int tile_x = tile_y;
                for (int j = 0; j < tile_w; ++j)
                {
                    int dst = tile_x;
                    for (int y = 0; y < meta.TileHeight; ++y)
                    {
                        file.Position = tile_position;
                        uint line_length = file.ReadUInt32();
                        tile_position += line_length;
                        if (file.ReadInt32() != 0)
                        {
                            int offset = file.ReadInt32() * 4;
                            int length = file.ReadInt32() * 4;
                            file.Read (pixels, dst + offset, length);
                        }
                        dst += stride;
                    }
                    tile_x += 4 * meta.TileWidth;
                }
                tile_y += tile_stride;
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("TilFormat.Write not implemented");
        }
    }
}
