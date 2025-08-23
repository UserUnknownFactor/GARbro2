using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Silky
{
    [Export(typeof(ImageFormat))]
    public class GrdFormat : ImageFormat
    {
        public override string         Tag { get { return "GRD"; } }
        public override string Description { get { return "Silky's compressed bitmap format"; } }
        public override uint     Signature { get { return 0x5f504d43u; } } // 'CMP_'

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GrdFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            using (var bmp = DecompressStream (stream))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var bmp = DecompressStream (stream))
                return Bmp.Read (bmp, info);
        }

        internal IBinaryStream DecompressStream (IBinaryStream stream)
        {
            stream.Position = 12;
            var input = new LzssStream (stream.AsStream, LzssMode.Decompress, true);
            input.Config.FrameFill = 0x20;
            return new BinaryStream (input, stream.Name);
        }
    }
}
