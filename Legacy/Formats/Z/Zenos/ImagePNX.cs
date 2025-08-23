using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Zenos
{
    [Export(typeof(ImageFormat))]
    public class PnxFormat : PngFormat
    {
        public override string         Tag { get { return "PNX/ZENOS"; } }
        public override string Description { get { return "Obfuscated PNG image"; } }
        public override uint     Signature { get { return 0x584E5089; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            using (var input = DeobfuscateStream (file))
                return base.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = DeobfuscateStream (file))
                return base.Read (input, info);
        }

        IBinaryStream DeobfuscateStream (IBinaryStream file)
        {
            var body = new StreamRegion (file.AsStream, 8, file.Length-8, true);
            var png = new PrefixStream (PNG_HEADER, body);
            return new BinaryStream (png, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PnxFormat.Write not implemented");
        }
    }
}
