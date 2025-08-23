using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Emic
{
    [Export(typeof(ImageFormat))]
    public class MwpFormat : ImageFormat
    {
        public override string         Tag { get { return "MWP"; } }
        public override string Description { get { return "Emic engine bitmap"; } }
        public override uint     Signature { get { return 0x1050574D; } } // 'MWP\x10'

        public MwpFormat ()
        {
            Extensions = new string[] { "mwp", "bmp" };
            Signatures = new uint[] { 0x1050574D, 0x4C594554 }; // 'TEYL'
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 4;
            return new ImageMetaData
            {
                Width  = file.ReadUInt32(),
                Height = file.ReadUInt32(),
                BPP = 32,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var pixels = new byte[info.Width*info.Height*4];
            file.Position = 12;
            if (pixels.Length != file.Read (pixels, 0, pixels.Length))
                throw new EndOfStreamException();
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MwpFormat.Write not implemented");
        }
    }
}
