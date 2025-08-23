using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Misc
{
    [Export(typeof(ImageFormat))]
    public class PnxFormat : PngFormat
    {
        public override string         Tag { get { return "PNX"; } }
        public override string Description { get { return "Encrypted PNG image"; } }
        public override uint     Signature { get { return 0x2F2638E1; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            using (var input = DeobfuscateStream (file, GuessEncryptionKey (file)))
                return base.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = DeobfuscateStream (file, GuessEncryptionKey (file)))
                return base.Read (input, info);
        }

        byte GuessEncryptionKey (IBinaryStream file)
        {
            return (byte)(file.Signature ^ 0x89);
        }

        IBinaryStream DeobfuscateStream (IBinaryStream file, byte key)
        {
            var png = new XoredStream (file.AsStream, key, true);
            return new BinaryStream (png, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PnxFormat.Write not implemented");
        }
    }
}
