using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Saiki
{
    public abstract class ObfuscatedImageFormat : ImageFormat
    {
        const int DefaultEncryptedLength = 200;

        internal static IBinaryStream OpenEncrypted (IBinaryStream input, int encrypted_length = DefaultEncryptedLength)
        {
            input.Position = 0;
            var header = input.ReadBytes (encrypted_length+2);
            header[0] ^= 0xFF;
            header[1] = Binary.RotByteL ((byte)~header[1] , 1);
            int shift = 1;
            byte count = header[0];
            for (int i = 2; i < header.Length; ++i)
            {
                header[i] = Binary.RotByteL (header[i], shift);
                if (++shift >= 7)
                    shift = 1;
                if (--count == 0)
                {
                    if (shift <= 4)
                        count = header[1];
                    else
                        count = header[0];
                    shift = 1;
                }
            }
            Stream stream = new StreamRegion (input.AsStream, header.Length, true);
            stream = new PrefixStream (header, stream);
            return new BinaryStream (stream, input.Name);
        }
    }

    [Export(typeof(ImageFormat))]
    public class JpxFormat : ObfuscatedImageFormat
    {
        public override string         Tag { get { return "JPX"; } }
        public override string Description { get { return "Obfuscated JPEG image"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public JpxFormat ()
        {
            Signatures = new uint[] { 0x38FF9300, 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (2);
            if (header[0] != 0 || header[1] != 0x93)
                return null;
            using (var jpg = OpenEncrypted (stream))
                return Jpeg.ReadMetaData (jpg);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var jpg = OpenEncrypted (stream))
                return Jpeg.Read (jpg, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("JpxFormat.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class BmxFormat : ObfuscatedImageFormat
    {
        public override string         Tag { get { return "BMX"; } }
        public override string Description { get { return "Obfuscated BMP image"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (2);
            if (header[0] != 0xBD || header[1] != 0x59)
                return null;
            using (var bmp = OpenEncrypted (stream))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var bmp = OpenEncrypted (stream))
                return Bmp.Read (bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BmxFormat.Write not implemented");
        }
    }
}
