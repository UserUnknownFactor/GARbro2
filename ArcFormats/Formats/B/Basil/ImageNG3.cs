using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Basil
{
    [Export(typeof(ImageFormat))]
    public class Ng3Format : ImageFormat
    {
        public override string         Tag { get { return "NG3"; } }
        public override string Description { get { return "BasiL image format"; } }
        public override uint     Signature { get { return 0x33474E; } } // 'NG3'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0xC);
            return new ImageMetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP    = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0xC;
            var palette = ReadColorMap (file.AsStream, 256, PaletteFormat.Bgr);
            int stride = (int)info.Width * 3;
            var pixels = new byte[stride * (int)info.Height];
            int dst = 0;
            while (dst < pixels.Length)
            {
                int ctl = file.PeekByte();
                if (-1 == ctl)
                    break;
                if (1 == ctl)
                {
                    file.ReadByte();
                    int idx = file.ReadByte();
                    var color = palette[idx];
                    pixels[dst  ] = color.B;
                    pixels[dst+1] = color.G;
                    pixels[dst+2] = color.R;
                    dst += 3;
                }
                else if (2 == ctl)
                {
                    file.ReadByte();
                    int idx = file.ReadByte();
                    int count = file.ReadByte();
                    var color = palette[idx];
                    for (int i = 0; i < count; ++i)
                    {
                        pixels[dst  ] = color.B;
                        pixels[dst+1] = color.G;
                        pixels[dst+2] = color.R;
                        dst += 3;
                    }
                }
                else
                {
                    file.Read (pixels, dst, 3);
                    dst += 3;
                }
            }
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Ng3Format.Write not implemented");
        }
    }
}
