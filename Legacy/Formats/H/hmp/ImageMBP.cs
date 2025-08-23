using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [000421][Sepia] Rinjin Mousou ~Danchizoku no Hirusagari~
// [000623][Sweet] Depaga ~Service Angel~

namespace GameRes.Formats.Hmp
{
    [Export(typeof(ImageFormat))]
    public class MbpFormat : ImageFormat
    {
        public override string         Tag { get { return "MBP"; } }
        public override string Description { get { return "h.m.p bitmap format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".MBP"))
                return null;
            var header = file.ReadHeader (8);
            uint w = header.ToUInt32 (0);
            uint h = header.ToUInt32 (4);
            if (8 + w * h * 2 != file.Length)
                return null;
            return new ImageMetaData {
                Width = w,
                Height = h,
                BPP = 15,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 8;
            int stride = (int)info.Width * 2;
            var pixels = file.ReadBytes (stride * (int)info.Height);
            return ImageData.Create (info, PixelFormats.Bgr555, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MbpFormat.Write not implemented");
        }
    }
}
