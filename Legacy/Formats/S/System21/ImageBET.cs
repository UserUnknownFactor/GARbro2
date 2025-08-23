using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;

namespace GameRes.Formats.System21
{
    [Export(typeof(ImageFormat))]
    public class BetFormat : ImageFormat
    {
        public override string         Tag { get { return "BET"; } }
        public override string Description { get { return "System21 image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".bet"))
                return null;
            uint w = file.ReadUInt32();
            uint h = file.ReadUInt32();
            int bpp = file.ReadUInt16();
            if ((bpp != 24 && bpp != 8) || 0 == w || w > 0x8000 || 0 == h || h > 0x8000)
                return null;
            return new ImageMetaData { Width = w, Height = h, BPP = bpp };
        }

        public override ImageData Read (IBinaryStream input, ImageMetaData info)
        {
            int stride = (int)info.Width * info.BPP / 8;
            int bitmap_length = stride * (int)info.Height;
            var pixels = new byte[Math.Max (bitmap_length, 10)];
            input.Read (pixels, 0, 10); // input might be non-seekable
            BitmapPalette palette = null;
            if (8 == info.BPP)
                palette = ReadPalette (input.AsStream);
            if (input.Read (pixels, 0, bitmap_length) != bitmap_length)
                throw new InvalidFormatException();
            for (int i = 0; i < bitmap_length; ++i)
                pixels[i] ^= 0xFF;
            PixelFormat format = 8 == info.BPP ? PixelFormats.Indexed8 : PixelFormats.Bgr24;
            return ImageData.CreateFlipped (info, format, palette, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BetFormat.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class LzBetFormat : BetFormat
    {
        public override string         Tag { get { return "BET/SZDD"; } }
        public override string Description { get { return "System21 compressed image format"; } }
        public override uint     Signature { get { return 0x44445A53; } } // 'SZDD'
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".bet"))
                return null;
            using (var input = OpenLzStream (file))
                return base.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = OpenLzStream (file))
                return base.Read (input, info);
        }

        IBinaryStream OpenLzStream (IBinaryStream input)
        {
            input.Position = 0xE;
            var lz = new LzssStream (input.AsStream, LzssMode.Decompress, true);
            lz.Config.FrameSize = 0x1000;
            lz.Config.FrameFill = 0x20;
            lz.Config.FrameInitPos = 0x1000 - 0x10;
            return new BinaryStream (lz, input.Name); // XXX stream is unseekable
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("LzBetFormat.Write not implemented");
        }
    }
}
