using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Logg
{
    [Export(typeof(ImageFormat))]
    public class FrmFormat : ImageFormat
    {
        public override string         Tag { get => "FRM"; }
        public override string Description { get => "Logg image format"; }
        public override uint     Signature { get => 0x4D5246; } // 'FRM'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            return new ImageMetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP = 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x0C;
            int stride = file.ReadInt32();
            var palette = ReadPalette (file.AsStream);
            var pixels = file.ReadBytes (stride * info.iHeight);
            return ImageData.Create (info, PixelFormats.Indexed8, palette, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("FrmFormat.Write not implemented");
        }
    }
}
