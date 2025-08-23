using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.GameSystem
{
    [Export(typeof(ImageFormat))]
    public class AlpFormat : ImageFormat
    {
        public override string         Tag { get { return "ALP/GAMESYSTEM"; } }
        public override string Description { get { return "'Game System' grayscale image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".alp"))
                return null;
            var header = file.ReadHeader (8);
            uint width = header.ToUInt32 (0);
            uint height = header.ToUInt32 (4);
            if (0 == width || 0 == height)
                return null;
            if (file.Length != 8 + width * height)
                return null;
            return new ImageMetaData { Width = width, Height = height, BPP = 8 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 8;
            var pixels = file.ReadBytes ((int)info.Width * (int)info.Height);
            return ImageData.CreateFlipped (info, PixelFormats.Gray8, null, pixels, (int)info.Width);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AlpFormat.Write not implemented");
        }
    }
}
