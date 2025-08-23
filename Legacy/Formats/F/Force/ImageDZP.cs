using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

// [990807][Force] Hajimete no Otsukai ~first adventure of little witch~

namespace GameRes.Formats.Force
{
    [Export(typeof(ImageFormat))]
    public class DzpFormat : ImageFormat
    {
        public override string         Tag { get { return "DZP"; } }
        public override string Description { get { return "Force image format"; } }
        public override uint     Signature { get { return 0; } }

        public DzpFormat ()
        {
            Signatures = new uint[] { 24, 8 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".dzp"))
                return null;
            var header = file.ReadHeader (12);
            int bpp = header.ToInt32 (0);
            uint width  = header.ToUInt32 (4);
            uint height = header.ToUInt32 (8);
            if (0 == width || width > 0x1000 || 0 == height || height > 0x1000)
                return null;
            return new ImageMetaData { Width = width * 4, Height = height * 4, BPP = bpp };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 12;
            BitmapPalette palette = null;
            if (8 == info.BPP)
                palette = ReadPalette (file.AsStream);
            int stride = (int)info.Width * (info.BPP / 8);
            var pixels = new byte[stride * (int)info.Height];
            PixelFormat format;
            if (8 == info.BPP)
            {
                Unpack8BPP (file, pixels);
                format = PixelFormats.Indexed8;
            }
            else if (24 == info.BPP)
            {
                Unpack24BPP (file, pixels);
                format = PixelFormats.Bgr24;
            }
            else
                throw new InvalidFormatException();
            return ImageData.Create (info, format, palette, pixels, stride);
        }

        void Unpack8BPP (IBinaryStream input, byte[] output)
        {
            int dst = 0;
            while (dst < output.Length && input.PeekByte() != -1)
            {
                byte v = input.ReadUInt8();
                int count = input.ReadByte();
                for (int i = 0; i < count; ++i)
                    output[dst++] = v;
            }
        }

        void Unpack24BPP (IBinaryStream input, byte[] output)
        {
            int dst = 0;
            while (dst < output.Length && input.PeekByte() != -1)
            {
                input.Read (output, dst, 3);
                int count = input.ReadByte();
                if (count > 0)
                {
                    count *= 3;
                    Binary.CopyOverlapped (output, dst, dst+3, count-3);
                    dst += count;
                }
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DzpFormat.Write not implemented");
        }
    }
}
