using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.ScenePlayer
{
    [Export(typeof(ImageFormat))]
    public class PmpFormat : ImageFormat
    {
        public override string         Tag { get { return "PMP"; } }
        public override string Description { get { return "ScenePlayer compressed bitmap format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return true; } }

        public override void Write (Stream file, ImageData image)
        {
            using (var output = new XoredStream (file, 0x21, true))
            using (var zstream = new ZLibStream (output, CompressionMode.Compress, CompressionLevel.Level9))
                Bmp.Write (zstream, image);
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            int first = stream.PeekByte() ^ 0x21;
            if (first != 0x78) // doesn't look like zlib stream
                return null;

            using (var input = new XoredStream (stream.AsStream, 0x21, true))
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
            using (var bmp = new BinaryStream (zstream, stream.Name))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var input = new XoredStream (stream.AsStream, 0x21, true))
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
            using (var bmp = new BinaryStream (zstream, stream.Name))
                return Bmp.Read (bmp, info);
        }
    }
}
