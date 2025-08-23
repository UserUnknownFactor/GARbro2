using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Xuse
{
    [Export(typeof(ImageFormat))]
    public class P4AGFormat : ImageFormat
    {
        public override string         Tag { get { return "P/4AG"; } }
        public override string Description { get { return "Xuse/Eternal obfuscated PNG image"; } }
        public override uint     Signature { get { return 0x0A0D474E; } } // 'NG\n\r'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            using (var input = OpenAsPng (file))
                return Png.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = OpenAsPng (file))
                return Png.Read (input, info);
        }

        static readonly byte[] HeaderBytes = new byte[2] { PngFormat.PNG_HEADER[0], PngFormat.PNG_HEADER[1] };

        internal IBinaryStream OpenAsPng (IBinaryStream file)
        {
            var input = new PrefixStream (HeaderBytes, file.AsStream, true);
            return new BinaryStream (input, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("P4AGFormat.Write not implemented");
        }
    }
}
