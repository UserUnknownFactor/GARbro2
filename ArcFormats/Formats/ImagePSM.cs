using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Misc
{
    [Export(typeof(ImageFormat))]
    public class PsmFormat : PngFormat
    {
        public override string         Tag { get { return "PSM"; } }
        public override string Description { get { return "Obfuscated PNG image"; } }
        public override uint     Signature { get { return 0x474E50ED; } }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var png = DeobfuscateStream (file))
                return base.Read (png, info);
        }

        IBinaryStream DeobfuscateStream (IBinaryStream file)
        {
            var header = file.ReadHeader (4).ToArray();
            header[0] = 0x89;
            var body = new StreamRegion (file.AsStream, 4, true);
            var png = new PrefixStream (header, body);
            return new BinaryStream (png, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            var start_pos = file.Position;
            base.Write (file, image);
            var end_pos = file.Position;
            file.Position = start_pos;
            file.WriteByte (0xED);
            file.Position = end_pos;
        }
    }
}
