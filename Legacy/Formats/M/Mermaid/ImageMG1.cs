using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [000310][Mermaid] Into Your World

namespace GameRes.Formats.Mermaid
{
    internal class MgMetaData : ImageMetaData
    {
        public uint ImageOffset;
        public int  TileCount;
    }

    [Export(typeof(ImageFormat))]
    public class MgFormat : ImageFormat
    {
        public override string         Tag { get { return "MG1"; } }
        public override string Description { get { return "Mermaid obfuscated bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public MgFormat ()
        {
            Extensions = new string[] { "mg1", "mg2" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasAnyOfExtensions ("mg1", "mg2"))
                return null;
            var header = file.ReadHeader (0x36);
            if (!header.AsciiEqual ("BM"))
                return null;
            int width = header.ToInt32 (0x12);
            int tile_count = 5;
            if (file.Name.HasExtension ("mg2"))
                tile_count = width / 32;
            return new MgMetaData {
                Width = (uint)width,
                Height = header.ToUInt32 (0x16),
                BPP = header.ToInt16 (0x1C),
                ImageOffset = header.ToUInt32 (0x0A),
                TileCount = tile_count,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (MgMetaData)info;
            PixelFormat format;
            if (24 == meta.BPP)
                format = PixelFormats.Bgr24;
            else if (32 == meta.BPP)
                format = PixelFormats.Bgr32;
            else
                throw new InvalidFormatException();

            int tile_count = meta.TileCount;
            int stride = (int)meta.Width * ((meta.BPP + 7) / 8);
            int height = (int)meta.Height;
            int tile_width = stride / tile_count;
            int tile_height = (height + tile_count - 1) / tile_count;
            var pixels = new byte[stride * height];
            int row_size = tile_height * stride;
            file.Position = meta.ImageOffset;
            for (int dst = row_size - stride; dst >= 0; dst -= stride)
            {
                for (int y = tile_count-1; y >= 0; --y)
                {
                    int tile_dst = dst + y * tile_width;
                    for (int x = 0; x < stride; x += tile_width)
                    {
                        if (tile_dst + tile_width > pixels.Length)
                            file.Seek (tile_width, SeekOrigin.Current);
                        else
                            file.Read (pixels, tile_dst, tile_width);
                        tile_dst += row_size;
                    }
                }
            }
            return ImageData.Create (info, format, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MgFormat.Write not implemented");
        }
    }
}
