using System.ComponentModel.Composition;
using System.IO;
using System.Linq;

namespace GameRes.Formats.Tigerman
{
    internal class ChrMetaData : ImageMetaData
    {
        public uint             Offset;
        public uint             Length;
        public ImageMetaData    ZitInfo;
    }

    [Export(typeof(ImageFormat))]
    public class ChrFormat : ImageFormat
    {
        public override string         Tag { get { return "CHR/TIGERMAN"; } }
        public override string Description { get { return "Tigerman Project compound image"; } }
        public override uint     Signature { get { return 0; } }

        public ChrFormat ()
        {
            Extensions = new string[] { "chr", "cls", "ev" };
            Signatures = new uint[] { 0x01B1, 0 };
        }

        static readonly ResourceInstance<ImageFormat> s_zit_format = new ResourceInstance<ImageFormat> ("ZIT");

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            uint base_offset = file.ReadUInt32();
            if (base_offset >= file.Length)
                return null;
            uint base_length = file.ReadUInt32();
            if (base_offset + base_length > file.Length)
                return null;
            file.Position = base_offset;
            uint signature = file.ReadUInt32();
            if (!s_zit_format.Value.Signatures.Contains (signature))
                return null;
            using (var zit = OpenZitStream (file, base_offset, base_length))
            {
                var info = s_zit_format.Value.ReadMetaData (zit);
                if (null == info)
                    return null;
                return new ChrMetaData {
                    Width = info.Width,
                    Height = info.Height,
                    BPP = info.BPP,
                    Offset = base_offset,
                    Length = base_length,
                    ZitInfo = info,
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (ChrMetaData)info;
            using (var zit = OpenZitStream (file, meta.Offset, meta.Length))
                return s_zit_format.Value.Read (zit, meta.ZitInfo);
        }

        IBinaryStream OpenZitStream (IBinaryStream file, uint offset, uint size)
        {
            var input = new StreamRegion (file.AsStream, offset, size, true);
            return new BinaryStream (input, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ChrFormat.Write not implemented");
        }
    }
}
