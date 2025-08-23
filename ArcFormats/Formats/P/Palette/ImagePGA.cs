using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Palette
{
    [Export(typeof(ImageFormat))]
    public class PgaFormat : PngFormat
    {
        public override string         Tag { get { return "PGA"; } }
        public override string Description { get { return "Palette obfuscated PNG image"; } }
        public override uint     Signature { get { return 0x50414750; } } // 'PGAP'
        public override bool      CanWrite { get { return true; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            using (var png = DeobfuscateStream (stream))
                return base.ReadMetaData (png);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var png = DeobfuscateStream (stream))
                return base.Read (png, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var png = new MemoryStream())
            {
                base.Write (png, image);
                var buffer = png.GetBuffer();
                for (int i = 0; i < 8; ++i)
                    buffer[i+8] ^= (byte)"PGAECODE"[i];
                buffer[5] = (byte)'P';
                buffer[6] = (byte)'G';
                buffer[7] = (byte)'A';
                file.Write (buffer, 5, (int)png.Length - 5);
            }
        }

        public static byte[] PngHeader { get { return PNG_HEADER; } }
        public static byte[] PngFooter  { get { return PNG_FOOTER; } }

        IBinaryStream DeobfuscateStream (IBinaryStream stream)
        {
            var png_header = new byte[0x10];
            stream.Read (png_header, 5, 11);
            System.Buffer.BlockCopy (PngHeader, 0, png_header, 0, 8);
            for (int i = 0; i < 8; ++i)
                png_header[i+8] ^= (byte)"PGAECODE"[i];
            var png_body = new StreamRegion (stream.AsStream, 11, true);
            var pre = new PrefixStream (png_header, png_body);
            return new BinaryStream (pre, stream.Name);
        }
    }
}
