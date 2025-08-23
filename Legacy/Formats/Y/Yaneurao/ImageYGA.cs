using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Yaneurao
{
    internal class YgaMetaData : ImageMetaData
    {
        public int  UnpackedSize;
        public bool IsCompressed;
    }

    [Export(typeof(ImageFormat))]
    public class YgaFormat : ImageFormat
    {
        public override string         Tag { get { return "YGA"; } }
        public override string Description { get { return "Yaneurao image format"; } }
        public override uint     Signature { get { return 0x616779; } } // 'yga'

        public YgaFormat ()
        {
            Extensions = new string[] { "yga", "epf" };
            Signatures = new uint[] { 0x616779, 0x667065 }; // 'epf'
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            int compression = header.ToInt32 (0xC);
            if (compression > 1)
                return null;
            return new YgaMetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP    = 32,
                UnpackedSize = header.ToInt32 (0x10),
                IsCompressed = compression != 0,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (YgaMetaData)info;
            file.Position = 0x18;
            var pixels = new byte[meta.UnpackedSize];
            if (meta.IsCompressed)
            {
                using (var input = new LzssStream (file.AsStream, LzssMode.Decompress, true))
                    input.Read (pixels, 0, meta.UnpackedSize);
            }
            else
                file.Read (pixels, 0, meta.UnpackedSize);
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("YgaFormat.Write not implemented");
        }
    }
}
