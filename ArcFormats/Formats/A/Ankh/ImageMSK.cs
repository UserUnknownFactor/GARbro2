using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Ankh
{
    [Export(typeof(ImageFormat))]
    public class MskFormat : ImageFormat
    {
        public override string         Tag { get { return "MSK/ANKH"; } }
        public override string Description { get { return "Ankh bitmap format"; } }
        public override uint     Signature { get { return 0x6B736D; } } // 'msk'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            return new GpdMetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP    = 8,
                HeaderSize = header.ToInt32 (12) != 0 ? 12 : 16,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (GpdMetaData)info;
            file.Position = meta.HeaderSize;
            using (var lzss = new LzssStream (file.AsStream, LzssMode.Decompress, true))
            {
                var pixels = new byte[info.Width * info.Height];
                lzss.Read (pixels, 0, pixels.Length);
                return ImageData.CreateFlipped (info, PixelFormats.Gray8, null, pixels, (int)info.Width);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MskFormat.Write not implemented");
        }
    }
}
