using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Asura
{
    internal class MtgMetaData : ImageMetaData
    {
        public int  DataLength;
        public bool HasAlpha;
    }

    [Export(typeof(ImageFormat))]
    public class MtgFormat : ImageFormat
    {
        public override string         Tag { get { return "MTG"; } }
        public override string Description { get { return "Asura engine image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".mtg"))
                return null;
            var header = file.ReadHeader (0x10);
            int data_length = header.ToInt32 (8);
            if (data_length <= 0 || data_length > file.Length)
                return null;
            int has_alpha = header.ToInt32 (0xC);
            if (has_alpha != 0 && has_alpha != 1)
                return null;
            return new MtgMetaData {
                Width  = header.ToUInt32 (0),
                Height = header.ToUInt32 (4),
                BPP    = 24,
                DataLength = data_length,
                HasAlpha = has_alpha != 0,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (MtgMetaData)info;
            file.Position = 0x10;
            var pixels = file.ReadBytes (meta.DataLength);
            if (!meta.HasAlpha)
                return ImageData.Create (info, PixelFormats.Bgr24, null, pixels);
            var alpha = file.ReadBytes ((int)info.Width * (int)info.Height);
            var output = new byte[4 * alpha.Length];
            int psrc = 0;
            int asrc = 0;
            for (int dst = 0; dst < output.Length; dst += 4)
            {
                output[dst  ] = pixels[psrc++];
                output[dst+1] = pixels[psrc++];
                output[dst+2] = pixels[psrc++];
                output[dst+3] = alpha[asrc++];
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, output);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MtgFormat.Write not implemented");
        }
    }
}
