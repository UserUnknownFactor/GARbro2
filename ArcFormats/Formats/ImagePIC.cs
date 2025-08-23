using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Sprite
{
    [Export(typeof(ImageFormat))]
    public class PicFormat : ImageFormat
    {
        public override string         Tag { get { return "PIC/SPRITE"; } }
        public override string Description { get { return "Soft House Sprite bitmap format"; } }
        public override uint     Signature { get { return 0x434950; } } // 'PIC'
        public override bool      CanWrite { get { return true; } }

        const int HeaderSize = 10;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            using (var bmp = OpenAsBitmap (file))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var bmp = OpenAsBitmap (file))
                return Bmp.Read (bmp, info);
        }

        IBinaryStream OpenAsBitmap (IBinaryStream input)
        {
            var header = new byte[HeaderSize];
            header[0] = (byte)'B';
            header[1] = (byte)'M';
            LittleEndian.Pack ((uint)input.Length, header, 2);
            Stream stream = new StreamRegion (input.AsStream, HeaderSize, true);
            stream = new PrefixStream (header, stream);
            return new BinaryStream (stream, input.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var bmp = new MemoryStream())
            {
                Bmp.Write (bmp, image);
                var header = new byte[HeaderSize];
                header[0] = (byte)'P';
                header[1] = (byte)'I';
                header[2] = (byte)'C';
                file.Write (header, 0, HeaderSize);
                bmp.Position = HeaderSize;
                bmp.CopyTo (file);
            }
        }
    }
}
