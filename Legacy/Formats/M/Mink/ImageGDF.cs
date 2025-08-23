using System.ComponentModel.Composition;
using System.IO;

// [000225][Mink] Wonpara Wars

namespace GameRes.Formats.Mink
{
    [Export(typeof(ImageFormat))]
    public class GdfFormat : MbImageFormat
    {
        public override string         Tag { get { return "GDF"; } }
        public override string Description { get { return "Mink obfuscated bitmap"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            int c1 = stream.ReadByte();
            int c2 = stream.ReadByte();
            if ('G' != c1 || 'D' != c2)
                return null;
            using (var bmp = OpenAsBitmap (stream))
                return Bmp.ReadMetaData (bmp);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GdfFormat.Write not implemented");
        }
    }
}
