using GameRes.Compression;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Jam
{
    [Export(typeof(ImageFormat))]
    public class HtfFormat : ImageFormat
    {
        public override string         Tag => "HTF";
        public override string Description => "Huffman-compressed bitmap";
        public override uint     Signature => 0;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".HTF"))
                return null;
            int unpacked_size = file.ReadInt32();
            if (unpacked_size <= 0 || unpacked_size > 0x1000000)
                return null;
            using (var huff = new HuffmanStream (file.AsStream, true))
            using (var input = new BinaryStream (huff, file.Name))
            {
                return Bmp.ReadMetaData (input);
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 4;
            using (var input = new HuffmanStream (file.AsStream, true))
            {
                var decoder = new BmpBitmapDecoder (input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return new ImageData (decoder.Frames[0], info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("HtfFormat.Write not implemented");
        }
    }
}
