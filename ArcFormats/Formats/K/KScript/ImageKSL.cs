using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.KScript
{
    internal class KslMetaData : ImageMetaData
    {
        public byte Key;
        public int  DataLength;
    }

    [Export(typeof(ImageFormat))]
    public class KslFormat : ImageFormat
    {
        public override string         Tag { get { return "KSL"; } }
        public override string Description { get { return "KScript grayscale image format"; } }
        public override uint     Signature { get { return 0x4D4C534B; } } // 'KSLM'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x14);
            return new KslMetaData
            {
                Width   = header.ToUInt32 (0xC),
                Height  = header.ToUInt32 (0x10),
                BPP     = 8,
                Key     = (byte)(header[4] ^ header[5]),
                DataLength = header.ToInt32 (8),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (KslMetaData)info;
            stream.Position = 0x14;
            var pixels = stream.ReadBytes (meta.DataLength);
            for (int i = 0; i < pixels.Length; ++i)
                pixels[i] ^= meta.Key;
            return ImageData.Create (info, PixelFormats.Gray8, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("KslFormat.Write not implemented");
        }
    }
}
