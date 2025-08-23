using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Nekopunch
{
    [Export(typeof(ImageFormat))]
    public class PbmFormat : ImageFormat
    {
        public override string         Tag { get { return "PBM"; } }
        public override string Description { get { return "Studio Nekopunch compressed bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".pbm"))
                return null;
            var header = file.ReadHeader (8);
            if ((header[4] & 7) != 7 || !header.AsciiEqual (5, "BM"))
                return null;
            using (var bmp = OpenBitmapStream (file, header.ToUInt32 (0)))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var bmp = OpenBitmapStream (file, file.Signature))
                return Bmp.Read (bmp, info);
        }

        IBinaryStream OpenBitmapStream (IBinaryStream file, uint unpacked_size)
        {
            file.Position = 4;
            Stream input = new LzssStream (file.AsStream, LzssMode.Decompress, true);
            input = new LimitStream (input, unpacked_size, StreamOption.Fill);
            return new BinaryStream (input, file.Name); 
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PbmFormat.Write not implemented");
        }
    }
}
