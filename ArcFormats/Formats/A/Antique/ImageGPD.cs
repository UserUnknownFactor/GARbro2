using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;

namespace GameRes.Formats.Antique
{
    internal class GpdMetaData : ImageMetaData
    {
        public int  Colors;
    }

    [Export(typeof(ImageFormat))]
    public class GpdFormat : ImageFormat
    {
        public override string         Tag { get { return "GPD"; } }
        public override string Description { get { return "An*tique image format"; } }
        public override uint     Signature { get { return 0x20445047; } } // 'GPD '

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            return new GpdMetaData {
                Width  = header.ToUInt32 (8),
                Height = header.ToUInt32 (0x0C),
                Colors = header.ToInt32 (0x10),
                BPP    = header.ToInt32 (0x14),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (GpdMetaData)info;
            PixelFormat format;
            if (8 == meta.BPP)
                format = PixelFormats.Indexed8;
            else if (24 == meta.BPP)
                format = PixelFormats.Bgr24;
            else if (32 == meta.BPP)
                format = PixelFormats.Bgr32;
            else
                throw new InvalidFormatException();

            file.Position = 0x18;
            BitmapPalette palette = null;
            if (8 == meta.BPP)
                palette = ReadPalette (file.AsStream, meta.Colors);

            int stride = (int)meta.Width * meta.BPP / 8;
            int unpacked_size = stride * (int)meta.Height;
            var pixels = new byte[unpacked_size];
            int packed_size = file.ReadInt32();
            if (-1 == packed_size)
            {
                file.Read (pixels, 0, unpacked_size);
            }
            else
            {
                using (var lzss = new LzssStream (file.AsStream, LzssMode.Decompress, true))
                    lzss.Read (pixels, 0, unpacked_size);
            }
            return ImageData.CreateFlipped (info, format, palette, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GpdFormat.Write not implemented");
        }
    }
}
