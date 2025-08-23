using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [000324][Kurumi] 4m

namespace GameRes.Formats.Kurumi
{
    [Export(typeof(ImageFormat))]
    public class GraFormat : ImageFormat
    {
        public override string         Tag { get { return "GRA/KURUMI"; } }
        public override string Description { get { return "Kurumi encrypted image"; } }
        public override uint     Signature { get { return 0xEE2FA397; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14).ToArray();
            Decrypt (header);
            if (header.ToInt32 (0) != 0x10)
                return null;
            return new ImageMetaData {
                Width  = header.ToUInt16 (0x10),
                Height = header.ToUInt16 (0x12),
                OffsetX = header.ToInt16 (0xC),
                OffsetY = header.ToInt16 (0xE),
                BPP = 16,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var data = file.ReadBytes ((int)file.Length);
            Decrypt (data);
            var pixels = new byte[info.iWidth * info.iHeight * 2];
            int src = 0x14;
            int dst = 0;
            while (dst < pixels.Length)
            {
                byte p0 = data[src++];
                byte p1 = data[src++];
                pixels[dst++] = (byte)((p1 >> 2) & 0x1F | p0 & 0xE0);
                pixels[dst++] = (byte)((p0 & 0x1F) << 2 | p1 & 3);
            }
            return ImageData.Create (info, PixelFormats.Bgr555, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GraFormat.Write not implemented");
        }

        void Decrypt (byte[] data)
        {
            int seed = 34567;
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] -= (byte)(seed >> 8);
                seed = 5 * seed - 1;
            }
        }
    }
}
