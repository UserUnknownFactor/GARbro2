using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Vitamin
{
    internal class MfcMetaData : ImageMetaData
    {
        public int          AlphaSize;
        public SbiMetaData  BaseInfo;
    }

    [Export(typeof(ImageFormat))]
    public class MfcFormat : SbiFormat
    {
        public override string         Tag { get { return "MFC"; } }
        public override string Description { get { return "Vitamin image with alpha channel"; } }
        public override uint     Signature { get { return 0x0A43464D; } } // 'MFC'

        public MfcFormat ()
        {
            Extensions = new string[] { "mfc" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x18);

            if (header[4] != 1 || header[5] != 0 || header[6] != 1 || header[7] != 4)
                return null;

            int alpha_size = header.ToInt32 (12);
            using (var reg = new StreamRegion (stream.AsStream, alpha_size, true))
            using (var sbi = new BinaryStream (reg, stream.Name))
            {
                var info = base.ReadMetaData (sbi) as SbiMetaData;
                if (null == info)
                    return null;
                return new MfcMetaData
                {
                    Width = info.Width,
                    Height = info.Height,
                    BPP = 32,
                    AlphaSize = alpha_size,
                    BaseInfo = info,
                };
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (MfcMetaData)info;
            // XXX implemented like in reference code, width is expected to be even.
            var alpha = new byte[info.Width * info.Height / 2];
            stream.Position = 0x18;
            RleUnpack (stream.AsStream, meta.AlphaSize - 0x18, alpha);
            byte[] pixels;
            using (var reg = new StreamRegion (stream.AsStream, meta.AlphaSize, true))
            using (var sbi = new BinaryStream (reg, stream.Name))
            using (var reader = new SbiReader (sbi, meta.BaseInfo))
            {
                reader.Unpack();
                if (meta.BaseInfo.BPP != 32)
                {
                    var bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height, ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                                                  reader.Format, reader.Palette, reader.Data, reader.Stride);
                    bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);
                    int stride = (int)info.Width * 4;
                    pixels = new byte[stride * (int)info.Height];
                    bitmap.CopyPixels (pixels, stride, 0);
                }
                else
                    pixels = reader.Data;
            }
            using (var mem = new MemoryStream (alpha))
            using (var bits = new LsbBitStream (mem))
            {
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = (byte)(bits.GetBits (4) * 0x11);
                }
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MfcFormat.Write not implemented");
        }

        void RleUnpack (Stream input, int input_size, byte[] output)
        {
            int dst = 0;
            while (input_size > 0)
            {
                int b = input.ReadByte();
                if (-1 == b)
                    throw new EndOfStreamException();
                --input_size;
                if (b >= 0x80)
                {
                    int a = input.ReadByte();
                    if (-1 == a)
                        throw new EndOfStreamException();
                    --input_size;
                    b &= 0x7F;
                    for (int i = 0; i < b; ++i)
                    {
                        output[dst++] = (byte)a;
                    }
                }
                else
                {
                    input.Read (output, dst, b);
                    input_size -= b;
                    dst += b;
                }
            }
        }
    }
}
