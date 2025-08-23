using GameRes.Compression;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Nags
{
    internal class NgpMetaData : ImageMetaData
    {
        public int  PackedSize;
        public int  UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class NgpFormat : ImageFormat
    {
        public override string         Tag { get { return "NGP"; } }
        public override string Description { get { return "NAGS engine image format"; } }
        public override uint     Signature { get { return 0x2050474E; } } // 'NGP '

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 0x12;
            int packed_size = file.ReadInt32();
            uint width      = file.ReadUInt32();
            uint height     = file.ReadUInt32();
            int bpp         = file.ReadUInt16() * 8;
            file.Position = 0x100;
            int unpacked_size = file.ReadInt32();
            if (packed_size <= 0 || unpacked_size <= 0)
                return null;
            return new NgpMetaData
            {
                Width = width,
                Height = height,
                BPP = bpp,
                PackedSize = packed_size,
                UnpackedSize = unpacked_size,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (NgpMetaData)info;
            using (var input = new StreamRegion (stream.AsStream, 0x104, meta.PackedSize, true))
            using (var z = new ZLibStream (input, CompressionMode.Decompress))
            {
                var pixels = new byte[meta.UnpackedSize];
                if (pixels.Length != z.Read (pixels, 0, pixels.Length))
                    throw new EndOfStreamException();
                PixelFormat format;
                if (32 == meta.BPP)
                    format = PixelFormats.Bgra32;
                else if (24 == meta.BPP)
                    format = PixelFormats.Bgr24;
                else if (8 == meta.BPP)
                    format = PixelFormats.Gray8;
                else
                    throw new System.NotSupportedException ("Not supported NGP image color depth");

                return ImageData.Create (info, format, null, pixels);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("NgpFormat.Write not implemented");
        }
    }
}
