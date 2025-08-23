using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Regrips
{
    [Export(typeof(ImageFormat))]
    public class PrgFormat : ImageFormat
    {
        public override string         Tag { get { return "PRG"; } }
        public override string Description { get { return "Regrips encrypted image format"; } }
        public override uint     Signature { get { return 0xB8B1AF76; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            using (var input = DecryptStream (file))
                return Png.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = DecryptStream (file))
                return Png.Read (input, info);
        }

        internal IBinaryStream DecryptStream (IBinaryStream input)
        {
            var stream = new XoredStream (input.AsStream, 0xFF, true);
            return new BinaryStream (stream, input.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PrgFormat.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class BrgFormat : PrgFormat
    {
        public override string         Tag { get { return "BRG"; } }
        public override string Description { get { return "Regrips encrypted bitmap"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (2);
            if (header[0] != 0xBD || header[1] != 0xB2)
                return null;
            file.Position = 0;
            using (var input = DecryptStream (file))
                return Bmp.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = DecryptStream (file))
                return Bmp.Read (input, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BrgFormat.Write not implemented");
        }
    }
}
