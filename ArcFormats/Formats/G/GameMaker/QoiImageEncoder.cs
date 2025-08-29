using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.GameMaker
{
    internal class QoiImageEncoder
    {
        public void Encode(Stream output, ImageData image)
        {
            var bitmap = image.Bitmap;
            if (bitmap.Format != PixelFormats.Bgra32)
            {
                bitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            }

            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            EncodeGameMakerQoi(output, pixels, width, height);
        }

        private void EncodeGameMakerQoi(Stream output, byte[] pixels, int width, int height)
        {
            int maxSize = width * height * 5 + 12 + 4;
            if (maxSize < 4096)
            {
                maxSize = 4096;
            }

            uint[] colorTable = new uint[64];
            byte[] outputBuffer = new byte[maxSize];
            
            // Write header
            outputBuffer[0] = 102; // 'f'
            outputBuffer[1] = 105; // 'i'
            outputBuffer[2] = 111; // 'o'
            outputBuffer[3] = 113; // 'q'
            outputBuffer[4] = (byte)(width & 0xFF);
            outputBuffer[5] = (byte)((width >> 8) & 0xFF);
            outputBuffer[6] = (byte)(height & 0xFF);
            outputBuffer[7] = (byte)((height >> 8) & 0xFF);
            
            int totalPixelBytes = width * height * 4;
            uint currentColor = 0xFF000000; // Start with opaque black (ARGB)
            uint previousColor = currentColor;
            int runLength = 0;
            int outputPos = 12;
            int pixelPos = 0;
            
            while (pixelPos < totalPixelBytes)
            {
                // Read pixel in BGRA format from source
                int blue = pixels[pixelPos++];
                int green = pixels[pixelPos++];
                int red = pixels[pixelPos++];
                int alpha = pixels[pixelPos++];
                
                // Convert to ARGB format for internal processing
                currentColor = (uint)((alpha << 24) | (blue << 16) | (green << 8) | red);
                
                if (currentColor == previousColor)
                {
                    runLength++;
                }
                
                // Output run if we hit max length, color changed, or reached end
                if (runLength > 0 && (runLength == 8224 || currentColor != previousColor || pixelPos == totalPixelBytes))
                {
                    if (runLength < 33)
                    {
                        runLength--;
                        outputBuffer[outputPos++] = (byte)(0x40 | runLength);
                    }
                    else
                    {
                        runLength -= 33;
                        outputBuffer[outputPos++] = (byte)(0x60 | (runLength >> 8));
                        outputBuffer[outputPos++] = (byte)(runLength & 0xFF);
                    }
                    runLength = 0;
                }
                
                if (currentColor != previousColor)
                {
                    int hashIndex = (red ^ green ^ blue ^ alpha) & 0x3F;
                    
                    if (colorTable[hashIndex] == currentColor)
                    {
                        // Color found in table
                        outputBuffer[outputPos++] = (byte)hashIndex;
                    }
                    else
                    {
                        // Update color table
                        colorTable[hashIndex] = currentColor;
                        
                        // Extract previous color components
                        int prevRed = (int)(previousColor & 0xFF);
                        int prevGreen = (int)((previousColor >> 8) & 0xFF);
                        int prevBlue = (int)((previousColor >> 16) & 0xFF);
                        int prevAlpha = (int)((previousColor >> 24) & 0xFF);
                        
                        // Calculate deltas
                        int deltaRed = red - prevRed;
                        int deltaGreen = green - prevGreen;
                        int deltaBlue = blue - prevBlue;
                        int deltaAlpha = alpha - prevAlpha;
                        
                        if (deltaRed > -17 && deltaRed < 16 && deltaGreen > -17 && deltaGreen < 16 && 
                            deltaBlue > -17 && deltaBlue < 16 && deltaAlpha > -17 && deltaAlpha < 16)
                        {
                            if (deltaAlpha == 0 && deltaRed > -3 && deltaRed < 2 && 
                                deltaGreen > -3 && deltaGreen < 2 && deltaBlue > -3 && deltaBlue < 2)
                            {
                                // 1-byte delta encoding
                                outputBuffer[outputPos++] = (byte)(0x80 | 
                                    ((deltaRed << 4) & 0x30) | 
                                    ((deltaGreen << 2) & 0x0C) | 
                                    (deltaBlue & 0x03));
                            }
                            else if (deltaAlpha == 0 && deltaRed > -17 && deltaRed < 16 && 
                                     deltaGreen > -9 && deltaGreen < 8 && deltaBlue > -9 && deltaBlue < 8)
                            {
                                // 2-byte delta encoding
                                outputBuffer[outputPos++] = (byte)(0xC0 | (deltaRed & 0x1F));
                                outputBuffer[outputPos++] = (byte)(((deltaGreen << 4) & 0xF0) | (deltaBlue & 0x0F));
                            }
                            else
                            {
                                // 3-byte delta encoding
                                outputBuffer[outputPos++] = (byte)(0xE0 | ((deltaRed >> 1) & 0x0F));
                                outputBuffer[outputPos++] = (byte)(((deltaRed << 7) & 0x80) | 
                                                                  ((deltaGreen << 2) & 0x7C) | 
                                                                  ((deltaBlue >> 3) & 0x03));
                                outputBuffer[outputPos++] = (byte)(((deltaBlue << 5) & 0xE0) | (deltaAlpha & 0x1F));
                            }
                        }
                        else
                        {
                            // Direct color encoding
                            outputBuffer[outputPos++] = (byte)(0xF0 | 
                                ((deltaRed != 0) ? 0x08 : 0) |
                                ((deltaGreen != 0) ? 0x04 : 0) |
                                ((deltaBlue != 0) ? 0x02 : 0) |
                                ((deltaAlpha != 0) ? 0x01 : 0));
                                
                            if (deltaRed != 0)
                                outputBuffer[outputPos++] = (byte)red;
                            if (deltaGreen != 0)
                                outputBuffer[outputPos++] = (byte)green;
                            if (deltaBlue != 0)
                                outputBuffer[outputPos++] = (byte)blue;
                            if (deltaAlpha != 0)
                                outputBuffer[outputPos++] = (byte)alpha;
                        }
                    }
                }
                previousColor = currentColor;
            }
            
            // Write size to header
            int dataSize = outputPos - 12;
            outputBuffer[8] = (byte)(dataSize & 0xFF);
            outputBuffer[9] = (byte)((dataSize >> 8) & 0xFF);
            outputBuffer[10] = (byte)((dataSize >> 16) & 0xFF);
            outputBuffer[11] = (byte)((dataSize >> 24) & 0xFF);
            
            output.Write(outputBuffer, 0, outputPos);
        }
    }
}