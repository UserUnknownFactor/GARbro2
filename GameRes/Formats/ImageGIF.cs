using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace GameRes
{
    [Export(typeof(ImageFormat))]
    public class GifFormat : ImageFormat
    {
        public override string         Tag { get { return "GIF"; } }
        public override string Description { get { return "Graphics Interchange Format"; } }
        public override uint     Signature { get { return  0x38464947; } } // "GIF8"
        public override bool      CanWrite { get { return  true; } }

        public static readonly byte[] HeaderBytes87a = { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 }; // "GIF87a"
        public static readonly byte[] HeaderBytes89a = { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }; // "GIF89a"

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            MemoryStream memStream = new MemoryStream();
            file.Position = 0;
            file.AsStream.CopyTo (memStream);
            memStream.Position = 0;

            var decoder = new GifBitmapDecoder (memStream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count <= 1)
            {
                BitmapSource frame = decoder.Frames[0];
                frame.Freeze();
                return new ImageData (frame, info);
            }
            else
            {
                var compositeFrames = CompositeGifFrames (decoder, memStream);
                var frameDelays = ExtractFrameDelays (memStream);

                if (frameDelays.Count != compositeFrames.Count)
                    frameDelays = Enumerable.Repeat (100, compositeFrames.Count).ToList();

                return new AnimatedImageData (compositeFrames, frameDelays, info);
            }
        }

        private List<BitmapSource> CompositeGifFrames (GifBitmapDecoder decoder, Stream gifStream)
        {
            var compositeFrames = new List<BitmapSource>();

            // Get GIF logical screen size and background color
            var logicalScreen = GetLogicalScreenInfo (gifStream);
            int canvasWidth = logicalScreen.Width;
            int canvasHeight = logicalScreen.Height;
            var backgroundColor = logicalScreen.BackgroundColor;

            // Create a canvas to composite frames
            var canvasBuffer = new byte[canvasWidth * canvasHeight * 4]; // BGRA

            var savedStates = new Dictionary<int, byte[]>();

            // Fill with background color
            FillBackground (canvasBuffer, canvasWidth, canvasHeight, backgroundColor);

            var frameInfo = ExtractFrameInfo (gifStream);

            for (int i = 0; i < frameInfo.Count - 1; i++)
            {
                if (frameInfo[i].DisposalMethod == 3)
                    savedStates[i] = null; // Mark for saving
            }

            for (int i = 0; i < decoder.Frames.Count; i++)
            {
                var frame = decoder.Frames[i];
                var info = i < frameInfo.Count ? frameInfo[i] : new GifFrameInfo();

                // Handle disposal of previous frame
                if (i > 0)
                {
                    var prevInfo = frameInfo[i - 1];
                    HandleDisposal (canvasBuffer, savedStates, i - 1, canvasWidth, canvasHeight, 
                        prevInfo, backgroundColor);
                }

                // Save canvas state if this frame uses disposal method 3
                if (savedStates.ContainsKey (i))
                {
                    savedStates[i] = new byte[canvasBuffer.Length];
                    Array.Copy (canvasBuffer, savedStates[i], canvasBuffer.Length);
                }

                CompositeFrame (canvasBuffer, canvasWidth, canvasHeight, frame, info);

                var frameBuffer = new byte[canvasBuffer.Length];
                Array.Copy (canvasBuffer, frameBuffer, canvasBuffer.Length);

                var compositeBitmap = BitmapSource.Create (canvasWidth, canvasHeight, 96, 96,
                    PixelFormats.Bgra32, null, frameBuffer, canvasWidth * 4);
                compositeBitmap.Freeze();
                compositeFrames.Add (compositeBitmap);
            }

            return compositeFrames;
        }

        private void HandleDisposal (byte[] canvas, Dictionary<int, byte[]> savedStates, 
            int frameIndex, int width, int height, GifFrameInfo frameInfo, Color backgroundColor)
        {
            switch (frameInfo.DisposalMethod)
            {
            case 0: // No disposal specified
            case 1: // Do not dispose
                break;

            case 2: // Restore to background color
                ClearFrameArea (canvas, width, frameInfo, backgroundColor);
                break;

            case 3: // Restore to previous
                if (savedStates.TryGetValue (frameIndex, out byte[] previousState) && previousState != null)
                    Array.Copy (previousState, canvas, canvas.Length);
                else
                    ClearFrameArea (canvas, width, frameInfo, backgroundColor);
                break;
            }
        }

        private class LogicalScreenInfo
        {
            public int             Width { get; set; }
            public int            Height { get; set; }
            public Color BackgroundColor { get; set; }
        }

        private class GifFrameInfo
        {
            public int                   Left { get; set; }
            public int                    Top { get; set; }
            public int                  Width { get; set; }
            public int                 Height { get; set; }
            public byte        DisposalMethod { get; set; }
            public bool       HasTransparency { get; set; }
            public byte TransparentColorIndex { get; set; }
        }

        private LogicalScreenInfo GetLogicalScreenInfo (Stream gifStream)
        {
            gifStream.Position = 6; // Skip "GIF89a"

            var buffer  = new byte[7];
            gifStream.Read (buffer, 0, 7);

            int width   = buffer[0] | (buffer[1] << 8);
            int height  = buffer[2] | (buffer[3] << 8);
            byte packed = buffer[4];
            byte backgroundColorIndex = buffer[5];

            var backgroundColor = Colors.White; // Default

            // If there's a global color table, read the background color
            if ((packed & 0x80) != 0)
            {
                int colorTableSize = 3 * (1 << ((packed & 0x07) + 1));
                if (backgroundColorIndex * 3 < colorTableSize)
                {
                    var colorTable = new byte[colorTableSize];
                    gifStream.Read (colorTable, 0, colorTableSize);

                    if (backgroundColorIndex * 3 + 2 < colorTable.Length)
                    {
                        backgroundColor = Color.FromRgb(
                            colorTable[backgroundColorIndex * 3    ],
                            colorTable[backgroundColorIndex * 3 + 1],
                            colorTable[backgroundColorIndex * 3 + 2]);
                    }
                }
            }

            return new LogicalScreenInfo
            {
                Width = width,
                Height = height,
                BackgroundColor = backgroundColor
            };
        }

        private List<GifFrameInfo> ExtractFrameInfo (Stream gifStream)
        {
            var frames = new List<GifFrameInfo>();
            gifStream.Position = 6;

            // Skip logical screen descriptor
            var buffer = new byte[7];
            gifStream.Read (buffer, 0, 7);
            byte packed = buffer[4];

            // Skip global color table
            if ((packed & 0x80) != 0)
            {
                int globalColorTableSize = 3 * (1 << ((packed & 0x07) + 1));
                gifStream.Seek (globalColorTableSize, SeekOrigin.Current);
            }

            var currentFrame = new GifFrameInfo();

            while (gifStream.Position < gifStream.Length)
            {
                int b = gifStream.ReadByte();
                if (b == -1) break;

                if (b == 0x21) // Extension
                {
                    int label = gifStream.ReadByte();
                    if (label == 0xF9) // Graphic Control Extension
                    {
                        int blockSize = gifStream.ReadByte();
                        if (blockSize >= 4)
                        {
                            byte flags = (byte)gifStream.ReadByte();
                            currentFrame.DisposalMethod = (byte)((flags >> 2) & 0x07);
                            currentFrame.HasTransparency = (flags & 0x01) != 0;

                            gifStream.ReadByte(); // Delay low byte
                            gifStream.ReadByte(); // Delay high byte
                            currentFrame.TransparentColorIndex = (byte)gifStream.ReadByte();
                        }
                        gifStream.ReadByte(); // Block terminator
                    }
                    else
                    {
                        // Skip other extensions
                        SkipDataSubBlocks (gifStream);
                    }
                }
                else if (b == 0x2C) // Image
                {
                    var imageInfo = new byte[9];
                    gifStream.Read (imageInfo, 0, 9);

                    currentFrame.Left   = imageInfo[0] | (imageInfo[1] << 8);
                    currentFrame.Top    = imageInfo[2] | (imageInfo[3] << 8);
                    currentFrame.Width  = imageInfo[4] | (imageInfo[5] << 8);
                    currentFrame.Height = imageInfo[6] | (imageInfo[7] << 8);

                    frames.Add (currentFrame);
                    currentFrame = new GifFrameInfo();

                    // Skip local color table and image data
                    byte imageFlags = imageInfo[8];
                    if ((imageFlags & 0x80) != 0)
                    {
                        int localColorTableSize = 3 * (1 << ((imageFlags & 0x07) + 1));
                        gifStream.Seek (localColorTableSize, SeekOrigin.Current);
                    }

                    gifStream.ReadByte(); // LZW minimum code size
                    SkipDataSubBlocks (gifStream);
                }
                else if (b == 0x3B) // Trailer
                    break;
            }

            return frames;
        }

        private void FillBackground (byte[] buffer, int width, int height, Color color)
        {
            for (int i = 0; i < buffer.Length; i += 4)
            {
                buffer[i    ] = color.B;  // Blue
                buffer[i + 1] = color.G;  // Green  
                buffer[i + 2] = color.R;  // Red
                buffer[i + 3] = 255;      // Alpha
            }
        }

        private void HandleDisposal (byte[] canvas, int width, int height, GifFrameInfo frameInfo, Color backgroundColor)
        {
            switch (frameInfo.DisposalMethod)
            {
                case 0: // No disposal specified
                case 1: // Do not dispose
                        // Leave canvas as is
                    break;

                case 2: // Restore to background color
                    ClearFrameArea (canvas, width, frameInfo, backgroundColor);
                    break;

                case 3: // Restore to previous
                        // This would require keeping a copy of the previous state
                        // For simplicity, treat as restore to background
                    ClearFrameArea (canvas, width, frameInfo, backgroundColor);
                    break;
            }
        }

        private void ClearFrameArea (byte[] canvas, int canvasWidth, GifFrameInfo frameInfo, Color backgroundColor)
        {
            for (int y = frameInfo.Top; y < frameInfo.Top + frameInfo.Height; y++)
            {
                for (int x = frameInfo.Left; x < frameInfo.Left + frameInfo.Width; x++)
                {
                    if (x >= 0 && x < canvasWidth && y >= 0 && y < canvas.Length / (canvasWidth * 4))
                    {
                        int index = (y * canvasWidth + x) * 4;
                        canvas[index    ] = backgroundColor.B;
                        canvas[index + 1] = backgroundColor.G;
                        canvas[index + 2] = backgroundColor.R;
                        canvas[index + 3] = 255;
                    }
                }
            }
        }

        private void CompositeFrame (byte[] canvas, int canvasWidth, int canvasHeight, BitmapSource frame, GifFrameInfo frameInfo)
        {
            // Convert frame to BGRA format if needed
            BitmapSource convertedFrame = frame;
            if (frame.Format != PixelFormats.Bgra32)
                convertedFrame = new FormatConvertedBitmap (frame, PixelFormats.Bgra32, null, 0);

            int stride = convertedFrame.PixelWidth * 4;
            var frameBuffer = new byte[stride * convertedFrame.PixelHeight];
            convertedFrame.CopyPixels (frameBuffer, stride, 0);

            // Composite frame onto canvas at the specified position
            for (int y = 0; y < frameInfo.Height; y++)
            {
                for (int x = 0; x < frameInfo.Width; x++)
                {
                    int canvasX = frameInfo.Left + x;
                    int canvasY = frameInfo.Top + y;

                    if (canvasX >= 0 && canvasX < canvasWidth && canvasY >= 0 && canvasY < canvasHeight)
                    {
                        int frameIndex = (y * frameInfo.Width + x) * 4;
                        int canvasIndex = (canvasY * canvasWidth + canvasX) * 4;

                        if (frameIndex + 3 < frameBuffer.Length && canvasIndex + 3 < canvas.Length)
                        {
                            // Check for transparency
                            if (!frameInfo.HasTransparency || frameBuffer[frameIndex + 3] > 0)
                            {
                                canvas[canvasIndex    ] = frameBuffer[frameIndex    ];         // Blue
                                canvas[canvasIndex + 1] = frameBuffer[frameIndex + 1]; // Green
                                canvas[canvasIndex + 2] = frameBuffer[frameIndex + 2]; // Red
                                canvas[canvasIndex + 3] = frameBuffer[frameIndex + 3]; // Alpha
                            }
                        }
                    }
                }
            }
        }

        private List<int> ExtractFrameDelays (Stream gifStream)
        {
            var delays = new List<int>();
            try
            {
                gifStream.Position = 0;

                // Skip header
                gifStream.Seek (6, SeekOrigin.Current);

                // Skip Logical Screen Descriptor
                var buffer = new byte[7];
                gifStream.Read (buffer, 0, 7);
                byte packed = buffer[4];

                // Skip global color table if present
                if ((packed & 0x80) != 0)
                {
                    int globalColorTableSize = 3 * (1 << ((packed & 0x07) + 1));
                    gifStream.Seek (globalColorTableSize, SeekOrigin.Current);
                }

                // Scan for Graphics Control Extension blocks to get frame delays
                while (gifStream.Position < gifStream.Length - 1)
                {
                    byte introducer = (byte)gifStream.ReadByte();

                    if (introducer == 0x21) // Extension Introducer
                    {
                        byte extensionLabel = (byte)gifStream.ReadByte();

                        if (extensionLabel == 0xF9) // Graphics Control Extension
                        {
                            byte blockSize = (byte)gifStream.ReadByte();
                            if (blockSize == 4) // Should be 4 bytes
                            {
                                byte flags = (byte)gifStream.ReadByte();

                                // Read delay time (in 1/100 seconds)
                                int delayTime = gifStream.ReadByte() | (gifStream.ReadByte() << 8);

                                // Convert to milliseconds (minimum 10ms as per spec)
                                delays.Add (delayTime == 0 ? 100 : delayTime * 10);

                                // Skip transparent color index
                                gifStream.Seek (1, SeekOrigin.Current);

                                // Skip block terminator
                                gifStream.Seek (1, SeekOrigin.Current);
                            }
                            else
                            {
                                // Skip incorrect block
                                gifStream.Seek (blockSize + 1, SeekOrigin.Current);
                            }
                        }
                        else
                        {
                            // Skip other extension blocks
                            SkipDataSubBlocks (gifStream);
                        }
                    }
                    else if (introducer == 0x2C) // Image Descriptor
                    {
                        // Skip image descriptor
                        gifStream.Seek (9, SeekOrigin.Current);

                        // Check for local color table
                        byte imageFlags = (byte)gifStream.ReadByte();
                        bool hasLocalColorTable = (imageFlags & 0x80) != 0;

                        if (hasLocalColorTable)
                        {
                            int localColorTableSize = 3 * (1 << ((imageFlags & 0x07) + 1));
                            gifStream.Seek (localColorTableSize, SeekOrigin.Current);
                        }

                        // Skip LZW minimum code size
                        gifStream.Seek (1, SeekOrigin.Current);

                        // Skip image data blocks
                        SkipDataSubBlocks (gifStream);
                    }
                    else if (introducer == 0x3B) // Trailer
                        break;
                }
            }
            catch
            {
                delays.Clear();
            }

            return delays;
        }

        private void SkipDataSubBlocks (Stream stream)
        {
            while (true)
            {
                int blockSize = stream.ReadByte();
                if (blockSize <= 0)
                    break;

                stream.Seek (blockSize, SeekOrigin.Current);
            }
        }

        private void SkipDataSubBlocks (IBinaryStream file)
        {
            while (true)
            {
                int blockSize = file.ReadByte();
                if (blockSize <= 0)
                    break;

                file.Seek (blockSize, SeekOrigin.Current);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            var encoder = new GifBitmapEncoder();

            if (image is AnimatedImageData animatedImage && animatedImage.IsAnimated)
            {
                foreach (var frame in animatedImage.Frames)
                    encoder.Frames.Add (BitmapFrame.Create (frame));
            }
            else
            {
                encoder.Frames.Add (BitmapFrame.Create (image.Bitmap));
            }

            encoder.Save (file);
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var startPos = file.Position;

            var header = file.ReadBytes (6);
            if (header.Length < 6)
                return null;

            // Check if it's GIF87a or GIF89a
            if (!((header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46) &&
                  (header[3] == 0x38) &&
                  ((header[4] == 0x37 && header[5] == 0x61) ||
                   (header[4] == 0x39 && header[5] == 0x61))))
                return null;

            // Read Logical Screen Descriptor
            var meta    = new AnimationMetaData();
            meta.Width  = file.ReadUInt16();
            meta.Height = file.ReadUInt16();

            byte packed = file.ReadUInt8();
            bool hasGlobalColorTable = (packed & 0x80) != 0;
            int globalColorTableSize = 2 << (packed & 0x07);

            file.ReadByte(); // Background color index
            file.ReadByte(); // Pixel aspect ratio

            // Skip global color table if present
            if (hasGlobalColorTable)
                file.Seek (globalColorTableSize * 3, SeekOrigin.Current); // RGB triplets

            meta.FrameCount = CountFrames (file);
            meta.BPP = 8;

            file.Position = startPos;

            return meta;
        }

        private int CountFrames (IBinaryStream file)
        {
            int frameCount = 0;
            long savedPosition = file.Position;

            try
            {
                while (file.Position < file.Length)
                {
                    int b = file.ReadByte();
                    if (b == -1) break;

                    byte introducer = (byte)b;

                    if (introducer == 0x21) // Extension
                    {
                        byte label = file.ReadUInt8(); // Extension label

                        // Skip all sub-blocks
                        while (true)
                        {
                            int blockSize = file.ReadByte();
                            if (blockSize <= 0) break;
                            file.Seek (blockSize, SeekOrigin.Current);
                        }
                    }
                    else if (introducer == 0x2C) // Image separator
                    {
                        frameCount++;

                        // Skip image header (9 bytes: 4 position + 4 size + 1 flags)
                        file.Seek (8, SeekOrigin.Current); // Skip position and size

                        byte flags = file.ReadUInt8();
                        if ((flags & 0x80) != 0) // Local color table
                        {
                            int colorCount = 2 << (flags & 0x07);
                            file.Seek (colorCount * 3, SeekOrigin.Current);
                        }

                        file.ReadByte(); // LZW minimum code size

                        // Skip image data sub-blocks
                        while (true)
                        {
                            int blockSize = file.ReadByte();
                            if (blockSize <= 0) break;
                            file.Seek (blockSize, SeekOrigin.Current);
                        }
                    }
                    else if (introducer == 0x3B) // Trailer
                    {
                        break;
                    }
                    else if (introducer == 0x00) // Possible padding
                    {
                        continue;
                    }
                    else
                    {
                        // Unknown block - might be corrupted
                        break;
                    }
                }
            }
            catch
            {
                frameCount = Math.Max (frameCount, 1);
            }
            finally
            {
                // Restore file position
                file.Position = savedPosition;
            }

            return Math.Max (frameCount, 1);
        }
    }
}