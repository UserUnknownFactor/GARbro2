using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.FC01
{
    internal class ClmMetaData : ImageMetaData
    {
        public int  UnpackedSize;
        public uint DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class ClmFormat : ImageFormat
    {
        public override string         Tag { get { return "CLM"; } }
        public override string Description { get { return "F&C Co. image format"; } }
        public override uint     Signature { get { return  0x204D4C43; } } // 'CLM'

        public ClmFormat()
        {
            Extensions = new[] { "CLM" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x40);
            if (!header.AsciiEqual (4, "1.00"))
                return null;
            uint data_offset = header.ToUInt32 (0x10);
            if (data_offset < 0x40)
                return null;
            uint width  = header.ToUInt32 (0x1C);
            uint height = header.ToUInt32 (0x20);
            int bpp = header.ToInt32 (0x24);
            int unpacked_size = header.ToInt32 (0x28);
            return new ClmMetaData
            {
                Width   = width,
                Height  = height,
                BPP     = bpp,
                UnpackedSize = unpacked_size,
                DataOffset = data_offset,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (ClmMetaData)info;
            stream.Position = meta.DataOffset;
            PixelFormat format;
            BitmapPalette palette = null;
            if (8 == meta.BPP)
            {
                format = PixelFormats.Indexed8;
                palette = ReadPalette (stream.AsStream);
            }
            else if (24 == meta.BPP)
                format = PixelFormats.Bgr24;
            else if (32 == meta.BPP)
                format = PixelFormats.Bgr32;
            else
                throw new NotSupportedException ("Unsupported CLM color depth");

            int packed_size = (int)(stream.Length - stream.Position);
            var lzssReader = new MrgLzssReader (stream, packed_size, meta.UnpackedSize);
            lzssReader.Unpack();
            return ImageData.Create (info, format, palette, lzssReader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ClmFormat.Write not implemented");
        }
    }
}
