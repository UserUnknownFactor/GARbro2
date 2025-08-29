using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes;
using ICSharpCode.SharpZipLib.BZip2;

namespace GameRes.Formats.GameMaker
{
    [Export(typeof(ImageFormat))]
    public class GMQoiFormat : ImageFormat
    {
        public override string         Tag { get { return "QOI/GM"; } }
        public override string Description { get { return "GameMaker QOI image format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return true; } }

        public GMQoiFormat()
        {
            Extensions = new string[] { "qoi" };
            Signatures = new uint[] { 0x716f6966, 0x716F7A32 }; // 'fioq', 'qoz2'
        }

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            var header = file.ReadHeader(8);
            uint magic = header.ToUInt32(0);

            if (magic != 0x716F6966 && magic != 0x716F7A32)
                return null;

            return new ImageMetaData
            {
                Width = header.ToUInt16(4),
                Height = header.ToUInt16(6),
                BPP = 32 // Always RGBA
            };
        }

        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            using (var decoder = new QoiImageDecoder(file))
                return decoder.Image;
        }

        public override void Write(Stream file, ImageData image)
        {
            var encoder = new QoiImageEncoder();
            encoder.Encode(file, image);
        }
    }
}