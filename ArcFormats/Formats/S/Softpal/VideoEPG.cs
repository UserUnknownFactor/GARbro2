using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Softpal
{
    internal class EpegFrameEntry
    {
        public uint Flags;
        public uint Offset;

        public EpegFrameEntry (IBinaryStream file)
        {
            Offset = file.ReadUInt32();
            Flags  = file.ReadUInt32();
        }
    }

    [Export(typeof(ImageFormat))]
    public class EpegFormat : ImageFormat
    {
        public override string         Tag { get { return "EPEG"; } }
        public override string Description { get { return "Softpal animated image format"; } }
        public override uint     Signature { get { return  0x47455045; } } // 'EPEG'

        public EpegFormat()
        {
            Extensions = new string[] { "epg" };
        }

        internal class EpegMetaData : AnimationMetaData
        {
            public int KeyframeInterval;
            public bool HasAudio;
            //public uint KeyFrameNum;
            public float FPS;
            public List<EpegFrameEntry> FrameIndex;
            public long FrameDataStart;
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x30);
            if (!header.AsciiEqual ("EPEG"))
                return null;

            var meta = new EpegMetaData
            {
                Width            =  header.ToUInt16 (0x0C),
                Height           =  header.ToUInt16 (0x0E),
                KeyframeInterval =  header.ToInt16  (0x10),
                FPS              =  header.ToInt32  (0x14) / 1000.0f,
                FrameCount       =  header.ToInt32  (0x1C),
                //KeyFrameNum      =  header.ToUInt32 (0x20),
                HasAudio         = (header.ToUInt32 (0x2C) & 2) != 0,
                FrameIndex            = new List<EpegFrameEntry>(),
                BPP              = 24
            };

            file.Position = 0x24;
            for (int i = 0; i < meta.FrameCount + 2; i++)
                meta.FrameIndex.Add (new EpegFrameEntry (file));

            meta.FrameDataStart = file.Position;

            return meta;
        }


        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (EpegMetaData)info;
            return ReadAnimation (file, meta);
        }

        private static (int actualHeight, int cropTop) DetectPaddingFromDimensions (uint width, uint height)
        {
            int[] commonHeights = { 
                240, 300, 360, 480, 600, 720, 768, 800, 900, 960, 1024, 1050, 1080, 1200, 1440, 1600 
            };

            foreach (int commonHeight in commonHeights)
            {
                int diff = (int)height - commonHeight;
                if (diff > 0 && diff <= 8)
                {
                    // Check if the height is a multiple of macroblock alignment
                    if (height % 8 == 0)
                    {
                        int cropTop = diff / 2;
                        return (commonHeight, cropTop);
                    }
                }
            }
            // No padding
            return ((int)height, 0);
        }

        private AnimatedImageData ReadAnimation (IBinaryStream file, EpegMetaData meta)
        {
            var (actualHeight, cropTop) = DetectPaddingFromDimensions (meta.Width, meta.Height);

            var frames = new List<BitmapSource>();
            var delays = new List<int>();
            byte[] referenceFrame = null;

            int uvySize = (int)(meta.Width * meta.Height * 3 / 2);
            int mvSize = ((int)meta.Width / 4) * ((int)meta.Height / 8);

            file.Position = meta.FrameDataStart;

            for (int i = 0; i < meta.FrameIndex.Count - 1; i++)
            {
                if (meta.FrameIndex[i].Flags == 0xFFFFFFFF)
                    break;

                file.Position = meta.FrameIndex[i].Offset;

                var uncompressedSize = file.ReadInt32();
                var compressedSize = file.ReadInt32();
                var compressedUVY = file.ReadBytes (compressedSize);

                var buffer = new byte[uncompressedSize];
                LzDecompress (buffer, uncompressedSize, compressedUVY);

                byte[] outputFrame = null;

                bool isKeyframe = (i % meta.KeyframeInterval == 0);
                if (isKeyframe || referenceFrame == null)
                {
                    // Keyframe
                    referenceFrame = new byte[uvySize];
                    Array.Copy (buffer, referenceFrame, Math.Min (uvySize, buffer.Length));
                    outputFrame = referenceFrame;
                }
                else
                {
                    // Motion-compensated frame
                    if (uncompressedSize < uvySize + mvSize)
                    {
                        // Plain XOR differential frame
                        outputFrame = new byte[uvySize];
                        Buffer.BlockCopy (referenceFrame, 0, outputFrame, 0, uvySize);

                        for (int j=0; j < uvySize; j++)
                            outputFrame[j] ^= buffer[j];
                    }
                    else if (uncompressedSize >= uvySize + mvSize)
                    {
                        // Motion‑compensated differential frame
                        outputFrame = new byte[uvySize];
                        Array.Copy (referenceFrame, outputFrame, uvySize);

                        ApplyDifferential (referenceFrame, buffer, outputFrame, (int)meta.Width, (int)meta.Height);
                    }
                }

                if (outputFrame != null) 
                {
                    var rgb = ConvertToRgb (outputFrame, meta.Width, meta.Height);
                    var bmp = BitmapSource.Create(
                        (int)meta.Width, (int)meta.Height,
                        ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                        PixelFormats.Bgr24, null, rgb, (int)meta.Width * 3);

                    if (cropTop > 0)
                    {
                        // Or we'd have black lines since the format seems aligned:
                        //  (400 ×) 304 =   38 × 8 (perfect 8px macroblock alignment)
                        //  (400 ×) 300 = 37.5 × 8 (unaligned but standard 4:3 ratio)
                        var croppedBmp = new CroppedBitmap(bmp, 
                            new System.Windows.Int32Rect(0, cropTop, (int)meta.Width, actualHeight));
                        croppedBmp.Freeze();
                        frames.Add(croppedBmp);
                    }
                    else
                    {
                        bmp.Freeze();
                        frames.Add(bmp);
                    }
                    
                    delays.Add((int)(1000.0f / meta.FPS));
                }
            }

            return new AnimatedImageData (frames, delays, meta);
        }

        private static void ApplyDifferential(
            byte[] prev, byte[] diffBuffer, byte[] output,
            int width, int height)
        {
            int uvWidth   = width  / 2;
            int uvHeight  = height / 2;
            int uvSize    = uvWidth * uvHeight;
            int ySize     = width * height;
            int uvySize   = uvSize * 2 + ySize;

            int mbWidth   = width  / 8;
            int mbHeight  = height / 8;
            int numBlocks = mbWidth * mbHeight;
            int mvSize    = numBlocks * 2;

            int uOffset   = 0;
            int vOffset   = uvSize;
            int yOffset   = uvSize * 2;

            Buffer.BlockCopy (prev, 0, output, 0, uvySize);

            // U/V differentials
            int diffOffset = mvSize;
            for (int i = 0; i < uvSize; i++) {
                output[uOffset + i] ^= diffBuffer[diffOffset + i];
                output[vOffset + i] ^= diffBuffer[diffOffset + uvSize + i];
            }

            // Process Y with motion
            int yDiffOffset = diffOffset + uvSize * 2;
            int mvIndex = 0;

            for (int mbY = 0; mbY < mbHeight; mbY++)
            for (int mbX = 0; mbX < mbWidth; mbX++) {
                sbyte mvx = (sbyte)diffBuffer[mvIndex++];
                sbyte mvy = (sbyte)diffBuffer[mvIndex++];

                for (int yy = 0; yy < 8; yy++) {
                    int dstY = mbY * 8 + yy;
                    if (dstY >= height) break;

                    for (int xx = 0; xx < 8; xx++) {
                        int dstX = mbX * 8 + xx;
                        if (dstX >= width) break;

                        int dstIdx = yOffset + dstY * width + dstX;

                        // Get the Y differential value
                        int linearIdx = dstY * width + dstX;
                        byte diffVal = 0;
                        if (yDiffOffset + linearIdx < diffBuffer.Length) {
                            diffVal = diffBuffer[yDiffOffset + linearIdx];
                        }

                        int srcY = dstY + mvy;
                        int srcX = dstX + mvx;
                        if (srcY < 0) srcY = 0;
                        else if (srcY >= height) srcY = height - 1;
                        if (srcX < 0) srcX = 0;
                        else if (srcX >= width) srcX = width - 1;

                        int srcIdx = yOffset + srcY * width + srcX;
                        byte prediction = (mvx == 0 && mvy == 0) ? 
                            prev[dstIdx] : prev[srcIdx];
                        output[dstIdx] = (byte)(prediction ^ diffVal);
                    }
                }
            }
        }

        public static int LzDecompress (byte[] output, int outputSize, byte[] input)//, out int bytesConsumed)
        {
            int dst = 0;
            int src = 0;
            //bytesConsumed = 0;

            if (input.Length == 0) return 0;

            byte flags = input[src++];
            int flagBitsUsed = 0;

            while (dst < outputSize && src < input.Length)
            {
                if ((flags & 1) == 0)
                {
                    // Literal copy
                    if (src >= input.Length) break;

                    byte ctrl = input[src++];
                    int dwords = (ctrl & 0xFC) >> 2;
                    int remainder = ctrl & 0x03;
                    int totalBytes = (dwords * 4) + remainder;

                    if (src + totalBytes > input.Length) break;
                    if (dst + totalBytes > outputSize) break;

                    Buffer.BlockCopy (input, src, output, dst, totalBytes);
                    src += totalBytes;
                    dst += totalBytes;
                }
                else
                {
                    // Back reference
                    if (src + 1 >= input.Length) break;

                    ushort word = (ushort)(input[src] | (input[src + 1] << 8));

                    if ((word & 0x0008) != 0)
                    {
                        // Short form (2 bytes)
                        src += 2;

                        int distance = word >> 4;
                        int length = (word & 0x07) + 4;

                        if (dst - distance < 0) break;

                        for (int i = 0; i < length && dst < outputSize; i++)
                        {
                            output[dst] = output[dst - distance];
                            dst++;
                        }
                    }
                    else
                    {
                        // Long form (3 bytes)
                        if (src + 2 >= input.Length) break;

                        uint val = (uint)((word << 8) | input[src + 2]);
                        src += 3;

                        int distance = (int)(val >> 12);
                        int lengthField = (int)(val & 0xFFF);
                        int dwords = ((lengthField & 0xFFC) >> 2) + 1;
                        int remainder = lengthField & 0x03;
                        int totalBytes = (dwords * 4) + remainder;

                        if (dst - distance < 0) break;

                        for (int i = 0; i < totalBytes && dst < outputSize; i++)
                        {
                            output[dst] = output[dst - distance];
                            dst++;
                        }
                    }
                }

                flags >>= 1;
                flagBitsUsed++;

                if (flagBitsUsed == 8 && src < input.Length)
                {
                    flags = input[src++];
                    flagBitsUsed = 0;
                }
            }

            //bytesConsumed = src;
            return dst;
        }

        private static byte[] ConvertToRgb (byte[] data, uint width, uint height)
        {
            if (data == null || width == 0 || height == 0)
                return new byte[0];

            var rgb = new byte[width * height * 3];

            int segment_size = (int)(width * height / 4);
            int uOffset = 0;
            int vOffset = segment_size;
            int yOffset = segment_size * 2;

            // same logic as PGD but refactored
            for (int blockY = 0; blockY < height / 2; blockY++)
            for (int blockX = 0; blockX < width  / 2; blockX++)
            {
                sbyte U = (sbyte)data[uOffset++];
                sbyte V = (sbyte)data[vOffset++];

                int b = 226 * U;
                int g = -43 * U - 89 * V;
                int r = 179 * V;

                // Get the 4 Y values for this 2x2 block
                // Y values are stored in raster order in the Y plane
                int y0 = (int)(yOffset + (blockY * 2) * width + (blockX * 2)); // top-left
                int y1 = y0 + 1;                                               // top-right
                int y2 = (int)(y0 + width);                                    // bottom-left
                int y3 = y2 + 1;                                               // bottom-right

                int[] yIndices = { y0, y1, y2, y3 };
                int[][] pixelPos = {
                    new[] {blockX * 2,     blockY * 2},      // top-left
                    new[] {blockX * 2 + 1, blockY * 2},      // top-right
                    new[] {blockX * 2,     blockY * 2 + 1},  // bottom-left
                    new[] {blockX * 2 + 1, blockY * 2 + 1}   // bottom-right
                };

                for (int i = 0; i < 4; i++)
                {
                    if (yIndices[i] >= data.Length) continue;

                    int y_value = data[yIndices[i]] << 7;
                    int rgbIndex = (int)((pixelPos[i][1] * width + pixelPos[i][0]) * 3);

                    int B = (y_value + b) >> 7;
                    int G = (y_value + g) >> 7;
                    int R = (y_value + r) >> 7;

                    // Clamp
                    if (B > 255) B = 255; else if (B < 0) B = 0;
                    if (G > 255) G = 255; else if (G < 0) G = 0;
                    if (R > 255) R = 255; else if (R < 0) R = 0;

                    rgb[rgbIndex    ] = (byte)B;
                    rgb[rgbIndex + 1] = (byte)G;
                    rgb[rgbIndex + 2] = (byte)R;
                }
            }

            return rgb;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("EPEG writing not supported");
        }
    }
}