using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Prime
{
    [Export(typeof(ImageFormat))]
    public class ThpFormat : ImageFormat
    {
        public override string         Tag { get { return "THP"; } }
        public override string Description { get { return "Prime Soft image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("THP"))
                return null;
            var header = file.ReadHeader (4);
            ushort width  = header.ToUInt16 (0);
            ushort height = header.ToUInt16 (0);
            if (width == 0 || width > 0x4000 || height == 0 || height > 0x4000)
                return null;
            return new ImageMetaData { Width = width, Height = height, BPP = 8 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 4;
            var palette = ReadPalette (file.AsStream, 0x100, PaletteFormat.Bgr);
            int stride = (info.iWidth + 3) & ~3;
            var pixels = new byte[info.iHeight * stride];
            int length = info.iHeight * info.iWidth;
            int dst = 0;
            while (dst < length)
            {
                byte px = file.ReadUInt8();
                int next = file.PeekByte();
                if (px == next)
                {
                    file.ReadByte();
                    int count = file.ReadByte() + 1;
                    while (count --> 0)
                    {
                        pixels[dst++] = px;
                    }
                }
                else
                    pixels[dst++] = px;
            }
            return ImageData.CreateFlipped (info, PixelFormats.Indexed8, palette, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ThpFormat.Write not implemented");
        }
    }
}
