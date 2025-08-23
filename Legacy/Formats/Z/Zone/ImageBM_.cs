using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Zone
{
    internal class Bm_MetaData : ImageMetaData
    {
        public bool IsCompressed;
        public byte RleFlag;
    }

    [Export(typeof(ImageFormat))]
    public class Bm_Format : ImageFormat
    {
        public override string         Tag { get { return "BM_/ZONE"; } }
        public override string Description { get { return "Zone compressed image"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".bm_"))
                return null;
            var header = file.ReadHeader (0x18);
            int signature = header.ToInt32 (0);
            if (signature != 0 && signature != 1)
                return null;
            file.Position = 0x418;
            return new Bm_MetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP    = 8,
                IsCompressed = signature != 0,
                RleFlag = file.ReadUInt8(),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (Bm_MetaData)info;
            file.Position = 0x18;
            var palette = ReadPalette (file.AsStream);
            var pixels = new byte[info.Width * info.Height];
            file.Position = 0x424;
            if (meta.IsCompressed)
                ReadRle (file, pixels, meta.RleFlag);
            else
                file.Read (pixels, 0, pixels.Length);
            if (meta.IsCompressed)
                return ImageData.Create (info, PixelFormats.Indexed8, palette, pixels);
            else
                return ImageData.CreateFlipped (info, PixelFormats.Indexed8, palette, pixels, info.iWidth);
        }

        void ReadRle (IBinaryStream input, byte[] output, byte rle_flag)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                byte b = input.ReadUInt8();
                if (b != rle_flag)
                {
                    output[dst++] = b;
                    continue;
                }
                b = input.ReadUInt8();
                if (0 == b)
                {
                    output[dst++] = rle_flag;
                    continue;
                }
                int count = 0;
                while (1 == b)
                {
                    count += 0x100;
                    b = input.ReadUInt8();
                }
                count += b;
                if (b != 0)
                    input.ReadByte();
                b = input.ReadUInt8();
                while (count --> 0)
                    output[dst++] = b;
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Bm_Format.Write not implemented");
        }
    }
}
