using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.ApplePie
{
    internal class GtMetaData : ImageMetaData
    {
        public int  Flags;
        public uint DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class GtFormat : ImageFormat
    {
        public override string         Tag { get { return "GT/ApplePie"; } }
        public override string Description { get { return "Apple Pie image format"; } }
        public override uint     Signature { get { return 0; } }

        public GtFormat ()
        {
            Signatures = new uint[] { 0x0B105447, 0x06105447, 0x01105447, 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            if (!header.AsciiEqual ("GT\x10"))
                return null;
            bool has_alpha = (header[3] & 8) != 0;
            bool grayscale = (header[3] & 3) == 1;
            return new GtMetaData {
                Width = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP = grayscale ? 8 : has_alpha ? 32 : 24,
                Flags = header[3],
                DataOffset = header.ToUInt32 (8),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (GtMetaData)info;
            file.Position = meta.DataOffset;
            if (0 != (meta.Flags & 8))
            {
                var pixels = UnpackRle (file, (int)meta.Width * (int)meta.Height);
                return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
            }
            else if (1 == (meta.Flags & 3))
            {
                var pixels = file.ReadBytes ((int)meta.Width * (int)meta.Height);
                return ImageData.Create (info, PixelFormats.Gray8, null, pixels);
            }
            else
            {
                int stride = (int)meta.Width * 3;
                var pixels = file.ReadBytes (stride * (int)meta.Height);
                return ImageData.Create (info, PixelFormats.Bgr24, null, pixels, stride);
            }
        }

        byte[] UnpackRle (IBinaryStream input, int count)
        {
            var output = new byte[count * 4];
            int dst = 0;
            while (count > 0)
            {
                int length = input.ReadInt32();
                count -= length;
                dst += length * 4;
                length = input.ReadInt32();
                count -= length;
                input.Read (output, dst, length * 4);
                dst += length * 4;
            }
            return output;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GtFormat.Write not implemented");
        }
    }
}
