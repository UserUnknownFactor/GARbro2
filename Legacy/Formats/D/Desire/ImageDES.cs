using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [940720][Desire] H+

namespace GameRes.Formats.Desire
{
    [Export(typeof(ImageFormat))]
    public class DesFormat : ImageFormat
    {
        public override string         Tag => "DES98";
        public override string Description => "Des98 engine image format";
        public override uint     Signature => 0;

        public DesFormat ()
        {
            Extensions = new[] { "" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            ushort width  = Binary.BigEndian (file.ReadUInt16());
            ushort height = Binary.BigEndian (file.ReadUInt16());
            if (0 == width || 0 == height || (width & 7) != 0 || width > 640 || height > 400)
                return null;
            return new ImageMetaData {
                Width = width,
                Height = height,
                BPP = 4,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 4;
            var palette = ReadPalette (file.AsStream, 16, PaletteFormat.Rgb);
            var reader = new System98.GraBaseReader (file, info);
            reader.UnpackBits();
            return ImageData.Create (info, PixelFormats.Indexed4, palette, reader.Pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DesFormat.Write not implemented");
        }
    }
}
