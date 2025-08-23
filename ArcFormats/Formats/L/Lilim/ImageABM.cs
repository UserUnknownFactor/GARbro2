using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Lilim
{
    [Export(typeof(ImageFormat))]
    public class AbmFormat : ImageFormat
    {
        public override string         Tag { get { return "ABM"; } }
        public override string Description { get { return "LiLiM/Le.Chocolat compressed bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x46);
            if ('B' != header[0] || 'M' != header[1])
                return null;
            int type = header.ToInt16 (0x1C);
            uint frame_offset;
            int bpp = 24;
            if (1 == type || 2 == type)
            {
                int count = header.ToUInt16 (0x3A);
                if (count > 0xFF)
                    return null;
                frame_offset = header.ToUInt32 (0x42);
            }
            else if (32 == type || 24 == type || 8 == type || -8 == type)
            {
                uint unpacked_size = header.ToUInt32 (2);
                if (0 == unpacked_size || unpacked_size == stream.Length) // probably an ordinary bmp file
                    return null;
                frame_offset = header.ToUInt32 (0xA);
                if (8 == type)
                    bpp = 8;
            }
            else
                return null;
            if (frame_offset >= stream.Length)
                return null;
            return new AbmImageData
            {
                Width = header.ToUInt32 (0x12),
                Height = header.ToUInt32 (0x16),
                BPP = bpp,
                Mode = type,
                BaseOffset = frame_offset,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new AbmReader (stream, (AbmImageData)info))
                return reader.Image;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AbmFormat.Write not implemented");
        }
    }
}
