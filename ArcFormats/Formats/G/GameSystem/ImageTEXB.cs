using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.GameSystem
{
    [Export(typeof(ImageFormat))]
    public class TexbFormat : ImageFormat
    {
        public override string         Tag { get { return "TEXB"; } }
        public override string Description { get { return "'Game System' texture image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".texb"))
                return null;
            var header = file.ReadHeader (8);
            uint width = header.ToUInt32 (0);
            uint height = header.ToUInt32 (4);
            if (0 == width || 0 == height)
                return null;
            if (file.Length != 8 + width * height * 4)
                return null;
            return new ImageMetaData { Width = width, Height = height, BPP = 32 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 8;
            int stride = (int)info.Width * 4;
            var pixels = file.ReadBytes (stride * (int)info.Height);
            return ImageData.CreateFlipped (info, PixelFormats.Bgra32, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("TexbFormat.Write not implemented");
        }
    }
}
