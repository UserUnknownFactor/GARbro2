using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// [950616][JAST] Tenshi-tachi no Gogo ~Tenkousei~
// [950922][Tiare] Vanishing Point -Tenshi no Kieta Machi-

namespace GameRes.Formats.Tiare
{
    internal class GraMetaData : ImageMetaData
    {
        public byte Flags;
        public long DataOffset;

        public bool HasPalette => (Flags & 0x80) == 0;
    }

    [Export(typeof(ImageFormat))]
    public class GraFormat : ImageFormat
    {
        public override string         Tag => "GRA/TIARE";
        public override string Description => "Tiare image format";
        public override uint     Signature => 0;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x30);
            int pos = header.IndexOf (0x1A);
            if (-1 == pos)
                return null;
            ++pos;
            while (pos < header.Length && header[pos++] != 0)
                ;
            if (pos + 3 >= header.Length || header[pos+3] != 4)
                return null;
            byte flags = header[pos];
            file.Position = pos + 8;
            int skip = Binary.BigEndian (file.ReadUInt16());
            if (skip != 0)
                file.Seek (skip, SeekOrigin.Current);
            uint width  = Binary.BigEndian (file.ReadUInt16());
            uint height = Binary.BigEndian (file.ReadUInt16());
            if (width == 0 || height == 0)
                return null;
            return new GraMetaData
            {
                Width  = width,
                Height = height,
                BPP    = 4,
                Flags  = flags,
                DataOffset = file.Position,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var gra = (GraMetaData)info;
            file.Position = gra.DataOffset;
            BitmapPalette palette;
            if (gra.HasPalette)
                palette = ReadPalette (file.AsStream, 16, PaletteFormat.Rgb);
            else
                palette = DefaultPalette;
            var reader = new System98.GraBaseReader (file, info);
            reader.UnpackBits();
            return ImageData.Create (gra, PixelFormats.Indexed4, palette, reader.Pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GraFormat.Write not implemented");
        }

        static readonly BitmapPalette DefaultPalette = new BitmapPalette (new Color[] {
            #region Default palette
            Color.FromRgb (0x00, 0x00, 0x00),
            Color.FromRgb (0x00, 0x00, 0x77),
            Color.FromRgb (0x77, 0x00, 0x00),
            Color.FromRgb (0x77, 0x00, 0x77),
            Color.FromRgb (0x00, 0x77, 0x00),
            Color.FromRgb (0x00, 0x77, 0x77),
            Color.FromRgb (0x77, 0x77, 0x00),
            Color.FromRgb (0x77, 0x77, 0x77),
            Color.FromRgb (0x00, 0x00, 0x00),
            Color.FromRgb (0x00, 0x00, 0xFF),
            Color.FromRgb (0xFF, 0x00, 0x00),
            Color.FromRgb (0xFF, 0x00, 0xFF),
            Color.FromRgb (0x00, 0xFF, 0x00),
            Color.FromRgb (0x00, 0xFF, 0xFF),
            Color.FromRgb (0xFF, 0xFF, 0x00),
            Color.FromRgb (0xFF, 0xFF, 0xFF),
            #endregion
        });
    }
}
