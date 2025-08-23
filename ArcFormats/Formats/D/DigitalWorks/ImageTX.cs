using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.DigitalWorks
{
    [Export(typeof(ImageFormat))]
    public class TxFormat : ImageFormat
    {
        public override string         Tag { get { return "TX"; } }
        public override string Description { get { return "Digital Works texture format"; } }
        public override uint     Signature { get { return 0x00035854; } } // 'TX'

        const int BlockSize = 256;

        public TxFormat ()
        {
            Extensions = new string[] { "tmx", "tx" };
            Signatures = new uint[] { 0x00035854, 0x00025854, 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            if (!header.AsciiEqual ("TX"))
                return null;
            byte bpp = header[6];
            if (bpp < 1 || bpp > 4)
                return null;
            uint w_blocks = header.ToUInt16 (2);
            uint h_blocks = header.ToUInt16 (4);
            return new ImageMetaData {
                Width  = w_blocks * BlockSize,
                Height = h_blocks * BlockSize,
                BPP    = bpp * 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x10;
            int block_stride = BlockSize * (info.BPP / 8);
            int stride = (int)info.Width * (info.BPP / 8);
            var pixels = new byte[stride * (int)info.Height];
            int x_blocks = (int)info.Width / BlockSize;
            int y_blocks = (int)info.Height / BlockSize;
            int dst_row = 0;
            for (int y = 0; y < y_blocks; ++y)
            {
                int dst_col = dst_row;
                for (int x = 0; x < x_blocks; ++x)
                {
                    int dst = dst_col;
                    for (int i = 0; i < BlockSize; ++i)
                    {
                        file.Read (pixels, dst, block_stride);
                        dst += stride;
                    }
                    dst_col += block_stride;
                }
                dst_row += stride * BlockSize;
            }
            BitmapPalette palette = null;
            if (8 == info.BPP)
                palette = ReadPalette (file.AsStream, 0x100, PaletteFormat.BgrA);
            PixelFormat format =  8 == info.BPP ? PixelFormats.Indexed8
                               : 24 == info.BPP ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
            return ImageData.Create (info, format, palette, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("TxFormat.Write not implemented");
        }
    }
}
