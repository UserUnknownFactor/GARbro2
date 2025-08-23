using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.DirectDraw
{
    internal class DdsMetaData : ImageMetaData
    {
        public   int DataOffset { get; set; }
        public DdsPF PixelFlags { get; set; }
        public    string FourCC { get; set; }
        public    uint RBitMask { get; set; }
        public    uint GBitMask { get; set; }
        public    uint BBitMask { get; set; }
        public    uint ABitMask { get; set; }

        public override string GetComment()
        {
            return string.Format(
                " {0} x {1}{2}{3}", Width, Height,
                BPP != 0 ?
                string.Format(" x {0}bpp", BPP) : "", 
                !string.IsNullOrEmpty(FourCC) ?
                string.Format(" [{0}]", FourCC) : ""
            );
        }
    }

    [Flags]
    internal enum DdsPF : uint
    {
        AlphaPixels = 0x00000001,
        Alpha       = 0x00000002,
        FourCC      = 0x00000004,
        Rgb         = 0x00000040,
        Yuv         = 0x00000200,
        Luminance   = 0x00020000,
    }

    [Export(typeof(ImageFormat))]
    public class DdsFormat : ImageFormat
    {
        public override string         Tag { get { return "DDS"; } }
        public override string Description { get { return "Direct Draw Surface format"; } }
        public override uint     Signature { get { return 0x20534444; } } // 'DDS'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x80);
            if (header.Length < 0x80)
                return null;

            int dwSize = header.ToInt32 (4);
            if (dwSize < 0x7C)
                return null;

            var bitflags = (DdsPF)header.ToUInt32 (0x50);
            string fourCC = null;

            if (bitflags.HasFlag (DdsPF.FourCC))
                fourCC = Binary.GetCString (header.ToArray(), 0x54, 4, Encoding.ASCII);

            return new DdsMetaData
            {
                Width  = header.ToUInt32 (0x10),
                Height = header.ToUInt32 (0xC),
                BPP    = header.ToInt32 (0x58),
                PixelFlags = bitflags,
                FourCC = fourCC,
                RBitMask = header.ToUInt32 (0x5C),
                GBitMask = header.ToUInt32 (0x60),
                BBitMask = header.ToUInt32 (0x64),
                ABitMask = header.ToUInt32 (0x68),
                DataOffset = 4 + dwSize,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (DdsMetaData)info;

            if (meta.PixelFlags.HasFlag (DdsPF.Yuv | DdsPF.Luminance))
                throw new NotSupportedException ("YUV and Luminance DDS formats are not supported");

            stream.Position = meta.DataOffset;
            byte[] pixels;
            PixelFormat format = PixelFormats.Bgra32;

            if (string.IsNullOrEmpty (meta.FourCC))
            {
                if (meta.PixelFlags.HasFlag (DdsPF.Rgb) &&
                    (meta.RBitMask == 0 || meta.GBitMask == 0 || meta.BBitMask == 0))
                    throw new InvalidFormatException ("Invalid RGB mask configuration");

                pixels = ReadPixelData (stream.AsStream, meta);
                if (!meta.PixelFlags.HasFlag (DdsPF.AlphaPixels) || meta.ABitMask == 0)
                    format = PixelFormats.Bgr32;
            }
            else
            {
                pixels = DecompressTexture (stream, meta);
            }

            return ImageData.Create (info, format, null, pixels);
        }

        byte[] DecompressTexture (IBinaryStream stream, DdsMetaData meta)
        {
            byte[] input = ReadCompressedData (stream, meta);
            var dxt = new DxtDecoder (input, meta);

            string fourCC = meta.FourCC?.ToUpperInvariant();

            if (fourCC == "DXT1")
                return dxt.UnpackDXT1();
            else if (fourCC == "DXT3")
                return dxt.UnpackDXT3();
            else if (fourCC == "DXT5")
                return dxt.UnpackDXT5();
            else if (fourCC == "BC5U" || fourCC == "BC5S" || fourCC == "ATI2")
                return dxt.UnpackBC5();
            else
                throw new NotSupportedException($"Unsupported DDS format: {meta.FourCC}");
        }

        byte[] ReadCompressedData (IBinaryStream stream, DdsMetaData meta)
        {
            int blockSize  = GetBlockSize (meta.FourCC);
            int blocksWide = Math.Max (1, ((int)meta.Width + 3) / 4);
            int blocksHigh = Math.Max (1, ((int)meta.Height + 3) / 4);
            int dataSize   = blocksWide * blocksHigh * blockSize;

            return stream.ReadBytes (dataSize);
        }

        static int GetBlockSize (string fourCC)
        {
            if (fourCC == null)
                return 16;

            string upperFourCC = fourCC.ToUpperInvariant();
            if (upperFourCC == "DXT1")
                return 8;
            else if (upperFourCC == "DXT3" || upperFourCC == "DXT5" || 
                     upperFourCC == "BC5U" || upperFourCC == "BC5S" || 
                     upperFourCC == "ATI2")
                return 16;
            else
                return 16;
        }

        byte[] ReadPixelData (Stream stream, DdsMetaData info)
        {
            int srcPixelSize = (info.BPP + 7) / 8;
            int inputSize = (int)info.Width * (int)info.Height * srcPixelSize;
            var input = new byte[inputSize + 4];
            stream.Position = info.DataOffset;
            if (inputSize != stream.Read (input, 0, inputSize))
                throw new InvalidFormatException ("Unexpected end of file");

            // Fast path for common BGRA format
            if (32 == info.BPP && 
                0xFF0000 == info.RBitMask && 
                0x00FF00 == info.GBitMask && 
                0x0000FF == info.BBitMask)
            {
                return input;
            }

            var output = new byte[info.Width * info.Height * 4];
            int dst = 0;

            Func<int, uint> getPixel;
            if (info.BPP == 8)
                getPixel = x => input[x];
            else if (info.BPP == 16)
                getPixel = x => LittleEndian.ToUInt16 (input, x);
            else if (info.BPP == 24)
                getPixel = x => (uint)(input[x] | (input[x + 1] << 8) | (input[x + 2] << 16));
            else
                getPixel = x => LittleEndian.ToUInt32 (input, x);

            bool hasAlpha = info.PixelFlags.HasFlag (DdsPF.AlphaPixels) && info.ABitMask != 0;

            for (int src = 0; src < inputSize; src += srcPixelSize)
            {
                uint srcPixel = getPixel (src);
                output[dst++] = ConvertChannel (srcPixel, info.BBitMask);
                output[dst++] = ConvertChannel (srcPixel, info.GBitMask);
                output[dst++] = ConvertChannel (srcPixel, info.RBitMask);
                output[dst++] = hasAlpha ? ConvertChannel (srcPixel, info.ABitMask) : (byte)0xFF;
            }

            return output;
        }

        static byte ConvertChannel (uint pixel, uint mask)
        {
            if (mask == 0)
                return 0;

            uint value = pixel & mask;

            // Find the position of the least significant bit
            int shift = 0;
            uint tempMask = mask;
            while ((tempMask & 1) == 0)
            {
                tempMask >>= 1;
                shift++;
            }

            value >>= shift;

            // Scale to 8-bit
            int bits = BitCount (mask);
            if (bits == 8)
                return (byte)value;
            else if (bits < 8)
                return (byte)((value * 255) / ((1u << bits) - 1));
            else
                return (byte)(value >> (bits - 8));
        }

        static int BitCount (uint value)
        {
            int count = 0;
            while (value != 0)
            {
                count++;
                value &= value - 1;
            }
            return count;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("DDS writing is not implemented");
        }
    }
}
