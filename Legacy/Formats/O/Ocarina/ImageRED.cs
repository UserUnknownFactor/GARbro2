using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Ocarina
{
    [Export(typeof(ImageFormat))]
    public class RedFormat : ImageFormat
    {
        public override string         Tag { get { return "RED"; } }
        public override string Description { get { return "Ocarina image format"; } }
        public override uint     Signature { get { return 0x304552; } } // 'RE0'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            return new ImageMetaData { Width = 800, Height = 600, BPP = 32 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 4;
            var pixels = new uint[info.Width * info.Height];
            int dst = 0;
            while (dst < pixels.Length && file.PeekByte() != -1)
            {
                uint px = file.ReadUInt32();
                if (px != 0)
                    pixels[dst++] = px;
                else
                    dst += file.ReadUInt8();
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("RedFormat.Write not implemented");
        }
    }
}
