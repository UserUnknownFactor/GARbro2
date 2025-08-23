using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.KeroQ
{
    [Export(typeof(ImageFormat))]
    public class CbmFormat : ImageFormat
    {
        public override string         Tag { get { return "CBM"; } }
        public override string Description { get { return "KeroQ bitmap format"; } }
        public override uint     Signature { get { return 0x004D4243; } } // 'CBM'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            var length = header.ToUInt32 (0xC);
            if (file.Length - 0x10 != length)
                return null;
            return new ImageMetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP = 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            BitmapPalette palette = null;
            PixelFormat format = PixelFormats.Gray8;
            foreach (var pal_name in GetPaletteNames (info.FileName))
            {
                if (!VFS.FileExists (pal_name))
                    continue;
                try
                {
                    using (var pal = VFS.OpenStream (pal_name))
                    {
                        palette = ReadPalette (pal, 0x100, PaletteFormat.Bgr);
                        format = PixelFormats.Indexed8;
                    }
                }
                catch { /* ignore palette read errors */ }
                break;
            }
            file.Position = 0x10;
            var pixels = file.ReadBytes ((int)info.Width * (int)info.Height);
            return ImageData.Create (info, format, palette, pixels);
        }

        IEnumerable<string> GetPaletteNames (string filename)
        {
            var base_name = Path.GetFileNameWithoutExtension (filename);
            yield return VFS.ChangeFileName (filename, base_name + ".pal");
            if (base_name.Length > 3)
            {
                base_name = base_name.Substring (0, 3);
                yield return VFS.ChangeFileName (filename, base_name + ".pal");
            }
            yield return VFS.ChangeFileName (filename, base_name + "_2.pal");
            yield return VFS.ChangeFileName (filename, base_name + "_1.pal");
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CbmFormat.Write not implemented");
        }
    }
}
