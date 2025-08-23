using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Misc
{
    [Export(typeof(ImageFormat))]
    public class PtiFormat : ImageFormat
    {
        public override string         Tag { get { return "PTI"; } }
        public override string Description { get { return "Custom BMP image"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = ReadHeader (stream);
            if (null == header)
                return null;
            using (var bmp = new BinMemoryStream (header, stream.Name))
                return Bmp.ReadMetaData (bmp);
        }

        byte[] ReadHeader (IBinaryStream stream)
        {
            var header = new byte[0x36];
            if (0x10 != stream.Read (header, 0, 0x10)
                || 'B' != header[0] || 'M' != header[1]
                || 0 != LittleEndian.ToUInt16 (header, 0xE)
                || 0x28 != stream.Read (header, 0xE, 0x28)
                || 0x28 != header[0xE])
                return null;
            return header;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            uint length = (uint)(stream.Length - 0x38);
            var image = new byte[length+0x38];
            stream.Read (image, 0, 0x10);
            stream.Read (image, 0xE, (int)length+0x28);
            if (24 == info.BPP && length+2 == info.Width * info.Height * 3)
            {
                image[image.Length-2] = 0xFF;
                image[image.Length-1] = 0xFF;
                length += 2;
            }
            LittleEndian.Pack (length+0x36, image, 2);
            using (var bmp = new BinMemoryStream (image, stream.Name))
                return Bmp.Read (bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PtiFormat.Write not implemented");
        }
    }
}
