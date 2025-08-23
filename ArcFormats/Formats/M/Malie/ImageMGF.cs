using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Malie
{
    [Export(typeof(ImageFormat))]
    public class MgfFormat : PngFormat
    {
        public override string         Tag { get { return "MGF"; } }
        public override string Description { get { return "Malie engine image format"; } }
        public override uint     Signature { get { return 0x696C614D; } } // 'Mali'
        public override bool      CanWrite { get { return true; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (8).ToArray();
            if (!Binary.AsciiEqual (header, "MalieGF"))
                return null;
            Buffer.BlockCopy (PNG_HEADER, 0, header, 0, 8);

            using (var data = new StreamRegion (stream.AsStream, 8, true))
            using (var pre = new PrefixStream (header, data))
            using (var png = new BinaryStream (pre, stream.Name))
                return base.ReadMetaData (png);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var header = PNG_HEADER.Clone() as byte[];
            using (var data = new StreamRegion (stream.AsStream, 8, true))
            using (var pre = new PrefixStream (header, data))
            using (var png = new BinaryStream (pre, stream.Name))
                return base.Read (png, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var png = new MemoryStream())
            {
                base.Write (png, image);
                var buffer = png.GetBuffer();
                Encoding.ASCII.GetBytes ("MalieGF\0", 0, 8, buffer, 0);
                file.Write (buffer, 0, (int)png.Length);
            }
        }
    }
}
