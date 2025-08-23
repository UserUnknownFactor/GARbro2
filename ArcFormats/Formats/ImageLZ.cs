using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats
{
    [Export(typeof(ImageFormat))]
    public class Bm_Format : ImageFormat
    {
        public override string         Tag { get { return "BM_"; } }
        public override string Description { get { return "LZ-compressed bitmap"; } }
        public override uint     Signature { get { return 0x44445A53u; } } // 'SZDD'
        public override bool      CanWrite { get { return false; } }

        public Bm_Format ()
        {
            Extensions = new string[] { "bm_", "gpp", "meh", "gr_" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 0x0e;
            using (var lz = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
            {
                lz.Config.FrameSize = 0x1000;
                lz.Config.FrameFill = 0x20;
                lz.Config.FrameInitPos = 0x1000 - 0x10;
                using (var bmp = new BinaryStream (lz, stream.Name))
                    return Bmp.ReadMetaData (bmp);
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 0x0e;
            using (var lz = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
            {
                lz.Config.FrameSize = 0x1000;
                lz.Config.FrameFill = 0x20;
                lz.Config.FrameInitPos = 0x1000 - 0x10;
                using (var bmp = new BinaryStream (lz, stream.Name))
                    return Bmp.Read (bmp, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("Bm_Format.Write not implemented");
        }
    }
}
