using GameRes.Compression;
using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Elf
{
    [Export(typeof(ImageFormat))]
    public class Gp8Format : ImageFormat
    {
        public override string         Tag { get { return "GP8"; } }
        public override string Description { get { return "Ai5 engine indexed image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length <= 0x408)
                return null;
            int x = file.ReadInt16();
            int y = file.ReadInt16();
            if (x < 0 || y < 0 || x > 0x300 || y > 0x300)
                return null;
            int w = file.ReadInt16();
            int h = file.ReadInt16();
            if (w <= 0 || w > 0x1000 || h <= 0 || h > 0x1000)
                return null;
            return new ImageMetaData {
                Width = (uint)w,
                Height = (uint)h,
                BPP = 8,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 8;
            var palette = ReadPalette (stream.AsStream);
            var pixels = new byte[info.Width * info.Height];
            using (var reader = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
            {
                if (pixels.Length != reader.Read (pixels, 0, pixels.Length))
                    throw new InvalidFormatException();
                return ImageData.CreateFlipped (info, PixelFormats.Indexed8, palette, pixels, (int)info.Width);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Gp8Format.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class MskFormat : ImageFormat
    {
        public override string         Tag { get { return "MSK/AI5"; } }
        public override string Description { get { return "Ai5 engine image mask"; } }
        public override uint     Signature { get { return 0; } }

        public MskFormat ()
        {
            Extensions = new string[] { "msk" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream input)
        {
            int x = input.ReadInt16();
            int y = input.ReadInt16();
            int w = input.ReadInt16();
            int h = input.ReadInt16();
            if (w <= 0 || w > 0x1000 || h <= 0 || h > 0x1000
                || x < 0 || x > 0x800 || y < 0 || y > 0x800)
                return null;
            return new ImageMetaData {
                Width = (uint)w,
                Height = (uint)h,
                OffsetX = x,
                OffsetY = y,
                BPP = 8,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 8;
            var pixels = new byte[info.Width * info.Height];
            using (var reader = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
            {
                if (pixels.Length != reader.Read (pixels, 0, pixels.Length))
                    throw new InvalidFormatException();
                return ImageData.CreateFlipped (info, PixelFormats.Gray8, null, pixels, (int)info.Width);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MskFormat.Write not implemented");
        }
    }
}
