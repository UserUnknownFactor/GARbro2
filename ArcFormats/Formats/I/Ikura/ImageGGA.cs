using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Ikura
{
    [Export(typeof(ImageFormat))]
    public class GgaFormat : ImageFormat
    {
        public override string         Tag { get { return "GGA"; } }
        public override string Description { get { return "D.O. image format"; } }
        public override uint     Signature { get { return 0; } }

        class GgaMetaData : ImageMetaData
        {
            public int  UnpackedSize;
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".gga"))
                return null;
            int x = file.ReadInt16();
            int y = file.ReadInt16();
            uint w = file.ReadUInt16();
            uint h = file.ReadUInt16();
            int unpacked_size = file.ReadInt32();
            if (0 == w || 0 == h || w > 0x7FFF || h > 0x7FFF || x < 0 || y < 0
                || 3 * w * h != unpacked_size)
                return null;
            return new GgaMetaData {
                Width = w, Height = h, OffsetX = x, OffsetY = y, BPP = 24,
                UnpackedSize = unpacked_size,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (GgaMetaData)info;
            file.Position = 12;
            var pixels = new byte[meta.UnpackedSize];
            using (var input = new LzssStream (file.AsStream, LzssMode.Decompress, true))
            {
                if (pixels.Length != input.Read (pixels, 0, pixels.Length))
                    throw new InvalidFormatException();
            }
            return ImageData.Create (info, PixelFormats.Bgr24, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GgaFormat.Write not implemented");
        }
    }
}
