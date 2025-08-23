using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [030620][Gipsy] Angel Gather

namespace GameRes.Formats.Rina
{
    internal class RadMetaData : ImageMetaData
    {
        public bool IsCompressed;
    }

    [Export(typeof(ImageFormat))]
    public class RadFormat : ImageFormat
    {
        public override string         Tag { get { return "RAD"; } }
        public override string Description { get { return "Rina engine image format"; } }
        public override uint     Signature { get { return 0x304152; } }

        public RadFormat ()
        {
            Signatures = new uint[] { 0x304152, 0x444152 }; // 'RA0', 'RAD'
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            return new RadMetaData {
                Width = 640, Height = 480, BPP = 24,
                IsCompressed = file.Signature == 0x304152,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (RadMetaData)info;
            file.Position = 4;
            int stride = info.iWidth * 3;
            var pixels = new byte[stride * info.iHeight];
            if (meta.IsCompressed)
                UnpackRgb (file, pixels);
            else
                file.Read (pixels, 0, pixels.Length);
            if (file.PeekByte() == -1)
                return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, stride);

            var alpha = new byte[info.iWidth * info.iHeight];
            if (meta.IsCompressed)
                UnpackAlpha (file, alpha);
            else
                file.Read (alpha, 0, alpha.Length);

            stride = info.iWidth * 4;
            var output = new byte[stride * info.iHeight];
            int src = 0;
            int asrc = 0;
            for (int dst = 0; dst < output.Length; dst += 4)
            {
                output[dst  ] = pixels[src++];
                output[dst+1] = pixels[src++];
                output[dst+2] = pixels[src++];
                output[dst+3] = alpha[asrc++];
            }
            return ImageData.CreateFlipped (info, PixelFormats.Bgra32, null, output, stride);
        }

        void UnpackRgb (IBinaryStream file, byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                if (file.Read (output, dst, 3) != 3)
                    break;
                int count = 1;
                int pixel = output.ToInt24 (dst);
                if (0 == pixel)
                {
                    count = file.ReadUInt8();
                }
                dst += count * 3;
            }
        }

        void UnpackAlpha (IBinaryStream file, byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                int v = file.ReadByte();
                if (-1 == v)
                    break;
                int count = file.ReadByte();
                if (-1 == count)
                    break;
                count = System.Math.Min (count, output.Length - dst);
                while (count --> 0)
                    output[dst++] = (byte)v;
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("RadFormat.Write not implemented");
        }
    }
}
