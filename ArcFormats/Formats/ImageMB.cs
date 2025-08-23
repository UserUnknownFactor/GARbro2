using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats
{
    [Export(typeof(ImageFormat))]
    public class MbImageFormat : ImageFormat
    {
        public override string         Tag { get { return "BMP/MB"; } }
        public override string Description { get { return "Obfuscated bitmap"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return true; } }

        public MbImageFormat ()
        {
            Extensions = new[] { "bmp", "gra", "xxx" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            int c1 = stream.ReadByte();
            int c2 = stream.ReadByte();
            // MB/MC/MK/CL/XX
            switch (c1)
            {
            case 'M':
                if ('B' != c2 && 'C' != c2 && 'K' != c2)
                    return null;
                break;
            case 'C':
                if ('L' != c2)
                    return null;
                break;
            case 'X':
                if ('X' != c2)
                    return null;
                break;
            default:
                return null;
            }
            using (var bmp = OpenAsBitmap (stream))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var bmp = OpenAsBitmap (stream))
                return Bmp.Read (bmp, info);
        }

        protected IBinaryStream OpenAsBitmap (IBinaryStream input)
        {
            var header = new byte[2] { (byte)'B', (byte)'M' };
            Stream stream = new StreamRegion (input.AsStream, 2, true);
            stream = new PrefixStream (header, stream);
            return new BinaryStream (stream, input.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var bmp = new MemoryStream())
            {
                Bmp.Write (bmp, image);
                file.WriteByte ((byte)'M');
                file.WriteByte ((byte)'B');
                bmp.Position = 2;
                bmp.CopyTo (file);
            }
        }
    }
}
