using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [991008][Easy Mode] Zero Jikan e...

namespace GameRes.Formats.HillField
{
    [Export(typeof(ImageFormat))]
    public class ImgFormat : ImageFormat
    {
        public override string         Tag { get { return "IMG/HF"; } }
        public override string Description { get { return "Hill Field script system image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".img"))
                return null;
            var header = file.ReadHeader (8);
            uint width = header.ToUInt32 (0);
            uint height = header.ToUInt32 (4);
            uint bitmap_size = width * height * 3;
            if (bitmap_size != file.Length - 10 && bitmap_size != file.Length - 8)
                return null;
            return new ImageMetaData { Width = width, Height = height, BPP = 24 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 8;
            int stride = info.iWidth * 3;
            var pixels = new byte[stride * info.iHeight];
            file.Read (pixels, 0, pixels.Length);
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ImgFormat.Write not implemented");
        }
    }
}
