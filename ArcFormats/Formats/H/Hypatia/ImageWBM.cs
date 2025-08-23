using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Hypatia
{
    [Export(typeof(ImageFormat))]
    public class WbmFormat : ImageFormat
    {
        public override string         Tag { get { return "WBM/HYPATIA"; } }
        public override string Description { get { return "Hypatia bitmap format"; } }
        public override uint     Signature { get { return 0x4D425721; } } // '!WBM'

        public WbmFormat ()
        {
            Extensions = new[] { "wbm"/*, "dat"*/ };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (12);
            return new ImageMetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP    = 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 12;
            var pixels = file.ReadBytes ((int)info.Width * (int)info.Height);
            var format = PixelFormats.Gray8;
            BitmapPalette palette = null;
            var palette_name = VFS.ChangeFileName (file.Name, "data.act");
            if (VFS.FileExists (palette_name))
            {
                using (var pal_file = VFS.OpenStream (palette_name))
                    palette = ReadPalette (pal_file, 0x100, PaletteFormat.Rgb);
                format = PixelFormats.Indexed8;
            }
            return ImageData.Create (info, format, palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("WbmFormat.Write not implemented");
        }
    }
}
