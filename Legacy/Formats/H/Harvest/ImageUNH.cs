using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [021206][MyHarvest] Idol Mahjong Final Romance 4

namespace GameRes.Formats.MyHarvest
{
    [Export(typeof(ImageFormat))]
    public class UnhFormat : ImageFormat
    {
        public override string         Tag => "UNH";
        public override string Description => "MyHarvest image format";
        public override uint     Signature => 0x30484E55; // 'UNH0'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            if (header.ToInt32 (4) != 1)
                return null;
            return new ImageMetaData {
                Width  = header.ToUInt32 (0x10),
                Height = header.ToUInt32 (0x14),
                BPP    = 16,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x44;
            var pixels = new ushort[info.iWidth * info.iHeight];
            var frame = new ushort[0x1000];
            int frame_pos = 0;
            int dst = 0;
            byte mask = 0;
            int ctl = 0;
            while (dst < pixels.Length)
            {
                mask <<= 1;
                if (0 == mask)
                {
                    ctl = file.ReadByte();
                    if (-1 == ctl)
                        break;
                    mask = 1;
                }
                ushort word = file.ReadUInt16();
                if ((ctl & mask) == 0)
                {
                    pixels[dst++] = frame[frame_pos++ & 0xFFF] = word;
                }
                else
                {
                    int offset = word >> 4;
                    int count = (word & 0xF) + 2;
                    while (count --> 0)
                    {
                        ushort u = frame[offset++ & 0xFFF];
                        pixels[dst++] = frame[frame_pos++ & 0xFFF] = u;
                    }
                }
            }
            return ImageData.Create (info, PixelFormats.Bgr565, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("UnhFormat.Write not implemented");
        }
    }
}
