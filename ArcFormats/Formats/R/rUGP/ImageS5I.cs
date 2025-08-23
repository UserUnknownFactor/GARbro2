using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Rugp
{
    internal class S5iMetaData : RioMetaData
    {
        public int  Schema;
    }

    [Export(typeof(ImageFormat))]
    public class S5iFormat : ImageFormat
    {
        public override string         Tag { get { return "S5I"; } }
        public override string Description { get { return "rUGP engine image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Signature != CRioArchive.ObjectSignature)
                return null;
            var rio = new CRioArchive (file);
            uint signature;
            var class_ref = rio.LoadRioTypeCore (out signature);
            uint object_pos = (uint)file.Position;
            if ("CS5i" != class_ref)
                return null;
            file.Seek (8, SeekOrigin.Current);
            return new S5iMetaData
            {
                Width   = file.ReadUInt16(),
                Height  = file.ReadUInt16(),
                BPP     = 32,
                ObjectOffset = object_pos,
                Schema  = rio.Schema,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (S5iMetaData)info;
            file.Position = meta.ObjectOffset + 0x14;
            int size;
            if (meta.Schema != 0)
                size = file.ReadInt32();
            else
                size = (int)meta.Width * (int)meta.Height * 4;
            var pixels = file.ReadBytes (size);
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("S5iFormat.Write not implemented");
        }
    }
}
