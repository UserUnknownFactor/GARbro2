using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Ankh
{
    internal class GpdMetaData : ImageMetaData
    {
        public int  HeaderSize;
    }

    [Export(typeof(ImageFormat))]
    public class GpdFormat : ImageFormat
    {
        public override string         Tag { get { return "GPD/ANKH"; } }
        public override string Description { get { return "Ankh image format"; } }
        public override uint     Signature { get { return 0x647067; } } // 'gpd'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            return new GpdMetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP    = 24,
                HeaderSize = header.ToInt32 (12) != 0 ? 12 : 16,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (GpdMetaData)info;
            int stride = (int)info.Width * 3;
            file.Position = meta.HeaderSize;
            using (var lzss = new LzssStream (file.AsStream, LzssMode.Decompress, true))
            {
                var pixels = new byte[stride * (int)info.Height];
                lzss.Read (pixels, 0, pixels.Length);
                return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GpdFormat.Write not implemented");
        }
    }
}
