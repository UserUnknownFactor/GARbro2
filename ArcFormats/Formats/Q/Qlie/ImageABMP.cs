using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Qlie
{
    internal class Abmp6MetaData : ImageMetaData
    {
        public uint         ImageDataOffset;
        public uint         ImageDataSize;
        public ImageFormat  Format;
        public ImageMetaData Info;
    }

    [Export(typeof(ImageFormat))]
    public class AbmpFormat : ImageFormat
    {
        public override string         Tag { get { return "B/ABMP6"; } }
        public override string Description { get { return "QLIE engine image format"; } }
        public override uint     Signature { get { return 0x504D4241; } } // 'ABMP6'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            if (!header.AsciiEqual ("ABMP6\0"))
                return null;
            uint data_offset = header.ToUInt32 (0xC) + 0x10;
            file.Position = data_offset;
            uint data_size = file.ReadUInt32();
            data_offset += 4;
            uint signature = file.ReadUInt32();
            ImageFormat format;
            if (signature == Png.Signature)
                format = Png;
            else if (signature == 0xE0FFD8FFu)
                format = Jpeg;
            else if ((signature & 0xFFFF) == 0x4D42)
            {
                format = Bmp;
                file.Position = data_offset+2;
                data_size = file.ReadUInt32();
            }
            else
                return null;
            using (var input = OpenImageStream (file, data_offset, data_size))
            {
                var info = format.ReadMetaData (input);
                if (null == info)
                    return null;
                return new Abmp6MetaData {
                    Width = info.Width, Height = info.Height,
                    OffsetX = info.OffsetX, OffsetY = info.OffsetY,
                    BPP = info.BPP,
                    ImageDataOffset = data_offset, ImageDataSize = data_size,
                    Format = format, Info = info,
                };
            }
        }

        internal IBinaryStream OpenImageStream (IBinaryStream file, uint offset, uint size)
        {
            var input = new StreamRegion (file.AsStream, offset, size, true);
            return new BinaryStream (input, file.Name);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (Abmp6MetaData)info;
            using (var input = OpenImageStream (file, meta.ImageDataOffset, meta.ImageDataSize))
                return meta.Format.Read (input, meta.Info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Abmp6Format.Write not implemented");
        }
    }
}
