using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Gaia
{
    [Export(typeof(ImageFormat))]
    public class HiddenJpegFormat : ImageFormat
    {
        public override string         Tag { get { return "JPG/HIDDEN"; } }
        public override string Description { get { return "Gaia obfuscated JPEG image"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if ((file.Signature & 0xFFFFFF) != 0xFDFF00)
                return null;
            using (var jpeg = OpenAsJpeg (file))
                return Jpeg.ReadMetaData (jpeg);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var jpeg = OpenAsJpeg (file))
                return Jpeg.Read (jpeg, info);
        }

        IBinaryStream OpenAsJpeg (IBinaryStream file)
        {
            var input = new StreamRegion (file.AsStream, 100, true);
            return new BinaryStream (input, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("HiddenJpegFormat.Write not implemented");
        }
    }
}
