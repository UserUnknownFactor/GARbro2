using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Brownie
{
    [Export(typeof(ImageFormat))]
    public class NgwFormat : MbImageFormat
    {
        public override string         Tag { get { return "NGW"; } }
        public override string Description { get { return "Brownie obfuscated bitmap"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (2);
            if ('N' != header[0] || 'G' != header[1])
                return null;
            using (var bmp = OpenAsBitmap (file))
                return Bmp.ReadMetaData (bmp);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("NgwFormat.Write not implemented");
        }
    }
}
