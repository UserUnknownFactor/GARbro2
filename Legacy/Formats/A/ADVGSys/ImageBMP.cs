using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.AdvgSys
{
    [Export(typeof(ImageFormat))]
    public class AdvgFormat : ImageFormat
    {
        public override string         Tag { get { return "BMP/ADVG"; } }
        public override string Description { get { return "Compressed bitmap format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0xA);
            if (!((header[4] & 0xF) == 0xF && header.AsciiEqual (5, "BM")))
                return null;
            using (var input = OpenBitmapStream (file))
                return Bmp.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = OpenBitmapStream (file, true))
                return Bmp.Read (input, info);
        }

        internal IBinaryStream OpenBitmapStream (IBinaryStream input, bool seekable = false)
        {
            input.Position = 4;
            Stream bmp = new LzssStream (input.AsStream, LzssMode.Decompress, true);
            if (seekable)
                bmp = new SeekableStream (bmp);
            return new BinaryStream (bmp, input.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AdvgFormat.Write not implemented");
        }
    }
}
