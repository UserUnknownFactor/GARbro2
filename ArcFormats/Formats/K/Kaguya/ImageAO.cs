using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Kaguya
{
    [Export(typeof(ImageFormat))]
    public class AoFormat : ApFormat
    {
        public override string         Tag { get { return "AO/KAGUYA"; } }
        public override string Description { get { return "KaGuYa script engine image format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return true; } }

        public AoFormat ()
        {
            Extensions = new string[] { "sp_" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            int A = stream.ReadByte();
            int O = stream.ReadByte();
            if ('A' != A || 'O' != O)
                return null;
            var info = new ImageMetaData();
            info.Width = stream.ReadUInt32();
            info.Height = stream.ReadUInt32();
            info.BPP = stream.ReadInt16();
            info.OffsetX = stream.ReadInt32();
            info.OffsetY = stream.ReadInt32();
            if (info.Width > 0x8000 || info.Height > 0x8000 || !(32 == info.BPP || 24 == info.BPP))
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 0x14;
            return ReadBitmapData (stream, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var output = new BinaryWriter (file, Encoding.ASCII, true))
            {
                output.Write ((byte)'A');
                output.Write ((byte)'O');
                output.Write (image.Width);
                output.Write (image.Height);
                output.Write ((short)24);
                output.Write (image.OffsetX);
                output.Write (image.OffsetY);
                WriteBitmapData (file, image);
            }
        }
    }
}
