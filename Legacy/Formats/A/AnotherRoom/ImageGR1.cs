using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.AnotherRoom
{
    [Export(typeof(ImageFormat))]
    public class Gr1Format : ImageFormat
    {
        public override string         Tag { get { return "GR1/LUVL"; } }
        public override string Description { get { return "AnotherRoom compressed bitmap"; } }
        public override uint     Signature { get { return 0x4C56554C; } } // 'LUVL'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x24);
            if (!header.AsciiEqual (4, "LATIO"))
                return null;
            return new ImageMetaData {
                Width = header.ToUInt32 (0x1C),
                Height = header.ToUInt32 (0x20),
                BPP = 16,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x24;
            int stride = (int)info.Width * 2;
            var pixels = new byte[stride * (int)info.Height];
            using (var input = new LzssStream (file.AsStream, LzssMode.Decompress, true))
            {
                input.Read (pixels, 0, pixels.Length);
            }
            return ImageData.CreateFlipped (info, PixelFormats.Bgr555, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Gr1Format.Write not implemented");
        }
    }
}
