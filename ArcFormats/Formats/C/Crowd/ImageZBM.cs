using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Crowd
{
    [Export(typeof(ImageFormat))]
    public class ZbmFormat : ImageFormat
    {
        public override string         Tag { get { return "ZBM"; } }
        public override string Description { get { return "Crowd LZ-compressed bitmap"; } }
        public override uint     Signature { get { return 0x44445A53u; } } // 'SZDD'
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 0x0e;
            using (var lz = new LzssReader (stream.AsStream, 100, 54)) // extract BMP header
            {
                lz.FrameSize = 0x1000;
                lz.FrameFill = 0x20;
                lz.FrameInitPos = 0x1000 - 0x10;
                lz.Unpack();
                var header = lz.Data;
                for (int i = 0; i < 54; ++i)
                    header[i] ^= 0xff;
                using (var bmp = new BinMemoryStream (header, stream.Name))
                    return Bmp.ReadMetaData (bmp);
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            if (stream.Length > int.MaxValue)
                throw new FileSizeException();
            var header = stream.ReadHeader (14);
            int data_length = header.ToInt32 (10);
            int input_length = (int)(stream.Length-stream.Position);
            using (var lz = new LzssReader (stream.AsStream, input_length, data_length))
            {
                lz.FrameSize = 0x1000;
                lz.FrameFill = 0x20;
                lz.FrameInitPos = 0x1000 - 0x10;
                lz.Unpack();
                var data = lz.Data;
                int count = Math.Min (100, data.Length);
                for (int i = 0; i < count; ++i)
                    data[i] ^= 0xff;
                using (var bmp = new BinMemoryStream (data, stream.Name))
                    return Bmp.Read (bmp, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("ZbmFormat.Write not implemented");
        }
    }
}
