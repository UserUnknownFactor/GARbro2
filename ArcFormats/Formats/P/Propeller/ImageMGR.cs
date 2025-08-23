using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Propeller
{
    internal class MgrMetaData : ImageMetaData
    {
        public int  Offset;
        public int  PackedSize;
        public int  UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class MgrFormat : ImageFormat
    {
        public override string         Tag { get { return "MGR"; } }
        public override string Description { get { return "Propeller image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            int count = stream.ReadInt16();
            if (count <= 0 || count >= 0x100)
                return null;
            int offset;
            if (count > 1)
            {
                offset = stream.ReadInt32();
                if (offset != 2 + count * 4)
                    return null;
            }
            else
                offset = 2;
            stream.Position = offset;
            int unpacked_size = stream.ReadInt32();
            int packed_size = stream.ReadInt32();
            offset += 8;
            if (offset + packed_size > stream.Length)
                return null;
            byte[] header = new byte[0x36];
            if (0x36 != MgrOpener.Decompress (stream.AsStream, header)
                || header[0] != 'B' || header[1] != 'M')
                return null;
            using (var bmp = new BinMemoryStream (header, stream.Name))
            {
                var info = Bmp.ReadMetaData (bmp);
                if (null == info)
                    return null;
                return new MgrMetaData
                {
                    Width = info.Width,
                    Height = info.Height,
                    BPP = info.BPP,
                    Offset = offset,
                    PackedSize = packed_size,
                    UnpackedSize = unpacked_size,
                };
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (MgrMetaData)info;
            stream.Position = meta.Offset;
            var data = new byte[meta.UnpackedSize];
            if (data.Length != MgrOpener.Decompress (stream.AsStream, data))
                throw new InvalidFormatException();
            if (meta.BPP != 32)
            {
                using (var bmp = new BinMemoryStream (data, stream.Name))
                    return Bmp.Read (bmp, info);
            }
            // special case for 32bpp bitmaps with alpha-channel
            int stride = (int)meta.Width * 4;
            var pixels = new byte[stride * (int)meta.Height];
            int src = LittleEndian.ToInt32 (data, 0xA);
            for (int dst = stride*((int)meta.Height-1); dst >= 0; dst -= stride)
            {
                Buffer.BlockCopy (data, src, pixels, dst, stride);
                src += stride;
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MgrFormat.Write not implemented");
        }
    }
}
