using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using GameRes;
using GameRes.Utility;

namespace Gameres.Formats.Elf
{
    internal class GpxMetaData : ImageMetaData
    {
        public uint Mode;
    }

    [Export(typeof(ImageFormat))]
    public class GpxFormat : ImageFormat
    {
        public override string         Tag { get { return "GPX"; } }
        public override string Description { get { return "ELF 8BPP indexed image format"; } }
        public override uint     Signature { get { return  0; } }

        public GpxFormat()
        {
            Extensions = new string[] { "gpx" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            if (!stream.Name.EndsWith (".gpx", StringComparison.OrdinalIgnoreCase))
                return null;

            var header = stream.ReadHeader (10);
            if (header.Length != 10) return null;

            var width  = LittleEndian.ToUInt16 (header, 4);
            var height = LittleEndian.ToUInt16 (header, 6);
            var mode   = LittleEndian.ToUInt16 (header, 8);

            if (width == 0 || height == 0 || width > 8192 || height > 8192) return null;
            if (mode  != 0 && mode != 1) return null;

            return new GpxMetaData
            {
                OffsetX = LittleEndian.ToUInt16 (header, 0),
                OffsetY = LittleEndian.ToUInt16 (header, 2),
                Width   = width,
                Height  = height,
                Mode    = mode,
                BPP     = 8
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GpxMetaData)info;
            var reader = new GpxReader (stream, meta);
            var (indices, palette) = reader.Decode();
            return ImageData.Create (info, PixelFormats.Indexed8, palette, indices);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var writer = new BinaryWriter (file))
            {
                var bitmap = image.Bitmap;
                BitmapPalette palette = bitmap.Palette;

                if (bitmap.Format != PixelFormats.Indexed8)
                {
                    var quantizer = new PaletteQuantizer (236); // Leave room for reserved colors
                    bitmap = quantizer.Quantize (bitmap);
                    palette = bitmap.Palette;
                }

                writer.Write ((ushort)image.OffsetX);
                writer.Write ((ushort)image.OffsetY);
                writer.Write ((ushort)bitmap.PixelWidth);
                writer.Write ((ushort)bitmap.PixelHeight);
                writer.Write ((ushort)0); // mode 0

                // Write palette (236 colors, skipping first and last 10)
                for (int i = 0; i < Math.Min (236, palette.Colors.Count); ++i)
                {
                    var color = palette.Colors[i];
                    writer.Write (color.R);
                    writer.Write (color.G);
                    writer.Write (color.B);
                }

                // Pad palette if needed
                for (int i = palette.Colors.Count; i < 236; ++i)
                {
                    writer.Write ((byte)0);
                    writer.Write ((byte)0);
                    writer.Write ((byte)0);
                }

                // Get indexed pixels
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight];
                bitmap.CopyPixels (pixels, bitmap.PixelWidth, 0);

                // Offset indices to account for reserved colors
                for (int i = 0; i < pixels.Length; ++i)
                    pixels[i] = (byte)(pixels[i] + 10);

                // NOTE: Simplified - Compress pixels using RLE
                CompressGpxPixels (writer, pixels, bitmap.PixelWidth, bitmap.PixelHeight);
            }
        }

        private void CompressGpxPixels (BinaryWriter writer, byte[] pixels, int width, int height)
        {
            // NOTE: Simplified - raw with bit markers
            var bitWriter = new BitWriter (writer.BaseStream);

            for (int i = 0; i < pixels.Length; ++i)
            {
                bitWriter.WriteBit (1); // literal marker
                bitWriter.WriteBits (pixels[i], 8);
            }

            bitWriter.Flush();
        }

        internal sealed class GpxReader
        {
            private readonly IBinaryStream _stream;
            private readonly GpxMetaData _info;

            private static readonly int[] MODE0_LOOKUP_3BIT = { -1, -2, -4, -6, -8, -12, -16, -20 };
            private static readonly int[] MODE0_LOOKUP_4BIT = { -20, -16, -12, -8, -6, -4, -2, -1, 0, 1, 2, 4, 6, 8, 12, 16 };
            private static readonly int[] MODE1_LOOKUP_3BIT = { 1, 2, 4, 6, 8, 12, 16, 20 };
            private static readonly int[] MODE1_LOOKUP_4BIT = { 20, 16, 12, 8, 6, 4, 2, 1, 0, -1, -2, -4, -6, -8, -12, -16 };

            public GpxReader (IBinaryStream stream, GpxMetaData info)
            {
                _stream = stream;
                _info = info;
            }

            public (byte[] indices, BitmapPalette palette) Decode()
            {
                _stream.Position = 10;
                var palette = ReadPalette();

                var bs = new BitStream (_stream);
                byte[] indices = _info.Mode == 0
                    ? DecodeMode0 (bs, (int)_info.Width, (int)_info.Height)
                    : DecodeMode1 (bs, (int)_info.Width, (int)_info.Height);

                return (indices, palette);
            }

            private BitmapPalette ReadPalette()
            {
                byte[] paletteData = new byte[256 * 3];
                byte[] defaultColor = { 0, 0, 0 };

                // Fill first and last 10 entries with default
                for (int i = 0; i < 10; i++)
                {
                    Buffer.BlockCopy (defaultColor, 0, paletteData, i * 3, 3);
                    Buffer.BlockCopy (defaultColor, 0, paletteData, (246 + i) * 3, 3);
                }

                // Read middle 236 entries directly into buffer
                int bytesRead = _stream.Read (paletteData, 10 * 3, 236 * 3);
                if (bytesRead != 236 * 3)
                {
                    throw new InvalidDataException ("Unexpected end of stream while reading palette data");
                }

                using (var ms = new MemoryStream (paletteData))
                {
                    return GpxFormat.ReadPalette (ms, 256, PaletteFormat.Rgb);
                }
            }

            private int ReadVariableLength (BitStream bs)
            {
                if (bs.ReadBit() == 1) return bs.ReadBit()    + 2;
                if (bs.ReadBit() == 1) return bs.ReadBits (2) + 4;
                if (bs.ReadBit() == 1) return bs.ReadBits (3) + 8;
                if (bs.ReadBit() == 1) return bs.ReadBits (6) + 16;
                if (bs.ReadBit() == 1) return bs.ReadBits (8) + 80;
                return bs.ReadBits (10) + 336;
            }

            private void CopyPixels (byte[] pixels, int width, int height, int dstX, int dstY, int srcX, int srcY, int length, byte defaultIdx)
            {
                for (int i = 0; i < length; i++)
                {
                    int x = dstX + i;
                    if (x >= width || dstY >= height) break;

                    int dstPos = dstY * width + x;
                    if (srcX + i >= 0 && srcX + i < width && srcY >= 0 && srcY < height)
                        pixels[dstPos] = pixels[srcY * width + srcX + i];
                    else if (x > 0)
                        pixels[dstPos] = pixels[dstPos - 1];
                    else if (dstY > 0)
                        pixels[dstPos] = pixels[dstPos - width];
                    else
                        pixels[dstPos] = defaultIdx;
                }
            }

            private byte[] DecodeMode0 (BitStream bs, int width, int height)
            {
                var pixels = new byte[width * height];

                for (int y = 0; y < height; y++)
                {
                    int x = 0;
                    while (x < width)
                    {
                        int bit1 = bs.ReadBit();
                        if (bit1 == -1) break;

                        if (bit1 == 1)
                        {
                            int idx = bs.ReadBits (8);
                            if (idx == -1) break;
                            pixels[y * width + x] = (byte)idx;
                            x++;
                            continue;
                        }

                        int bit2 = bs.ReadBit();
                        if (bit2 == -1) break;

                        int refX, refY;
                        if (bit2 == 1)
                        {
                            int bit3 = bs.ReadBit();
                            if (bit3 == -1) break;
                            if (bit3 == 1)
                            {
                                int bits4 = bs.ReadBits (4);
                                if (bits4 == -1) break;
                                refX = x + MODE0_LOOKUP_4BIT[bits4];
                                refY = y - 1;
                            }
                            else
                            {
                                int bits3 = bs.ReadBits (3);
                                if (bits3 == -1) break;
                                refX = x + MODE0_LOOKUP_3BIT[bits3];
                                refY = y;
                            }
                        }
                        else
                        {
                            int bit3 = bs.ReadBit();
                            if (bit3 == -1) break;
                            int distance;
                            if (bit3 == 1)
                            {
                                int bit4 = bs.ReadBit();
                                if (bit4 == -1) break;
                                distance = 2 + bit4;
                            }
                            else
                            {
                                int bits2 = bs.ReadBits (2);
                                if (bits2 == -1) break;
                                distance = 4 + bits2;
                            }
                            int bits4 = bs.ReadBits (4);
                            if (bits4 == -1) break;
                            refX = x + MODE0_LOOKUP_4BIT[bits4];
                            refY = y - distance;
                        }

                        int length = ReadVariableLength (bs);
                        if (length == -1) break;
                        CopyPixels (pixels, width, height, x, y, refX, refY, length, 0);
                        x += length;
                    }
                }
                return pixels;
            }

            private byte[] DecodeMode1 (BitStream bs, int width, int height)
            {
                var pixels = new byte[width * height];

                for (int x = 0; x < width; x++)
                {
                    int y = 0;
                    while (y < height)
                    {
                        int bit1 = bs.ReadBit();
                        if (bit1 == -1) break;

                        if (bit1 == 1)
                        {
                            int idx = bs.ReadBits (8);
                            if (idx == -1) break;
                            pixels[y * width + x] = (byte)idx;
                            y++;
                            continue;
                        }

                        int bit2 = bs.ReadBit();
                        if (bit2 == -1) break;

                        int refX, refY;
                        if (bit2 == 0)
                        {
                            int distType = bs.ReadBit();
                            if (distType == -1) break;
                            int distance;
                            if (distType == 0)
                            {
                                int bits2 = bs.ReadBits (2);
                                if (bits2 == -1) break;
                                distance = 4 + bits2;
                            }
                            else
                            {
                                int bit3 = bs.ReadBit();
                                if (bit3 == -1) break;
                                distance = 2 + bit3;
                            }
                            refX = x - distance;
                            int bits4 = bs.ReadBits (4);
                            if (bits4 == -1) break;
                            refY = y - MODE1_LOOKUP_4BIT[bits4];
                        }
                        else
                        {
                            int refType = bs.ReadBit();
                            if (refType == -1) break;
                            if (refType == 1)
                            {
                                refX = x - 1;
                                int bits4 = bs.ReadBits (4);
                                if (bits4 == -1) break;
                                refY = y - MODE1_LOOKUP_4BIT[bits4];
                            }
                            else
                            {
                                refX = x;
                                int bits3 = bs.ReadBits (3);
                                if (bits3 == -1) break;
                                refY = y - MODE1_LOOKUP_3BIT[bits3];
                            }
                        }

                        int length = ReadVariableLength (bs);
                        if (length == -1) break;
                        for (int i = 0; i < length && y + i < height; i++)
                        {
                            int dstPos = (y + i) * width + x;
                            if (refX >= 0 && refX < width && refY + i >= 0 && refY + i < height)
                                pixels[dstPos] = pixels[(refY + i) * width + refX];
                        }
                        y += length;
                    }
                }
                return pixels;
            }
        }

        internal class BitStream
        {
            private readonly IBinaryStream _stream;
            private byte _bitMask, _bitBuf;

            public BitStream (IBinaryStream stream)
            {
                _stream = stream;
                _bitMask = 0;
                _bitBuf = 0;
            }

            public int ReadBit ()
            {
                if (_bitMask == 0)
                {
                    int? b = _stream.ReadUInt8();
                    if (b == null) return -1;
                    _bitBuf = (byte)b;
                    _bitMask = 0x80;
                }
                int result = (_bitBuf & _bitMask) != 0 ? 1 : 0;
                _bitMask >>= 1;
                return result;
            }

            public int ReadBits (int n)
            {
                int v = 0;
                for (int i = 0; i < n; i++)
                {
                    int bit = ReadBit();
                    if (bit == -1) return -1;
                    v = (v << 1) | bit;
                }
                return v;
            }
        }
    }
}