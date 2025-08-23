using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Mina
{
    [Export(typeof(ImageFormat))]
    public class MdFormat : ImageFormat
    {
        public override string         Tag { get { return "MD"; } }
        public override string Description { get { return "Mina compressed bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if ((file.Signature & 0xFFFF) != 0x444D) // 'MD'
                return null;
            using (var bmp = OpenBitmap (file))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var bmp = OpenBitmap (file))
                return Bmp.Read (bmp, info);
        }

        IBinaryStream OpenBitmap (IBinaryStream input)
        {
            input.Position = 0xA;
            var stream = new LzssStream (input.AsStream, LzssMode.Decompress, true);
            return new BinaryStream (stream, input.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MdFormat.Write not implemented");
        }
    }
}
