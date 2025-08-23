using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Softpal
{
    [Export(typeof(ImageFormat))]
    public class BpicFormat : ImageFormat
    {
        public override string         Tag { get { return "BPIC"; } }
        public override string Description { get { return "Softpal engine image format"; } }
        public override uint     Signature { get { return 0x43495042; } } // 'BPIC'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x10);
            int pixel_size = header.ToInt32 (12);
            if (pixel_size != 4 && pixel_size != 3 && pixel_size != 1)
                return null;
            return new ImageMetaData
            {
                Width   = header.ToUInt32 (4),
                Height  = header.ToUInt32 (8),
                BPP     = pixel_size * 8,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 0x10;
            int pixel_size = info.BPP/8;
            var pixels = new byte[(int)info.Width * (int)info.Height * pixel_size];
            if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                throw new EndOfStreamException();
            if (pixel_size > 1)
            {
                for (int i = 2; i < pixels.Length; i += pixel_size)
                {
                    byte t = pixels[i];
                    pixels[i] = pixels[i-2];
                    pixels[i-2] = t;
                }
            }
            PixelFormat format = 24 == info.BPP ? PixelFormats.Bgr24 :
                                  8 == info.BPP ? PixelFormats.Gray8 : PixelFormats.Bgra32;
            return ImageData.Create (info, format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BpicFormat.Write not implemented");
        }
    }
}
