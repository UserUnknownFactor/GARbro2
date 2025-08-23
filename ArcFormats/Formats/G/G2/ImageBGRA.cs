using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.G2
{
    [Export(typeof(ImageFormat))]
    public class BgraFormat : ImageFormat
    {
        public override string         Tag { get { return "BGRA"; } }
        public override string Description { get { return "G2 engine image format"; } }
        public override uint     Signature { get { return 0x41524742; } } // 'BGRA'

        public BgraFormat ()
        {
            Extensions = new string[] { "argb", "arg" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 4;
            uint bitmap = stream.ReadUInt32();
            if (0x08080808 != bitmap)
                return null;
            return new ImageMetaData
            {
                Width = stream.ReadUInt32(),
                Height = stream.ReadUInt32(),
                BPP = 32,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 0x10;
            var pixels = new byte[info.Width*info.Height*4];
            if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                throw new EndOfStreamException();
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BgraFormat.Write not implemented");
        }
    }
}
