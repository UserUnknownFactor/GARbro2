using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [030314][Mermaid] Ayakashizoushi ~Oumagatoki no Yume~

namespace GameRes.Formats.Mermaid
{
    [Export(typeof(ImageFormat))]
    public class Gp1Format : ImageFormat
    {
        public override string         Tag { get { return "GP1"; } }
        public override string Description { get { return "Mermaid image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("GP1"))
                return null;
            var header = file.ReadHeader (8);
            uint width  = header.ToUInt32 (0);
            uint height = header.ToUInt32 (4);
            return new ImageMetaData {
                Width  = width,
                Height = height,
                BPP    = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 8;
            int plane_size = info.iWidth * info.iHeight;
            var b = new byte[plane_size];
            UnpackChannel (file, b);
            var g = new byte[plane_size];
            UnpackChannel (file, g);
            var r = new byte[plane_size];
            UnpackChannel (file, r);
            var pixels = new byte[plane_size * 3];
            int dst = 0;
            for (int src = 0; src < plane_size; ++src)
            {
                pixels[dst++] = b[src];
                pixels[dst++] = g[src];
                pixels[dst++] = r[src];
            }
            return ImageData.Create (info, PixelFormats.Bgr24, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Gp1Format.Write not implemented");
        }

        void UnpackChannel (IBinaryStream input, byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                int count = input.ReadByte();
                if (count < 0)
                    break;
                if (count <= 0x32)
                {
                    input.Read (output, dst, count);
                    dst += count;
                }
                else
                {
                    count -= 0x32;
                    byte v = input.ReadUInt8();
                    while (count --> 0)
                        output[dst++] = v;
                }
            }
        }
    }
}
