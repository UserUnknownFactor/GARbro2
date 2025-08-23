using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;
using Astc;

namespace GameRes.Formats.Cri
{
    internal class XtxMetaData : ImageMetaData
    {
        public byte Format;
        public uint DataOffset;
        public int  AlignedWidth;
        public int  AlignedHeight;
    }

    [Export(typeof(ImageFormat))]
    public class XtxFormat : ImageFormat
    {
        public override string         Tag { get { return "XTX"; } }
        public override string Description { get { return "Xbox 360 texture format"; } }
        public override uint     Signature { get { return  0x00787478; } } // 'xtx'

        public XtxFormat ()
        {
            Signatures = new uint[] { 0x00787478, 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x20).ToArray();
            if (!Binary.AsciiEqual (header, 0, "xtx\0"))
            {
                var header_size = LittleEndian.ToUInt32 (header, 0);
                if (header_size >= 0x1000)
                    return null;

                stream.Position = header_size;
                if (0x20 != stream.Read (header, 0, 0x20))
                    return null;
                if (!Binary.AsciiEqual (header, 0, "xtx\0"))
                    return null;
            }

            if (header[4] > 9) // Support formats 0-9
                return null;

            int aligned_width  = BigEndian.ToInt32 (header, 8);
            int aligned_height = BigEndian.ToInt32 (header, 0xC);
            if (aligned_width <= 0 || aligned_height <= 0)
                return null;

            uint width  = BigEndian.ToUInt32 (header, 0x10);
            uint height = BigEndian.ToUInt32 (header, 0x14);
            if (width == 0 || height == 0 || width > aligned_width || height > aligned_height)
                return null;

            return new XtxMetaData
            {
                Width   = width,
                Height  = height,
                OffsetX = BigEndian.ToInt32  (header, 0x18),
                OffsetY = BigEndian.ToInt32  (header, 0x1C),
                BPP     = GetBppForFormat(header[4]),
                Format  = header[4],
                AlignedWidth  = aligned_width,
                AlignedHeight = aligned_height,
                DataOffset  = (uint)stream.Position,
            };
        }

        private static int GetBppForFormat(byte format)
        {
            switch (format)
            {
                case 1:  return 16;  // BGR565
                case 0:
                case 2:
                case 9:
                default: return 32; // BGRA32 or DXT5
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = new XtxReader (stream.AsStream, (XtxMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("XtxFormat.Write not implemented");
        }
    }

    internal sealed class XtxReader
    {
        Stream      m_input;
        int         m_width;
        int         m_height;
        XtxMetaData m_info;

        public PixelFormat Format { get; private set; }

        public XtxReader (Stream input, XtxMetaData info)
        {
            m_input = input;
            m_info = info;
            m_width = (int)m_info.Width;
            m_height = (int)m_info.Height;
        }

        public byte[] Unpack ()
        {
            m_input.Position = m_info.DataOffset;
            Format = PixelFormats.Bgra32;
            switch (m_info.Format)
            {
            case 0:  return ReadTex0();
            case 1:  return ReadTex1();
            case 2:  return ReadTex2();
            case 9:  return ReadTex9();
            default:
                throw new NotSupportedException($"XTX format {m_info.Format} not supported");
            }
        }

        byte[] ReadTex0 ()
        {
            int output_stride = m_width * 4;
            var output  = new byte[output_stride * m_height];
            int total   = m_info.AlignedWidth * m_info.AlignedHeight;
            var texture = new byte[total * 4];
            m_input.Read (texture, 0, texture.Length);

            int src = 0;
            for (int i = 0; i < total; ++i)
            {
                int y = GetY (i, m_info.AlignedWidth, 4);
                int x = GetX (i, m_info.AlignedWidth, 4);
                if (y < m_height && x < m_width)
                {
                    int dst = output_stride * y + x * 4;
                    output[dst  ] = texture[src+3];
                    output[dst+1] = texture[src+2];
                    output[dst+2] = texture[src+1];
                    output[dst+3] = texture[src  ];
                }
                src += 4;
            }
            return output;
        }

        byte[] ReadTex1 ()
        {
            int output_stride = m_width * 2;
            var output  = new byte[output_stride * m_height];
            int total   = m_info.AlignedWidth * m_info.AlignedHeight;
            var texture = new byte[total * 2];
            m_input.Read (texture, 0, texture.Length);

            int src = 0;
            for (int i = 0; i < total; ++i)
            {
                int y = GetY (i, m_info.AlignedWidth, 2);
                int x = GetX (i, m_info.AlignedWidth, 2);
                if (y < m_height && x < m_width)
                {
                    int dst = output_stride * y + x * 2;
                    // Swap bytes for BGR565 format
                    output[dst  ] = texture[src+1];
                    output[dst+1] = texture[src  ];
                }
                src += 2;
            }

            Format = PixelFormats.Bgr565;
            return output;
        }

        byte[] ReadTex2 ()
        {
            int tex_width  = m_info.AlignedWidth  >> 2;
            int tex_height = m_info.AlignedHeight >> 2;
            int total = tex_width * tex_height;
            var texture = new byte[total * 16]; // DXT5 uses 16 bytes per 4x4 block
            var packed  = new byte[total * 16];

            m_input.Read (texture, 0, texture.Length);

            int src = 0;
            for (int i = 0; i < total; ++i)
            {
                int y = GetY (i, tex_width, 0x10);
                int x = GetX (i, tex_width, 0x10);

                if (x < tex_width && y < tex_height)
                {
                    int dst = (x + y * tex_width) * 16;
                    // Copy DXT5 block (16 bytes)
                    for (int j = 0; j < 8; ++j)
                    {
                        packed[dst++] = texture[src+1];
                        packed[dst++] = texture[src];
                        src += 2;
                    }
                }
                else
                {
                    src += 16; // Skip this block
                }
            }

            // Create DXT decoder with aligned dimensions for decompression
            var dxtInfo = new ImageMetaData
            {
                Width = (uint)m_info.AlignedWidth,
                Height = (uint)m_info.AlignedHeight,
                BPP = 32
            };
            var dxt = new DirectDraw.DxtDecoder (packed, dxtInfo);
            var decompressed = dxt.UnpackDXT5();

            // If actual size differs from aligned size, crop the result
            if (m_width != m_info.AlignedWidth || m_height != m_info.AlignedHeight)
                return CropImage(decompressed, m_info.AlignedWidth, m_info.AlignedHeight, m_width, m_height, 4);

            return decompressed;
        }

        byte[] ReadTex9()
        {
            // Assuming ASTC 4x4 format
            const int blockWidth = 4;
            const int blockHeight = 4;
            const int blockSizeBytes = 16;

            int blocksX = (m_info.AlignedWidth + blockWidth - 1) / blockWidth;
            int blocksY = (m_info.AlignedHeight + blockHeight - 1) / blockHeight;
            int compressedSize = blocksX * blocksY * blockSizeBytes;
            var compressedData = new byte[compressedSize];

            m_input.Read(compressedData, 0, compressedData.Length);

            var astcDecoder = new Astc.AstcDecoder();
            byte[] decompressedPixels = astcDecoder.DecodeASTC(
                compressedData,
                m_info.AlignedWidth,
                m_info.AlignedHeight,
                blockWidth,
                blockHeight
            );

            Format = PixelFormats.Bgra32;

            // Crop to actual dimensions if needed
            if (m_width != m_info.AlignedWidth || m_height != m_info.AlignedHeight)
                return CropImage(decompressedPixels, m_info.AlignedWidth, m_info.AlignedHeight, m_width, m_height, 4);

            return decompressedPixels;
        }

        byte[] CropImage(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, int bytesPerPixel)
        {
            if (sourceWidth == targetWidth && sourceHeight == targetHeight)
                return source;

            int sourceStride = sourceWidth * bytesPerPixel;
            int targetStride = targetWidth * bytesPerPixel;
            var result = new byte[targetStride * targetHeight];

            for (int y = 0; y < targetHeight; y++)
            {
                int srcOffset = y * sourceStride;
                int dstOffset = y * targetStride;
                Buffer.BlockCopy(source, srcOffset, result, dstOffset, targetStride);
            }

            return result;
        }

        static int GetY(int pixelIndex, int width, byte bytesPerPixel)
        {
            // Calculate shift amounts based on bytes per pixel
            int primaryShift = (bytesPerPixel >> 2) + (bytesPerPixel >> 1 >> (bytesPerPixel >> 2));
            int shiftedIndex = pixelIndex << primaryShift;

            // Extract bit patterns at different levels
            int lowBits  =  shiftedIndex & 0x3F;                 // Bottom 6 bits
            int midBits  = (shiftedIndex >> 2) & 0x1C0;          // Bits 8-6 shifted
            int highBits = (shiftedIndex >> 3) & 0x1FFFFE00;     // High bits shifted

            int combined = lowBits + midBits + highBits;

            // Calculate Y coordinate components
            int bit4 = (combined >> 4) & 1;  // Single bit from position 4

            // Complex middle component
            int maskSize = (bytesPerPixel << 6) - 1;
            int maskedValue = combined & maskSize & -0x20;  // Mask and align to 32
            int lowNibble = (lowBits + ((shiftedIndex >> 2) & 0xC0)) & 0xF;
            int middleComponent = (maskedValue + (lowNibble << 1)) >> (primaryShift + 3);
            middleComponent &= -2;  // Clear lowest bit

            // High component for larger textures
            int bit10 = (shiftedIndex >> 10) & 2;
            int highBit = (combined >> (primaryShift + 6)) & 1;
            int widthBlocks = (width + 31) >> 5;  // Width in 32-pixel blocks
            int blockOffset = (combined >> (primaryShift + 7)) / widthBlocks;
            int highComponent = (bit10 + highBit + (blockOffset << 2)) << 3;

            return bit4 + middleComponent + highComponent;
        }

        static int GetX(int pixelIndex, int width, byte bytesPerPixel)
        {
            // Calculate shift amounts based on bytes per pixel
            int primaryShift = (bytesPerPixel >> 2) + (bytesPerPixel >> 1 >> (bytesPerPixel >> 2));
            int shiftedIndex = pixelIndex << primaryShift;

            // Extract bit patterns at different levels
            int lowBits  =  shiftedIndex & 0x3F;                 // Bottom 6 bits
            int midBits  = (shiftedIndex >> 2) & 0x1C0;          // Bits 8-6 shifted
            int highBits = (shiftedIndex >> 3) & 0x1FFFFE00;     // High bits shifted

            int combined = lowBits + midBits + highBits;

            // Calculate X coordinate components
            // Low component: XOR pattern for local X within tile
            int xorPattern = (combined >> 1) ^ (combined ^ (combined >> 1)) & 0xF;
            int tileMask = (bytesPerPixel << 3) - 1;
            int lowComponent = (tileMask & xorPattern) >> primaryShift;

            // High component: tile X position
            int byte6to13 = (shiftedIndex >> 6) & 0xFF;
            int highShifted = (combined >> (primaryShift + 5)) & 0xFE;
            int tileX = (byte6to13 + highShifted) & 3;

            // Calculate horizontal tile offset
            int widthBlocks = (width + 31) >> 5;  // Width in 32-pixel blocks
            int blockX = (combined >> (primaryShift + 7)) % widthBlocks;

            int highComponent = (tileX + (blockX << 2)) << 3;

            return lowComponent + highComponent;
        }
    }
}