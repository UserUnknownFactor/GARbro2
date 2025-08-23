using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Slg
{
    internal class TimMetaData : ImageMetaData
    {
        public uint Seed;
        public int  Stride;
    }

    [Export(typeof(ImageFormat))]
    public class TimFormat : ImageFormat
    {
        public override string         Tag { get { return "TIM/SLG"; } }
        public override string Description { get { return "SLG system encrypted image"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".tim") || file.Length < 1024)
                return null;
            var header = file.ReadBytes (1024);
            uint seed = (uint)(header[18] | header[42] << 8 | header[98] << 16 | header[118] << 24);
            var rnd = new RandomGenerator (seed);
            for (int i = 512; i < 1024; ++i)
            {
                header[i] -= (byte)rnd.Next();
            }
            if (!Binary.AsciiEqual (header, 0x2DE, "TIM Data Ver 1.00\0"))
                return null;
            return new TimMetaData {
                Width  = header.ToUInt32 (0x248),
                Height = header.ToUInt32 (0x29C),
                Stride = header.ToInt32 (0x2BA),
                Seed   = rnd.Seed,
                BPP    = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (TimMetaData)info;
            file.Position = 1024;
            var pixels = file.ReadBytes (meta.Stride * (int)meta.Height);
            var key_table = GenerateDecryptTable (meta.Seed);
            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] -= key_table[i & 0xFFF];
            }
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, meta.Stride);
        }

        internal byte[] GenerateDecryptTable (uint seed)
        {
            var rnd = new RandomGenerator (seed);
            var table = new byte[0x1000];
            for (int i = 0; i < table.Length; ++i)
                table[i] = (byte)rnd.Next();
            return table;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("TimFormat.Write not implemented");
        }
    }
}
