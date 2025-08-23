using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.System21
{
    [Export(typeof(ImageFormat))]
    public class TexFormat : ImageFormat
    {
        public override string         Tag { get { return "TEX/SZDD"; } }
        public override string Description { get { return "System21 compressed texture format"; } }
        public override uint     Signature { get { return 0x44445A53; } } // 'SZDD'
        public override bool      CanWrite { get { return false; } }

        static readonly ResourceInstance<ImageFormat> DdsFormat = new ResourceInstance<ImageFormat> ("DDS");

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            using (var input = OpenLzStream (file))
            {
                var header = new byte[0x84];
                if (input.Read (header, 0, header.Length) != header.Length)
                    return null;
                if (!header.AsciiEqual (4, "DDS "))
                    return null;
                using (var dds = new BinMemoryStream (header, 4, header.Length - 4))
                    return DdsFormat.Value.ReadMetaData (dds);
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var lz = OpenLzStream (file))
            {
                var skip = new byte[4];
                lz.Read (skip, 0, 4);
                using (var input = new SeekableStream (lz))
                using (var dds = new BinaryStream (input, file.Name))
                    return DdsFormat.Value.Read (dds, info);
            }
        }

        Stream OpenLzStream (IBinaryStream input)
        {
            input.Position = 0xE;
            var lz = new LzssStream (input.AsStream, LzssMode.Decompress, true);
            lz.Config.FrameSize = 0x1000;
            lz.Config.FrameFill = 0x20;
            lz.Config.FrameInitPos = 0x1000 - 0x10;
            return lz;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("TexFormat.Write not implemented");
        }
    }
}
