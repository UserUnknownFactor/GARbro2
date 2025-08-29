using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes;
using ICSharpCode.SharpZipLib.BZip2;

namespace GameRes.Formats.GameMaker
{
    internal class QoiImageDecoder : BinaryImageDecoder
    {
        private const uint MAGIC_FIOQ = 0x716F6966; // "fioq" in Little-Endian
        private const uint MAGIC_QOZ2 = 0x716F7A32; // "qoz2" in Little-Endian

        private byte[] m_pixels;
        private int m_width;
        private int m_height;

        public QoiImageDecoder(IBinaryStream input) : base(input)
        {
            input.Position = 0;
            DecodeQoi(input);

            Info = new ImageMetaData
            {
                Width = (uint)m_width,
                Height = (uint)m_height,
                BPP = 32 // Always RGBA
            };
        }

        private void DecodeQoi(IBinaryStream input)
        {
            uint magic = input.ReadUInt32();

            byte[] qoiData;

            m_width = input.ReadUInt16();
            m_height = input.ReadUInt16();
            if (magic == MAGIC_FIOQ)
            {
                input.Position = 12;
                qoiData = input.ReadBytes((int)(input.Length - 12));
            }
            else if (magic == MAGIC_QOZ2)
            {
                uint compressedSize = input.ReadUInt32(); // at offset 8
                input.Position = 12;
                byte[] compressedData = input.ReadBytes((int)(input.Length - 12));

                try
                {
                    using (var compressedStream = new MemoryStream(compressedData))
                    using (var bzipStream = new BZip2InputStream(compressedStream))
                    using (var decompressed = new MemoryStream())
                    {
                        bzipStream.CopyTo(decompressed);
                        qoiData = decompressed.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidFormatException($"BZip2 decompression failed: {ex.Message}");
                }
            }
            else
            {
                throw new InvalidFormatException($"Invalid GameMaker QOI magic: {magic:X8}");
            }

            DecodeGameMakerQoi(qoiData);
        }

        private void DecodeGameMakerQoi(byte[] data)
        {
            if (m_width <= 0 || m_height <= 0)
            {
                throw new InvalidFormatException($"Invalid dimensions: {m_width}x{m_height}");
            }
            
            m_pixels = new byte[m_width * m_height * 4];
            int runLength = 0;
            int totalPixelBytes = m_width * m_height * 4;
            // Reference stores as 0xAABBGGRR
            uint currentColor = 0xFF000000; // Opaque black
            uint[] colorTable = new uint[64];
            int srcPos = 0;
            int dstPos = 0;
            
            while (dstPos < totalPixelBytes && srcPos < data.Length)
            {
                int opcode = data[srcPos++];
                if (((uint)opcode & 0x80U) != 0) // Bit 7 set - color operations
                {
                    if ((opcode & 0x40) == 0) // 1-byte color delta (0x80-0xBF)
                    {
                        // Extract 2-bit signed deltas
                        currentColor = (currentColor & 0xFFFFFF00U) | (uint)(int)(((currentColor & 0xFF) + ((opcode & 0x30) << 26 >> 30)) & 0xFF);
                        currentColor = (currentColor & 0xFFFF00FFU) | (uint)(int)(((currentColor & 0xFF00) + ((opcode & 0xC) << 28 >> 22)) & 0xFF00);
                        currentColor = (currentColor & 0xFF00FFFFU) | (uint)(int)(((currentColor & 0xFF0000) + ((opcode & 3) << 30 >> 14)) & 0xFF0000);
                    }
                    else if ((opcode & 0x20) == 0) // 2-byte color delta (0xC0-0xDF)
                    {
                        if (srcPos < data.Length)
                        {
                            int deltaByte = data[srcPos++];
                            int packedDeltas = (opcode << 8) | deltaByte;
                            currentColor = (currentColor & 0xFFFFFF00U) | (uint)(int)(((currentColor & 0xFF) + ((packedDeltas & 0x1F00) << 19 >> 27)) & 0xFF);
                            currentColor = (currentColor & 0xFFFF00FFU) | (uint)(int)(((currentColor & 0xFF00) + ((packedDeltas & 0xF0) << 24 >> 20)) & 0xFF00);
                            currentColor = (currentColor & 0xFF00FFFFU) | (uint)(int)(((currentColor & 0xFF0000) + ((packedDeltas & 0xF) << 28 >> 12)) & 0xFF0000);
                        }
                    }
                    else if ((opcode & 0x10) == 0) // 3-byte color delta (0xE0-0xEF)
                    {
                        if (srcPos + 1 < data.Length)
                        {
                            int deltaByte1 = data[srcPos++];
                            int deltaByte2 = data[srcPos++];
                            int packedDeltas = (opcode << 16) | (deltaByte1 << 8) | deltaByte2;
                            currentColor = (currentColor & 0xFFFFFF00U) | (uint)(int)(((currentColor & 0xFF) + ((packedDeltas & 0xF8000) << 12 >> 27)) & 0xFF);
                            currentColor = (currentColor & 0xFFFF00FFU) | (uint)(int)(((currentColor & 0xFF00) + ((packedDeltas & 0x7C00) << 17 >> 19)) & 0xFF00);
                            currentColor = (currentColor & 0xFF00FFFFU) | (uint)(int)(((currentColor & 0xFF0000) + ((packedDeltas & 0x3E0) << 22 >> 11)) & 0xFF0000);
                            currentColor = (currentColor & 0xFFFFFFU) | (uint)(int)(((uint)((int)currentColor & -16777216) + ((packedDeltas & 0x1F) << 27 >> 3)) & 0xFF000000U);
                        }
                    }
                    else // Direct channel updates (0xF0-0xFF)
                    {
                        // Reference format: 0xAABBGGRR
                        if (((uint)opcode & 8U) != 0 && srcPos < data.Length) // Red channel (bits 0-7)
                        {
                            currentColor = (currentColor & 0xFFFFFF00U) | data[srcPos++];
                        }
                        if (((uint)opcode & 4U) != 0 && srcPos < data.Length) // Green channel (bits 8-15)
                        {
                            currentColor = (currentColor & 0xFFFF00FFU) | (uint)(data[srcPos++] << 8);
                        }
                        if (((uint)opcode & 2U) != 0 && srcPos < data.Length) // Blue channel (bits 16-23)
                        {
                            currentColor = (currentColor & 0xFF00FFFFU) | (uint)(data[srcPos++] << 16);
                        }
                        if (((uint)opcode & 1U) != 0 && srcPos < data.Length) // Alpha channel (bits 24-31)
                        {
                            currentColor = (currentColor & 0xFFFFFFU) | (uint)(data[srcPos++] << 24);
                        }
                    }
                    
                    // Extract components from 0xAABBGGRR format
                    int red = (int)(currentColor & 0xFF);
                    int green = (int)((currentColor >> 8) & 0xFF);
                    int blue = (int)((currentColor >> 16) & 0xFF);
                    int alpha = (int)((currentColor >> 24) & 0xFF);
                    
                    // Update color table with hash
                    int hashIndex = (red ^ green ^ blue ^ alpha) & 0x3F;
                    colorTable[hashIndex] = currentColor;
                    
                    // Write pixel in BGRA format
                    m_pixels[dstPos++] = (byte)blue;
                    m_pixels[dstPos++] = (byte)green;
                    m_pixels[dstPos++] = (byte)red;
                    m_pixels[dstPos++] = (byte)alpha;
                }
                else // Non-color operations
                {
                    if ((opcode & 0x40) == 0) // Color table lookup (0x00-0x3F)
                    {
                        currentColor = colorTable[opcode];
                    }
                    else if ((opcode & 0x20) == 0) // Short run (0x40-0x5F)
                    {
                        runLength = opcode & 0x1F;
                    }
                    else // Extended run (0x60-0x7F)
                    {
                        if (srcPos < data.Length)
                        {
                            int extensionByte = data[srcPos++];
                            runLength = (((opcode & 0x1F) << 8) | extensionByte) + 32;
                        }
                    }
                    
                    // Write current pixel (format: 0xAABBGGRR to BGRA)
                    m_pixels[dstPos++] = (byte)(currentColor >> 16); // Blue
                    m_pixels[dstPos++] = (byte)(currentColor >> 8);  // Green
                    m_pixels[dstPos++] = (byte)currentColor;         // Red
                    m_pixels[dstPos++] = (byte)(currentColor >> 24); // Alpha
                    
                    // Write run pixels
                    while (runLength > 0 && dstPos < totalPixelBytes)
                    {
                        m_pixels[dstPos++] = (byte)(currentColor >> 16); // Blue
                        m_pixels[dstPos++] = (byte)(currentColor >> 8);  // Green
                        m_pixels[dstPos++] = (byte)currentColor;         // Red
                        m_pixels[dstPos++] = (byte)(currentColor >> 24); // Alpha
                        runLength--;
                    }
                }
            }
        }

        protected override ImageData GetImageData()
        {
            if (m_pixels == null)
                return null;

            return ImageData.Create(Info, PixelFormats.Bgra32, null, m_pixels, m_width * 4);
        }
    }
}