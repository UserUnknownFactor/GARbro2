using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Sohfu
{
    internal class DtlMetaData : ImageMetaData
    {
        public int Stride;
    }

    [Export(typeof(ImageFormat))]
    public class DtlFormat : ImageFormat
    {
        public override string         Tag { get { return "DTL/SOHFU"; } }
        public override string Description { get { return "Sohfu image format"; } }
        public override uint     Signature { get { return 0x5F4C5444; } } // 'DTL_'

        public DtlFormat ()
        {
            Extensions = new[] { "ls8b", "ls8" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            int bpp = header.ToInt32 (16);
            if (bpp != 24 && bpp != 32 && bpp != 8 && bpp != 4)
                return null;
            return new DtlMetaData
            {
                Width  = header.ToUInt32 (8),
                Height = header.ToUInt32 (12),
                BPP    = bpp,
                Stride = header.ToInt32 (20),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (DtlMetaData)info;
            file.Position = 0x18;
            var pixels = file.ReadBytes (meta.Stride * meta.iHeight);
            PixelFormat format;
            if (24 == meta.BPP)
                format = PixelFormats.Bgr24;
            else if (32 == meta.BPP)
                format = PixelFormats.Bgra32;
            else if (4 == meta.BPP)
                format = PixelFormats.Gray4;
            else
                format = PixelFormats.Gray8;
            return ImageData.Create (meta, format, null, pixels, meta.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DtlFormat.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class DtlcFormat : ImageFormat
    {
        public override string         Tag { get { return "DTLC/SOHFU"; } }
        public override string Description { get { return "Sohfu image format"; } }
        public override uint     Signature { get { return 0x434C5444; } } // 'DTLC'

        public DtlcFormat ()
        {
            Extensions = new[] { "ls8b", "ls8" };
            Signatures = new[] { 0x434C5444u, 0x414C5444u }; // 'DTLA'
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            int bpp = header.ToInt32 (16);
            if (bpp != 24 && bpp != 32)
                return null;
            return new DtlMetaData
            {
                Width  = header.ToUInt32 (8),
                Height = header.ToUInt32 (12),
                BPP    = bpp,
                Stride = header.ToInt32 (20),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (DtlMetaData)info;
//            var lineBuffer = new int[meta.iHeight][];
            file.Position = 0x18;
            for (int y = 0; y < meta.iHeight; ++y)
            {
                int n = file.ReadInt32();
//                lineBuffer[y] = new int[n * 2];
                for (int i = 0; i < n; ++i)
                {
//                    lineBuffer[y][i*2] = file.ReadInt32();    // x
//                    lineBuffer[y][i*2+1] = file.ReadInt32();  // number of pixels
                    file.ReadInt32();
                    file.ReadInt32();
                }
            }
            var pixels = file.ReadBytes (meta.Stride * meta.iHeight);
            PixelFormat format;
            if (24 == meta.BPP)
                format = PixelFormats.Bgr24;
            else
                format = PixelFormats.Bgra32;
            return ImageData.Create (meta, format, null, pixels, meta.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DtlcFormat.Write not implemented");
        }
    }
}
