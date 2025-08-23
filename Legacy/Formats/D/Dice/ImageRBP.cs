using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [000623][Marimo] Setsunai

namespace GameRes.Formats.Dice
{
    internal class RbpMetaData : ImageMetaData
    {
        public int  DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class RbpFormat : ImageFormat
    {
        public override string         Tag { get { return "RBP"; } }
        public override string Description { get { return "DiceSystem image format"; } }
        public override uint     Signature { get { return 0x31504252; } } // 'RBP1'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            return new RbpMetaData {
                BPP = header.ToInt32 (4) == 1 ? 24 : 32,
                Width = header.ToUInt32 (8),
                Height = header.ToUInt32 (0xC),
                DataOffset = header.ToInt32 (0x10),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (RbpMetaData)info;
            file.Position = meta.DataOffset;
            int stride = 4 * (int)meta.Width;
            var pixels = new byte[stride * (int)meta.Height];
            if (24 == meta.BPP)
            {
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    int pixel = file.ReadInt24();
                    pixels[i  ] = (byte) (pixel << 3);
                    pixels[i+1] = (byte)((pixel >> 3) & 0xFC);
                    pixels[i+2] = (byte)((pixel >> 8) & 0xF8);
                    pixels[i+3] = (byte)((pixel >> 16) * 0xFF / 0x3F);
                }
            }
            else
            {
                file.Read (pixels, 0, pixels.Length);
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    byte alpha = pixels[i];
                    if (alpha != 0)
                        pixels[i] = (byte)(alpha * 0xFF / 0x3F);
                }
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("RbpFormat.Write not implemented");
        }
    }
}
