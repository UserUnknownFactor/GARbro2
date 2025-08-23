using System.ComponentModel.Composition;
using System.Windows.Media;

namespace GameRes.Formats.Hyperspace
{
    /// <summary>
    /// Color depth of 24bpp images simply changed to 16bpp. This extensions reverts it back.
    /// </summary>
    [Export(typeof(IBmpExtension))]
    public class BmpDepthFixer : IBmpExtension
    {
        public ImageData Read (IBinaryStream file, BmpMetaData info)
        {
            if (info.BPP != 0x10 || !file.CanSeek)
                return null;
            int stride = ((int)info.Width * 3 + 3) & ~3;
            int total_24bpp = stride * (int)info.Height;
            if (total_24bpp + info.ImageOffset != file.Length)
                return null;
            file.Position = info.ImageOffset;
            var pixels = file.ReadBytes ((int)total_24bpp);
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, stride);
        }
    }
}
