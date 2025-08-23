using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media.Imaging;

// [980925][Candy Soft] Osananajimi

namespace GameRes.Formats.Interheart
{
    internal class BmpRleMetaData : ImageMetaData
    {
        public int HeaderSize;
        public int UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class BmpRleFormat : ImageFormat
    {
        public override string         Tag { get { return "BMP/RLE"; } }
        public override string Description { get { return "Candy Soft RLE-compressed bitmap"; } }
        public override uint     Signature { get { return 0x32504D42; } } // 'BMP24'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x26);
            if (!header.AsciiEqual ("BMP24RLE")) // also "BMP08RLE" and "BMP16RLE"
                return null;
            return new BmpRleMetaData {
                Width  = header.ToUInt32 (0x1A),
                Height = header.ToUInt32 (0x1E),
                BPP    = header.ToUInt16 (0x24),
                HeaderSize = header.ToInt32 (0x12),
                UnpackedSize = header.ToInt32 (0x0A),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (BmpRleMetaData)info;
            var output = new byte[meta.UnpackedSize];
            file.Position = 8;
            file.Read (output, 0, meta.HeaderSize);
            var buffer = new byte[3];
            int dst = meta.HeaderSize;
            // XXX BMMP08 and BMP16 bitmaps use exactly the same algorithm, just different pixel size
            file.Read (buffer, 0, 3);
            while (dst + 3 <= output.Length)
            {
                byte r = buffer[0];
                byte g = buffer[1];
                byte b = buffer[2];
                output[dst++] = b;
                output[dst++] = g;
                output[dst++] = r;
                file.Read (buffer, 0, 3);
                if (r == buffer[0] && g == buffer[1] && b == buffer[2])
                {
                    if (dst + 3 > output.Length)
                        break;
                    output[dst++] = b;
                    output[dst++] = g;
                    output[dst++] = r;
                    int count = file.ReadUInt16();
                    while (count --> 0)
                    {
                        output[dst++] = b;
                        output[dst++] = g;
                        output[dst++] = r;
                    }
                    file.Read (buffer, 0, 3);
                }
            }
            using (var input = new BinMemoryStream (output))
            {
                var decoder = new BmpBitmapDecoder (input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                return new ImageData (frame, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BmpRleFormat.Write not implemented");
        }
    }
}
