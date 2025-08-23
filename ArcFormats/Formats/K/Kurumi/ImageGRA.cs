using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Kurumi
{
    [Export(typeof(ImageFormat))]
    public class GraFormat : ImageFormat
    {
        public override string         Tag { get { return "GRA/VS"; } }
        public override string Description { get { return "Virgin Snow image format"; } }
        public override uint     Signature { get { return 0x67726956; } } // 'Virg'

        static readonly byte[] Key = { 0x5A, 0xA5 };

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            if (!header.AsciiEqual ("Virgin Snow Compressed Data 1.0"))
                return null;
            using (var bmp = UnpackStream (file))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x20;
            using (var bmp = UnpackStream (file))
                return Bmp.Read (bmp, info);
        }

        IBinaryStream UnpackStream (IBinaryStream file)
        {
            Stream input = new ByteStringEncryptedStream (file.AsStream, Key, true);
            input = new ZLibStream (input, CompressionMode.Decompress);
            return new BinaryStream (input, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GraFormat.Write not implemented");
        }
    }
}
