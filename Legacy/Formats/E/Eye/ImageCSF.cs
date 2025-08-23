using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Eye
{
    [Export(typeof(ImageFormat))]
    public class CsfFormat : ImageFormat
    {
        public override string         Tag { get { return "CSF"; } }
        public override string Description { get { return "Compressed bitmap format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0xB);
            if (!header.AsciiEqual ("CSF"))
                return null;
            using (var input = UnpackedStream (file))
                return Bmp.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = UnpackedStream (file))
                return Bmp.Read (input, info);
        }

        IBinaryStream UnpackedStream (IBinaryStream input)
        {
            input.Position = 0xB;
            var unpacked = new LzssStream (input.AsStream, LzssMode.Decompress, true);
            return new BinaryStream (unpacked, input.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CsfFormat.Write not implemented");
        }
    }
}
