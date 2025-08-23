using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.FC01
{
    [Export(typeof(ImageFormat))]
    public class Wm2Format : ImageFormat
    {
        public override string         Tag { get { return "WM2"; } }
        public override string Description { get { return "F&C Co. bitmap mask"; } }
        public override uint     Signature { get { return 0x302E32; } } // '2.0'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (12);
            uint width  = header.ToUInt32 (4);
            uint height = header.ToUInt32 (8);
            if (0 == width || width > 0x8000 || 0 == height || height > 0x8000)
                return null;
            return new ImageMetaData { Width = width, Height = height, BPP = 8 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var output = new byte[info.Width * info.Height];
            var table = new int[info.Height * 4];
            file.Position = 12;
            for (int i = 0; i < table.Length; ++i)
                table[i] = file.ReadInt32();
            int stride = (int)info.Width;
            int height = (int)info.Height;
            int row = 0;
            int data_pos = height * 16 + 12;
            int dst = 0;
            for (int y = 0; y < height; ++y)
            {
                if (table[row] != 0)
                {
                    file.Position = table[row+3] + data_pos;
                    file.Read (output, dst+table[row+1], table[row+2]);
                }
                row += 4;
                dst += stride;
            }
            return ImageData.Create (info, PixelFormats.Gray8, null, output);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Wm2Format.Write not implemented");
        }
    }
}
