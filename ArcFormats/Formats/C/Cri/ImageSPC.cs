using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Cri
{
    [Export(typeof(ImageFormat))]
    public class SpcFormat : XtxFormat
    {
        public override string         Tag { get { return "SPC/Xbox360"; } }
        public override string Description { get { return "CRI MiddleWare compressed texture format"; } }
        public override uint     Signature { get { return 0; } }

        public SpcFormat ()
        {
            Signatures = new uint[] { 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            uint unpacked_size = stream.Signature;
            if (unpacked_size <= 0x20 || unpacked_size > 0x5000000) // ~83MB
                return null;
            stream.Position = 4;
            using (var lzss = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
            using (var input = new SeekableStream (lzss))
            using (var xtx = new BinaryStream (input, stream.Name))
                return base.ReadMetaData (xtx);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 4;
            using (var lzss = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
            using (var input = new SeekableStream (lzss))
            using (var xtx = new BinaryStream (input, stream.Name))
                return base.Read (xtx, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SpcFormat.Write not implemented");
        }
    }
}
