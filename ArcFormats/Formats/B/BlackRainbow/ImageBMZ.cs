using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Utility;
using GameRes.Compression;

namespace GameRes.Formats.BlackRainbow
{
    [Export(typeof(ImageFormat))]
    public class BmzFormat : ImageFormat
    {
        public override string         Tag { get { return "BMZ"; } }
        public override string Description { get { return "Compressed bitmap format"; } }
        public override uint     Signature { get { return 0x33434c5au; } } // 'ZLC3'
        public override bool      CanWrite { get { return true; } }

        public override void Write (Stream file, ImageData image)
        {
            using (var bmp = new MemoryStream())
            {
                Bmp.Write (bmp, image);
                using (var output = new BinaryWriter (file, Encoding.ASCII, true))
                {
                    output.Write (Signature);
                    output.Write ((uint)bmp.Length);
                }
                bmp.Position = 0;
                using (var zstream = new ZLibStream (file, CompressionMode.Compress, CompressionLevel.Level9, true))
                    bmp.CopyTo (zstream);
            }
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            using (var zstream = new ZLibStream (file.AsStream, CompressionMode.Decompress, true))
            using (var bmp = new BinaryStream (zstream, file.Name))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Seek (8, SeekOrigin.Current);
            using (var zstream = new ZLibStream (file.AsStream, CompressionMode.Decompress, true))
            using (var input = new SeekableStream (zstream))
            using (var bmp = new BinaryStream (input, file.Name))
                return Bmp.Read (bmp, info);
        }
    }
}
