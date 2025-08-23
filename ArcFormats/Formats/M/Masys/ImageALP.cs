using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Megu
{
    internal class AlpMetaData : ImageMetaData
    {
        public uint UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class AlpFormat : ImageFormat
    {
        public override string         Tag { get { return "ALP/MEGU"; } }
        public override string Description { get { return "Masys alpha channel bitmap"; } }
        public override uint     Signature { get { return 0x64504C41; } } // 'ALPd'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x15);
            if (header[4] != 0)
                return null;
            return new AlpMetaData {
                Width   = header.ToUInt32 (5),
                Height  = header.ToUInt32 (9),
                BPP     = 8,
                UnpackedSize = header.ToUInt32 (0x11),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (AlpMetaData)info;
            file.Position = 0x15;
            var pixels = Unpack (file, meta.UnpackedSize);
            return ImageData.Create (info, PixelFormats.Gray8, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AlpFormat.Write not implemented");
        }

        byte[] Unpack (IBinaryStream input, uint unpacked_size)
        {
            var output = new byte[unpacked_size];
            int dst = 0;
            while (dst < output.Length)
            {
                int ctl = input.ReadByte();
                if (-1 == ctl)
                    break;
                byte val = (byte)((ctl & 0x7F) * 0xFF / 0x40);
                if (0 != (ctl & 0x80))
                {
                    int count = input.ReadUInt16();
                    for (int i = 0; i < count; ++i)
                    {
                        output[dst++] = val;
                    }
                }
                else
                {
                    output[dst++] = val;
                }
            }
            return output;
        }
    }
}
