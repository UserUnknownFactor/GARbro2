using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Ism
{
    [Export(typeof(ImageFormat))]
    [ExportMetadata("Priority", -1)] // deprioritize
    public class PngIsmFormat : ImageFormat
    {
        public override string         Tag { get => "PNG/ISM"; }
        public override string Description { get => "ISM engine PNG image"; }
        public override uint     Signature { get => 0x474e5089; }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            // format only applied when extracting from related archive
            if (!VFS.IsVirtual || VFS.CurrentArchive.Tag != "ISA")
                return null;
            return Png.ReadMetaData (file);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var decoder = new PngBitmapDecoder (file.AsStream,
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource bitmap = decoder.Frames[0];
            if (bitmap.Format != PixelFormats.Bgra32)
                return new ImageData (bitmap, info);
            int stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels (pixels, stride, 0);
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] ^= 0xFF;
            }
            return ImageData.Create (info, bitmap.Format, bitmap.Palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PngIsmFormat.Write not implemented");
        }
    }
}
