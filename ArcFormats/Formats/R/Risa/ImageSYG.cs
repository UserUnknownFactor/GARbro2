using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.WestVision
{
    internal class SygMetaData : ImageMetaData
    {
        public uint AlphaOffset;
    }

    [Export(typeof(ImageFormat))]
    public class SygFormat : ImageFormat
    {
        public override string         Tag { get { return "SYG"; } }
        public override string Description { get { return "Risa game platform system image"; } }
        public override uint     Signature { get { return 0x47595324; } } // '$SYG'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x20);
            uint alpha_offset = header.ToUInt32 (0x1C);
            return new SygMetaData
            {
                Width  = header.ToUInt32 (0x10),
                Height = header.ToUInt32 (0x14),
                BPP = 0 == alpha_offset ? 24 : 32,
                AlphaOffset = alpha_offset,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (SygMetaData)info;
            int pixel_count = (int)meta.Width * (int)meta.Height;
            var pixels = new byte[pixel_count * 3];
            stream.Position = 0x20;
            stream.Read (pixels, 0, pixels.Length);
            var format = PixelFormats.Bgr24;
            if (meta.AlphaOffset != 0)
            {
                var alpha = new byte[pixel_count];
                stream.Position = 0x20 + meta.AlphaOffset;
                if (alpha.Length == stream.Read (alpha, 0, alpha.Length))
                {
                    var output = new byte[pixel_count * 4];
                    int dst = 0;
                    int src = 0;
                    for (int i = 0; i < pixel_count; ++i)
                    {
                        output[dst++] = pixels[src++];
                        output[dst++] = pixels[src++];
                        output[dst++] = pixels[src++];
                        output[dst++] = alpha[i];
                    }
                    pixels = output;
                    format = PixelFormats.Bgra32;
                }
            }
            return ImageData.Create (info, format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SygFormat.Write not implemented");
        }
    }
}
