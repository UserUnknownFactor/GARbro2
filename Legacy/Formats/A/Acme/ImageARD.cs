using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [031212][Acme] Hageshiku Botebara! Senpai, Watashi no Ko, Mitomete Kudasai!!
// [050325][Acme] Project Sex Shuudan Ninshin ~Athlete o Haramasero!~

namespace GameRes.Formats.Acme
{
    [Export(typeof(ImageFormat))]
    public class ArdFormat : ImageFormat
    {
        public override string         Tag { get { return "ARD"; } }
        public override string Description { get { return "Acme image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("ARD") || file.Length != 0x12C000)
                return null;
            return new ImageMetaData { Width = 640, Height = 480, BPP = 32 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var pixels = file.ReadBytes (0x12C000);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte a = pixels[i];
                pixels[i  ] = pixels[i+1];
                pixels[i+1] = pixels[i+2];
                pixels[i+2] = pixels[i+3];
                pixels[i+3] = a;
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ArdFormat.Write not implemented");
        }
    }
}
