using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Tama
{
    internal class BtnMetaData : ImageMetaData
    {
        public int SurOffset;
    }

    [Export(typeof(ImageFormat))]
    public class BtnFormat : SurFormat
    {
        public override string         Tag { get { return "BTN/SUR"; } }
        public override string Description { get { return "TamaSoft ADV system button image"; } }
        public override uint     Signature { get { return 0x4E544245; } } // 'EBTN'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 4;
            int count = stream.ReadInt32();
            int offset = 0x30 + count * 4;
            using (var data = new StreamRegion (stream.AsStream, offset, true))
            using (var input = new BinaryStream (data, stream.Name))
            {
                var info = base.ReadMetaData (input);
                if (null == info)
                    return null;
                return new BtnMetaData
                {
                    Width = info.Width,
                    Height = info.Height,
                    BPP = info.BPP,
                    SurOffset = offset,
                };
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (BtnMetaData)info;
            using (var data = new StreamRegion (stream.AsStream, meta.SurOffset, true))
            using (var input = new BinaryStream (data, stream.Name))
                return base.Read (input, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BtnFormat.Write not implemented");
        }
    }
}
