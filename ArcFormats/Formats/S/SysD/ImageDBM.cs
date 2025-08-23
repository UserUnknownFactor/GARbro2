using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.SysD
{
    [Export(typeof(ImageFormat))]
    public class DbmFormat : ImageFormat
    {
        public override string         Tag { get { return "DBM"; } }
        public override string Description { get { return "SYSD engine bitmap format"; } }
        public override uint     Signature { get { return 0x4D44; } } // 'DM'

        public DbmFormat ()
        {
            Signatures = new uint[] { 0x004D44, 0x014D44, 0x044D44 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            if (header.ToUInt32 (4) != file.Length)
                return null;
            return new ImageMetaData {
                Width  = header.ToUInt16 (0xA),
                Height = header.ToUInt16 (0xC),
                BPP = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x18;
            bool is_packed = file.ReadByte() != 0;
            int packed_size = file.ReadInt32() - 9;
            int unpacked_size = file.ReadInt32();
            var pixels = new byte[unpacked_size];
            if (is_packed)
            {
                using (var lzss = new LzssStream (file.AsStream, LzssMode.Decompress, true))
                {
                    lzss.Read (pixels, 0, pixels.Length);
                }
            }
            else
            {
                file.Read (pixels, 0, pixels.Length);
            }
            int stride = (int)info.Width * info.BPP / 8;
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DbmFormat.Write not implemented");
        }
    }
}
