using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes
{
    public class PngMetaData : ImageMetaData
    {
        public int  FrameCount { get; set; } = 1;
        public int   PlayCount { get; set; } = 0; // 0 = infinite
        public bool IsAnimated { get { return FrameCount > 1; } }

        public Dictionary<string, byte[]> CustomChunks { get; set; } = new Dictionary<string, byte[]>();

        public override string GetComment()
        {
            var n_frames = IsAnimated ? FrameCount.Pluralize ("n_frames") : "";
            return Localization.Format ("MsgImageSize", Width, Height, BPP, n_frames);
        }
    }

    public class PngImageData : ImageData
    {
        public Dictionary<string, byte[]> CustomChunks { get; set; } = new Dictionary<string, byte[]>();

        public PngImageData (BitmapSource bitmap, ImageMetaData info) : base (bitmap, info) { }
    }

    [Export (typeof (ImageFormat))]
    [ExportMetadata ("Priority", 100)] // makes PNG first format in list
    public class PngFormat : ImageFormat
    {
        public override string         Tag { get { return "PNG"; } }
        public override string Description { get { return "Portable Network Graphics image"; } }
        public override uint     Signature { get { return  0x474e5089; } }
        public override bool      CanWrite { get { return  true; } }

        // PNG signature and header/footer constants
        public static readonly byte[] PNG_HEADER  = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // it is interesting why are these bytes like this
        public static readonly byte[] PNG_FOOTER  = { 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 }; // length + IEND + CRC
        public const uint PNG_SIGNATURE = 0x474e5089;

        // Chunk type constants
        public const string CHUNK_IHDR = "IHDR";
        public const string CHUNK_IDAT = "IDAT";
        public const string CHUNK_IEND = "IEND";
        public const string CHUNK_ACTL = "acTL";
        public const string CHUNK_FCTL = "fcTL";
        public const string CHUNK_FDAT = "fdAT";
        public const string CHUNK_OFFS = "oFFs";
        public const string CHUNK_PHYS = "pHYs";

        // APNG disposal operation constants
        public const byte APNG_DISPOSE_OP_NONE       = 0;
        public const byte APNG_DISPOSE_OP_BACKGROUND = 1;
        public const byte APNG_DISPOSE_OP_PREVIOUS   = 2;

        // APNG blend operation constants
        public const byte APNG_BLEND_OP_SOURCE = 0;
        public const byte APNG_BLEND_OP_OVER   = 1;

        // PNG color type constants
        public const byte PNG_COLOR_TYPE_GRAYSCALE       = 0;
        public const byte PNG_COLOR_TYPE_RGB             = 2;
        public const byte PNG_COLOR_TYPE_PALETTE         = 3;
        public const byte PNG_COLOR_TYPE_GRAYSCALE_ALPHA = 4;
        public const byte PNG_COLOR_TYPE_RGBA            = 6;

        // Units constants
        public const byte PNG_UNIT_PIXELS = 0;

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var pngInfo = info as PngMetaData;
            try
            {
                if (file.CanSeek)
                    file.Position = 0;
                if (pngInfo == null || !pngInfo.IsAnimated)
                    return ReadPNG (file, info);
                else
                    return ReadAPNG (file, info);
            }
            catch
            {
                System.Diagnostics.Trace.WriteLine ($"Failed to load {file.Name} as APNG, using ImageMagick fallback...");
                return FormatCatalog.Instance.GetImageFormatByTag ("IMGMAGICK")?.Read (file, info);
            }
        }

        private static ImageData ReadPNG (IBinaryStream file, ImageMetaData info)
        {
            var decoder = new PngBitmapDecoder (file.AsStream,
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            frame.Freeze();

            if (info is PngMetaData pngMeta && pngMeta.CustomChunks.Count > 0)
            {
                var pngData = new PngImageData (frame, info);
                pngData.CustomChunks = pngMeta.CustomChunks;
                return pngData;
            }

            return new ImageData (frame, info);
        }

        public Dictionary<string, byte[]> ReadCustomChunks (IBinaryStream file, params string[] chunkTypes)
        {
            var result = new Dictionary<string, byte[]>();

            if (!file.CanSeek)
                return result;

            var savedPosition = file.Position;
            try
            {
                file.Position = 8; // Skip PNG header

                while (file.Position < file.Length)
                {
                    uint chunkLength = file.ReadUInt32BE();
                    byte[] typeBytes = file.ReadBytes (4);
                    string chunkType = Encoding.ASCII.GetString (typeBytes);

                    if (chunkTypes.Length == 0 || chunkTypes.Contains (chunkType))
                    {
                        // Check if it's a custom chunk (lowercase letters indicate ancillary chunks)
                        if (!IsStandardChunk (chunkType))
                        {
                            byte[] data = file.ReadBytes((int)chunkLength);
                            result[chunkType] = data;
                            file.ReadUInt32(); // Skip CRC
                        }
                        else
                            file.Seek (chunkLength + 4, SeekOrigin.Current); // Skip data + CRC
                    }
                    else
                        file.Seek (chunkLength + 4, SeekOrigin.Current); // Skip data + CRC

                    if (chunkType == CHUNK_IEND)
                        break;
                }
            }
            catch { }
            finally
            {
                file.Position = savedPosition;
            }

            return result;
        }

        private bool IsStandardChunk (string chunkType)
        {
            // List of known chunks
            return chunkType == CHUNK_IHDR || chunkType == CHUNK_IDAT || chunkType == CHUNK_IEND ||
                   chunkType == CHUNK_ACTL || chunkType == CHUNK_FCTL || chunkType == CHUNK_FDAT ||
                   chunkType == CHUNK_OFFS || chunkType == "PLTE" || chunkType == "tRNS" ||
                   chunkType == "gAMA" || chunkType == "cHRM" || chunkType == "sRGB" ||
                   chunkType == "iCCP" || chunkType == "tEXt" || chunkType == "zTXt" ||
                   chunkType == "iTXt" || chunkType == "bKGD" || chunkType == CHUNK_PHYS ||
                   chunkType == "sBIT" || chunkType == "sPLT" || chunkType == "hIST" ||
                   chunkType == "tIME";
        }

        private ImageData ReadAPNG (IBinaryStream file, ImageMetaData info)
        {
            var pngInfo = info as PngMetaData ?? new PngMetaData
            {
                Width  = info.Width,
                Height = info.Height,
                BPP    = info.BPP
            };

            var frames   = new List<BitmapSource>();
            var delays   = new List<int>();
            var apngData = ParseApng (file);

            if (apngData.Frames.Count <= 1)
                return ReadPNG (file, info); // shouldn't happen but just in case

            // Composite frames
            var canvasWidth    = (int)pngInfo.Width;
            var canvasHeight   = (int)pngInfo.Height;
            var canvas         = new byte[canvasWidth * canvasHeight * 4]; // BGRA32
            var previousCanvas = new byte[canvas.Length];

            // Clear canvas to fully transparent black
            Array.Clear (canvas, 0, canvas.Length);

            for (int i = 0; i < apngData.Frames.Count; i++)
            {
                var frame = apngData.Frames[i];

                // Handle first frame special case
                if (i == 0)
                {
                    if (frame.DisposeOp == APNG_DISPOSE_OP_PREVIOUS)
                        frame.DisposeOp = APNG_DISPOSE_OP_BACKGROUND;

                    // Clear canvas for first frame
                    Array.Clear (canvas, 0, canvas.Length);
                }
                else
                {
                    // Apply disposal of previous frame
                    var prevFrame = apngData.Frames[i - 1];

                    switch (prevFrame.DisposeOp)
                    {
                        case APNG_DISPOSE_OP_NONE:
                            // No disposal - canvas stays as is
                            break;

                        case APNG_DISPOSE_OP_BACKGROUND:
                            // Clear the previous frame's region to transparent
                            for (int y = prevFrame.Y; y < Math.Min (prevFrame.Y + prevFrame.Height, canvasHeight); y++)
                            {
                                for (int x = prevFrame.X; x < Math.Min (prevFrame.X + prevFrame.Width, canvasWidth); x++)
                                {
                                    int idx = (y * canvasWidth + x) * 4;
                                    canvas[idx] = 0;     // B
                                    canvas[idx + 1] = 0; // G
                                    canvas[idx + 2] = 0; // R
                                    canvas[idx + 3] = 0; // A
                                }
                            }
                            break;

                        case APNG_DISPOSE_OP_PREVIOUS:
                            // Restore canvas to what it was before the previous frame
                            Array.Copy (previousCanvas, canvas, canvas.Length);
                            break;
                    }
                }

                // Save canvas state before compositing if the current frame requires it
                if (frame.DisposeOp == APNG_DISPOSE_OP_PREVIOUS)
                    Array.Copy (canvas, previousCanvas, canvas.Length);

                // Composite current frame onto canvas
                CompositeApngFrame (canvas, frame, canvasWidth, canvasHeight);

                // Create bitmap from current canvas state - ALWAYS USE CANVAS SIZE

                var bitmap = BitmapSource.Create(
                    canvasWidth,
                    canvasHeight,
                    apngData.DpiX, apngData.DpiY,
                    PixelFormats.Bgra32, 
                    null, 
                    canvas,
                    canvasWidth * 4);

                bitmap.Freeze();
                frames.Add (bitmap);

                // Convert delay - handle zero delay specially
                int delayMs;
                if (frame.DelayNum == 0)
                    delayMs = 10;
                else
                {
                    delayMs = frame.DelayDen > 0
                        ? (frame.DelayNum * 1000) / frame.DelayDen
                        : 100;
                }
                delays.Add (delayMs);
            }

            if (frames.Count > 1)
                return new AnimatedImageData (frames, delays, pngInfo);
            else if (frames.Count == 1)
                return new ImageData (frames[0], pngInfo);

            return null;
        }

        private class ApngFrame
        {
            public byte[] ImageData { get; set; }
            public            int X { get; set; }
            public            int Y { get; set; }
            public        int Width { get; set; }
            public       int Height { get; set; }
            public  ushort DelayNum { get; set; }
            public  ushort DelayDen { get; set; }
            public   byte DisposeOp { get; set; }
            public     byte BlendOp { get; set; }
        }

        private class ApngData
        {
            public List<ApngFrame> Frames { get; set; } = new List<ApngFrame>();
            public          int NumFrames { get; set; }
            public           int NumPlays { get; set; }
            public            double DpiX { get; set; } = 96.0;
            public            double DpiY { get; set; } = 96.0;
        }

        private ApngData ParseApng (IBinaryStream file)
        {
            var apng = new ApngData();
            var currentFrameChunks = new List<byte[]>();
            ApngFrame currentFrame = null;
            bool hasActl = false;
            bool isFirstFrame = true;

            // Store the main image dimensions from IHDR
            uint mainWidth = 0;
            uint mainHeight = 0;

            // Skip PNG header
            file.ReadBytes (8);

            try
            {
                while (file.Position < file.Length)
                {
                    uint chunkLength = file.ReadUInt32BE();
                    var chunkType = file.ReadBytes (4);
                    string chunkName = Encoding.ASCII.GetString (chunkType);

                    if (chunkLength > int.MaxValue)
                        break;

                    var chunkData = file.ReadBytes((int)chunkLength);
                    file.ReadUInt32(); // CRC

                    switch (chunkName)
                    {
                        case CHUNK_IHDR:
                            // Capture the main image dimensions
                            mainWidth = Binary.BigEndian (BitConverter.ToUInt32 (chunkData, 0));
                            mainHeight = Binary.BigEndian (BitConverter.ToUInt32 (chunkData, 4));
                            break;

                        case CHUNK_PHYS: // Physical pixel dimensions
                            if (chunkLength >= 9)
                            {
                                uint pixelsPerUnitX = Binary.BigEndian (BitConverter.ToUInt32 (chunkData, 0));
                                uint pixelsPerUnitY = Binary.BigEndian (BitConverter.ToUInt32 (chunkData, 4));
                                byte unit = chunkData[8];

                                if (unit == 1) // meter
                                {
                                    // Convert pixels per meter to DPI
                                    // 1 meter = 39.3701 inches
                                    apng.DpiX = pixelsPerUnitX / 39.3701;
                                    apng.DpiY = pixelsPerUnitY / 39.3701;
                                }
                            }
                            break;

                        case CHUNK_ACTL:
                            if (chunkLength >= 8)
                            {
                                apng.NumFrames = Binary.BigEndian (BitConverter.ToInt32 (chunkData, 0));
                                apng.NumPlays = Binary.BigEndian (BitConverter.ToInt32 (chunkData, 4));
                                hasActl = true;
                            }
                            break;

                        case CHUNK_FCTL:
                            // Process any pending frame
                            if (currentFrame != null && currentFrameChunks.Count > 0)
                            {
                                var frameData = CombineChunks (currentFrameChunks);

                                int decodeWidth  = currentFrame.Width;
                                int decodeHeight = currentFrame.Height;

                                if (isFirstFrame && apng.Frames.Count == 0)
                                {
                                    decodeWidth  = (int)mainWidth;
                                    decodeHeight = (int)mainHeight;
                                }

                                currentFrame.ImageData = DecodePngFrame (frameData, decodeWidth, decodeHeight);
                                apng.Frames.Add (currentFrame);
                                currentFrameChunks.Clear();
                            }

                            if (chunkLength >= 26)
                            {
                                currentFrame = new ApngFrame
                                {
                                    Width = (int)Binary.BigEndian (BitConverter.ToUInt32 (chunkData, 4)),
                                    Height = (int)Binary.BigEndian (BitConverter.ToUInt32 (chunkData, 8)),
                                    X = (int)Binary.BigEndian (BitConverter.ToUInt32 (chunkData, 12)),
                                    Y = (int)Binary.BigEndian (BitConverter.ToUInt32 (chunkData, 16)),
                                    DelayNum = Binary.BigEndian (BitConverter.ToUInt16 (chunkData, 20)),
                                    DelayDen = Binary.BigEndian (BitConverter.ToUInt16 (chunkData, 22)),
                                    DisposeOp = chunkData[24],
                                    BlendOp = chunkData[25]
                                };
                            }
                            break;

                        case CHUNK_IDAT: // Default image data
                            if (!hasActl)
                                return apng; // Regular PNG

                            if (currentFrame != null)
                            {
                                currentFrameChunks.Add (chunkData);
                                isFirstFrame = true;
                            }
                            break;

                        case CHUNK_FDAT: // Frame data
                            if (chunkLength >= 4 && currentFrame != null)
                            {
                                // Skip sequence number (first 4 bytes)
                                var imageData = new byte[chunkLength - 4];
                                Array.Copy (chunkData, 4, imageData, 0, imageData.Length);
                                currentFrameChunks.Add (imageData);
                                isFirstFrame = false;
                            }
                            break;

                        case CHUNK_IEND:
                            // Process final frame
                            if (currentFrame != null && currentFrameChunks.Count > 0)
                            {
                                var frameData = CombineChunks (currentFrameChunks);
                                int decodeWidth = currentFrame.Width;
                                int decodeHeight = currentFrame.Height;

                                if (isFirstFrame && apng.Frames.Count == 0)
                                {
                                    decodeWidth = (int)mainWidth;
                                    decodeHeight = (int)mainHeight;
                                }

                                currentFrame.ImageData = DecodePngFrame (frameData, decodeWidth, decodeHeight);
                                apng.Frames.Add (currentFrame);
                            }
                            return apng;
                    }
                }
            }
            catch (EndOfStreamException) { }

            return apng;
        }

        private byte[] CombineChunks (List<byte[]> chunks)
        {
            int totalLength = chunks.Sum (c => c.Length);
            var combined = new byte[totalLength];
            int offset = 0;

            foreach (var chunk in chunks)
            {
                Array.Copy (chunk, 0, combined, offset, chunk.Length);
                offset += chunk.Length;
            }

            return combined;
        }

        private byte[] DecodePngFrame (byte[] compressedData, int width, int height)
        {
            // Create a minimal PNG from the compressed data
            using (var ms = new MemoryStream())
            {
                // PNG header
                ms.Write (PNG_HEADER, 0, PNG_HEADER.Length);

                // IHDR chunk
                WriteChunk (ms, CHUNK_IHDR, writer =>
                {
                    writer.Write (Binary.BigEndian((uint)width));
                    writer.Write (Binary.BigEndian((uint)height));
                    writer.Write((byte)8); // bit depth
                    writer.Write((byte)PNG_COLOR_TYPE_RGBA); // color type (RGBA)
                    writer.Write((byte)0); // compression
                    writer.Write((byte)0); // filter
                    writer.Write((byte)0); // interlace
                });

                // IDAT chunk (s)
                WriteChunk (ms, CHUNK_IDAT, writer =>
                {
                    writer.Write (compressedData);
                });

                // IEND chunk
                WriteChunk (ms, CHUNK_IEND, writer => { });

                // Decode the PNG
                ms.Position = 0;
                var decoder = new PngBitmapDecoder (ms,
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

                if (decoder.Frames.Count > 0)
                {
                    var frame = decoder.Frames[0];
                    var convertedFrame = new FormatConvertedBitmap (frame, PixelFormats.Bgra32, null, 0);

                    int stride = width * 4;
                    var pixels = new byte[stride * height];
                    convertedFrame.CopyPixels (pixels, stride, 0);
                    return pixels;
                }
            }

            return new byte[width * height * 4];
        }

        private void WriteChunk (Stream stream, string chunkType, Action<BinaryWriter> writeData)
        {
            using (var chunkStream = new MemoryStream())
            {
                var typeBytes = Encoding.ASCII.GetBytes (chunkType);
                chunkStream.Write (typeBytes, 0, 4);

                using (var writer = new BinaryWriter (chunkStream, Encoding.Default, true))
                {
                    writeData (writer);
                }

                var chunkData = chunkStream.ToArray();
                var dataLength = chunkData.Length - 4;

                // Write length
                var lengthBytes = BitConverter.GetBytes (Binary.BigEndian((uint)dataLength));
                stream.Write (lengthBytes, 0, 4);

                // Write chunk data
                stream.Write (chunkData, 0, chunkData.Length);

                // Write CRC
                uint crc = Crc32.Compute (chunkData, 0, chunkData.Length);
                var crcBytes = BitConverter.GetBytes (Binary.BigEndian (crc));
                stream.Write (crcBytes, 0, 4);
            }
        }

        private void ApplyDisposal (byte[] canvas, byte[] previousCanvas, ApngFrame frame,
            int canvasWidth, int canvasHeight)
        {
            switch (frame.DisposeOp)
            {
            case APNG_DISPOSE_OP_NONE:
                // Leave canvas as is
                break;

            case APNG_DISPOSE_OP_BACKGROUND:
                // Clear frame area to transparent
                for (int y = frame.Y; y < frame.Y + frame.Height && y < canvasHeight; y++)
                {
                    for (int x = frame.X; x < frame.X + frame.Width && x < canvasWidth; x++)
                    {
                        int idx = (y * canvasWidth + x) * 4;
                        canvas[idx] = canvas[idx + 1] = canvas[idx + 2] = canvas[idx + 3] = 0;
                    }
                }
                break;

            case APNG_DISPOSE_OP_PREVIOUS:
                // Restore previous canvas
                Array.Copy (previousCanvas, canvas, canvas.Length);
                break;
            }
        }

        private void CompositeApngFrame (byte[] canvas, ApngFrame frame, int canvasWidth, int canvasHeight)
        {
            if (frame.ImageData == null) return;

            // The frame data is decoded at frame.Width x frame.Height
            // We need to composite it at position (frame.X, frame.Y) on the canvas

            for (int frameY = 0; frameY < frame.Height; frameY++)
            {
                for (int frameX = 0; frameX < frame.Width; frameX++)
                {
                    // Position on the canvas
                    int canvasX = frame.X + frameX;
                    int canvasY = frame.Y + frameY;

                    // Check bounds
                    if (canvasX < 0 || canvasX >= canvasWidth ||
                        canvasY < 0 || canvasY >= canvasHeight)
                        continue;

                    // Source index in frame data (frame is Width x Height)
                    int srcIdx = (frameY * frame.Width + frameX) * 4;

                    // Destination index in canvas (canvas is canvasWidth x canvasHeight)
                    int dstIdx = (canvasY * canvasWidth + canvasX) * 4;

                    if (srcIdx + 3 >= frame.ImageData.Length || dstIdx + 3 >= canvas.Length)
                    {
                        System.Diagnostics.Trace.WriteLine ($"Index out of bounds: srcIdx={srcIdx}, dstIdx={dstIdx}");
                        continue;
                    }

                    byte srcB = frame.ImageData[srcIdx];
                    byte srcG = frame.ImageData[srcIdx + 1];
                    byte srcR = frame.ImageData[srcIdx + 2];
                    byte srcA = frame.ImageData[srcIdx + 3];

                    if (frame.BlendOp == APNG_BLEND_OP_SOURCE || canvas[dstIdx + 3] == 0)
                    {
                        // Replace
                        canvas[dstIdx] = srcB;
                        canvas[dstIdx + 1] = srcG;
                        canvas[dstIdx + 2] = srcR;
                        canvas[dstIdx + 3] = srcA;
                    }
                    else // APNG_BLEND_OP_OVER
                    {
                        // Alpha blend
                        if (srcA == 255)
                        {
                            canvas[ dstIdx ] = srcB;
                            canvas[dstIdx+1] = srcG;
                            canvas[dstIdx+2] = srcR;
                            canvas[dstIdx+3] = 255;
                        }
                        else if (srcA > 0)
                        {
                            byte dstA = canvas[dstIdx + 3];
                            int outA = srcA + (dstA * (255 - srcA)) / 255;

                            if (outA > 0)
                            {
                                canvas[ dstIdx ] = (byte)((srcB * srcA + canvas[ dstIdx ] * dstA * (255 - srcA) / 255) / outA);
                                canvas[dstIdx+1] = (byte)((srcG * srcA + canvas[dstIdx+1] * dstA * (255 - srcA) / 255) / outA);
                                canvas[dstIdx+2] = (byte)((srcR * srcA + canvas[dstIdx+2] * dstA * (255 - srcA) / 255) / outA);
                                canvas[dstIdx+3] = (byte)outA;
                            }
                        }
                    }
                }
            }
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.ReadUInt32();
            if (file.ReadUInt32() != 0x0a1a0a0d)
                return null;

            var meta = new PngMetaData();
            bool foundIhdr = false;
            bool foundActl = false;

            try
            {
                while (true)
                {
                    uint chunk_size   = file.ReadUInt32BE();
                    byte[] chunk_type = file.ReadBytes (4);
                    string chunk_name = Encoding.ASCII.GetString (chunk_type);

                    if (Binary.AsciiEqual (chunk_type, CHUNK_IHDR))
                    {
                        meta.Width     = file.ReadUInt32BE();
                        meta.Height    = file.ReadUInt32BE();
                        int bpp        = file.ReadByte();
                        int color_type = file.ReadByte();

                        switch (color_type)
                        {
                            case PNG_COLOR_TYPE_RGB:             meta.BPP = bpp * 3; break;
                            case PNG_COLOR_TYPE_PALETTE:         meta.BPP = 24;      break;
                            case PNG_COLOR_TYPE_GRAYSCALE_ALPHA: meta.BPP = bpp * 2; break;
                            case PNG_COLOR_TYPE_RGBA:            meta.BPP = bpp * 4; break;
                            case PNG_COLOR_TYPE_GRAYSCALE:       meta.BPP = bpp;     break;
                            default: return null;
                        }

                        SkipBytes (file, chunk_size - 10 + 4); // Rest of IHDR + CRC
                        foundIhdr = true;
                    }
                    else if (Binary.AsciiEqual (chunk_type, CHUNK_ACTL))
                    {
                        // Animation control chunk
                        meta.FrameCount = (int)file.ReadUInt32BE();
                        meta.PlayCount  = (int)file.ReadUInt32BE();
                        SkipBytes (file, chunk_size - 8 + 4); // Rest of acTL + CRC
                        foundActl = true;
                    }
                    else if (Binary.AsciiEqual (chunk_type, CHUNK_OFFS))
                    {
                        int x = file.ReadInt32BE();
                        int y = file.ReadInt32BE();
                        if (file.ReadByte() == PNG_UNIT_PIXELS)
                        {
                            meta.OffsetX = x;
                            meta.OffsetY = y;
                        }
                        SkipBytes (file, chunk_size - 9 + 4); // Rest of oFFs + CRC
                    }
                    else if (Binary.AsciiEqual (chunk_type, CHUNK_IDAT) || Binary.AsciiEqual (chunk_type, CHUNK_IEND))
                    {
                        break;
                    }
                    else if (!IsStandardChunk (chunk_name))
                    {
                        // Read custom chunk
                        var chunkData = file.ReadBytes((int)chunk_size);
                        meta.CustomChunks[chunk_name] = chunkData;
                        file.ReadUInt32(); // Skip CRC
                    }
                    else
                        SkipBytes (file, chunk_size + 4); // Skip chunk data + CRC

                    if (foundIhdr && foundActl)
                        break;
                }
            }
            catch (EndOfStreamException) { }

            return foundIhdr ? meta : null;
        }

        public override void Write (Stream file, ImageData image)
        {
            if (image is AnimatedImageData animatedImage)
            {
                if (animatedImage.Frames.Count == 0)
                    throw new InvalidOperationException ("No frames to write");

                if (animatedImage.Frames.Count == 1)
                {
                    WritePNG (file, image);
                    return;
                }
                WriteAPNG (file, animatedImage);
            }
            else
                WritePNG (file, image);
        }

        public void Write (Stream file, ImageData image, Dictionary<string, byte[]> customChunks)
        {
            if (image is AnimatedImageData animatedImage)
                throw new NotSupportedException ("Custom chunks not supported for animated PNG");
            else
                WritePNGWithCustomChunks (file, image, customChunks);
        }

        private void WritePNG (Stream file, ImageData image)
        {
            if (image is PngImageData pngImage && pngImage.CustomChunks.Count > 0)
            {
                WritePNGWithCustomChunks (file, image, pngImage.CustomChunks);
                return;
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add (BitmapFrame.Create (image.Bitmap, null, null, null));

            if (0 == image.OffsetX && 0 == image.OffsetY && file.CanSeek)
            {
                encoder.Save (file);
                return;
            }

            using (var mem_stream = new MemoryStream())
            {
                encoder.Save (mem_stream);

                if (0 == image.OffsetX && 0 == image.OffsetY)
                {
                    // No offset but stream doesn't support seeking
                    mem_stream.Position = 0;
                    mem_stream.CopyTo (file);
                    return;
                }

                // PNG with offset chunk
                byte[] buf = mem_stream.GetBuffer();
                long header_pos = 8;
                mem_stream.Position = header_pos;
                uint header_length = ReadChunkLength (mem_stream);

                // Write PNG header and IHDR chunk
                file.Write (buf, 0, (int)(header_pos + header_length + 12));

                // Insert oFFs chunk
                WriteOffsChunk (file, image);

                // Write the rest of the PNG
                mem_stream.Position = header_pos + header_length + 12;
                mem_stream.CopyTo (file);
            }
        }

        private void WritePNGWithCustomChunks (Stream file, ImageData image, Dictionary<string, byte[]> customChunks)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add (BitmapFrame.Create (image.Bitmap, null, null, null));

            using (var mem_stream = new MemoryStream())
            {
                encoder.Save (mem_stream);
                mem_stream.Position = 0;

                // Copy PNG header
                byte[] header = new byte[8];
                mem_stream.Read (header, 0, 8);
                file.Write (header, 0, 8);

                // Copy IHDR chunk
                CopyNextChunk (mem_stream, file);

                // Insert custom chunks after IHDR
                foreach (var chunk in customChunks)
                    WriteChunk (file, chunk.Key, writer => writer.Write (chunk.Value));

                // Insert offset chunk if needed
                if (image.OffsetX != 0 || image.OffsetY != 0)
                    WriteOffsChunk (file, image);

                // Copy remaining chunks
                while (mem_stream.Position < mem_stream.Length)
                {
                    if (!CopyNextChunk (mem_stream, file))
                        break;
                }
            }
        }

        private bool CopyNextChunk (Stream source, Stream dest)
        {
            var lengthBytes = new byte[4];
            if (source.Read (lengthBytes, 0, 4) != 4)
                return false;

            uint chunkLength = Binary.BigEndian (BitConverter.ToUInt32 (lengthBytes, 0));

            var typeBytes = new byte[4];
            if (source.Read (typeBytes, 0, 4) != 4)
                return false;

            // Write length
            dest.Write (lengthBytes, 0, 4);

            // Write type
            dest.Write (typeBytes, 0, 4);

            // Copy data
            byte[] data = new byte[chunkLength];
            source.Read (data, 0, (int)chunkLength);
            dest.Write (data, 0, (int)chunkLength);

            // Copy CRC
            byte[] crc = new byte[4];
            source.Read (crc, 0, 4);
            dest.Write (crc, 0, 4);

            return true;
        }

        private void WriteAPNG (Stream file, AnimatedImageData animatedImage)
        {
            var frames = animatedImage.Frames;
            var delays = animatedImage.FrameDelays;

            file.Write (PNG_HEADER, 0, PNG_HEADER.Length);

            var firstFrame = frames[0];
            int width   = firstFrame.PixelWidth;
            int height  = firstFrame.PixelHeight;
            double dpiX = firstFrame.DpiX;
            double dpiY = firstFrame.DpiY;

            WriteChunk (file, CHUNK_IHDR, writer =>
            {
                writer.Write (Binary.BigEndian((uint)width));
                writer.Write (Binary.BigEndian((uint)height));
                writer.Write((byte)8); // bit depth
                writer.Write((byte)PNG_COLOR_TYPE_RGBA); // color type
                writer.Write((byte)0); // compression
                writer.Write((byte)0); // filter
                writer.Write((byte)0); // interlace
            });

            // Write pHYs chunk if DPI is not 96
            if (Math.Abs (dpiX - 96.0) > 0.001 || Math.Abs (dpiY - 96.0) > 0.001)
            {
                WriteChunk (file, "pHYs", writer =>
                {
                    uint pixelsPerMeterX = (uint)(dpiX * 39.3701);
                    uint pixelsPerMeterY = (uint)(dpiY * 39.3701);

                    writer.Write (Binary.BigEndian (pixelsPerMeterX));
                    writer.Write (Binary.BigEndian (pixelsPerMeterY));
                    writer.Write((byte)1); // unit = meter
                });
            }

            // Write acTL (animation control)
            WriteChunk (file, CHUNK_ACTL, writer =>
            {
                writer.Write (Binary.BigEndian((uint)frames.Count)); // num_frames
                writer.Write (Binary.BigEndian((uint)0)); // num_plays (0 = infinite)
            });

            uint sequenceNumber = 0;

            // Write frames
            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                var delay = i < delays.Count ? delays[i] : 100;

                // Convert frame to BGRA32 if needed
                var convertedFrame = frame;
                if (frame.Format != PixelFormats.Bgra32)
                {
                    convertedFrame = new FormatConvertedBitmap (frame, PixelFormats.Bgra32, null, 0);
                }

                int stride = convertedFrame.PixelWidth * 4;
                var pixels = new byte[stride * convertedFrame.PixelHeight];
                convertedFrame.CopyPixels (pixels, stride, 0);

                // Write fcTL (frame control)
                WriteChunk (file, CHUNK_FCTL, writer =>
                {
                    writer.Write (Binary.BigEndian (sequenceNumber++)); // sequence_number
                    writer.Write (Binary.BigEndian ((uint)convertedFrame.PixelWidth)); // width
                    writer.Write (Binary.BigEndian ((uint)convertedFrame.PixelHeight)); // height
                    writer.Write (Binary.BigEndian ((uint)0)); // x_offset
                    writer.Write (Binary.BigEndian ((uint)0)); // y_offset
                    writer.Write (Binary.BigEndian ((ushort)delay)); // delay_num (in milliseconds)
                    writer.Write (Binary.BigEndian ((ushort)1000)); // delay_den (1000 for milliseconds)
                    writer.Write ((byte)APNG_DISPOSE_OP_NONE); // dispose_op
                    writer.Write ((byte)APNG_BLEND_OP_SOURCE); // blend_op
                });

                var encodedData = EncodePngFrame (
                    pixels, convertedFrame.PixelWidth, convertedFrame.PixelHeight,
                    convertedFrame.DpiX, convertedFrame.DpiY
                );

                if (i == 0)
                {
                    // First frame uses IDAT
                    WriteChunk (file, CHUNK_IDAT, writer =>
                    {
                        writer.Write (encodedData);
                    });
                }
                else
                {
                    // Subsequent frames use fdAT
                    WriteChunk (file, CHUNK_FDAT, writer =>
                    {
                        writer.Write (Binary.BigEndian (sequenceNumber++)); // sequence_number
                        writer.Write (encodedData);
                    });
                }
            }

            WriteChunk (file, CHUNK_IEND, writer => { });
        }

        private byte[] EncodePngFrame (
            byte[] pixels, int width, int height, double dpiX, double dpiY)
        {
            using (var ms = new MemoryStream())
            {
                var bitmap = BitmapSource.Create (width, height, dpiX, dpiY,
                    PixelFormats.Bgra32, null, pixels, width * 4);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add (BitmapFrame.Create (bitmap));
                encoder.Save (ms);

                // Extract IDAT data from the encoded PNG
                ms.Position = 8; // Skip PNG header
                var idatData = new List<byte>();

                while (ms.Position < ms.Length)
                {
                    var lengthBytes = new byte[4];
                    if (ms.Read (lengthBytes, 0, 4) != 4)
                        break;

                    uint chunkLength = Binary.BigEndian (BitConverter.ToUInt32 (lengthBytes, 0));

                    var typeBytes = new byte[4];
                    if (ms.Read (typeBytes, 0, 4) != 4)
                        break;

                    string chunkType = Encoding.ASCII.GetString (typeBytes);

                    if (chunkType == CHUNK_IDAT)
                    {
                        var chunkData = new byte[chunkLength];
                        ms.Read (chunkData, 0, (int)chunkLength);
                        idatData.AddRange (chunkData);
                        ms.Seek (4, SeekOrigin.Current); // Skip CRC
                    }
                    else
                        ms.Seek (chunkLength + 4, SeekOrigin.Current); // Skip data + CRC

                    if (chunkType == CHUNK_IEND)
                        break;
                }

                return idatData.ToArray();
            }
        }

        uint ReadChunkLength (Stream file)
        {
            int length = file.ReadByte() << 24;
            length    |= file.ReadByte() << 16;
            length    |= file.ReadByte() << 8;
            length    |= file.ReadByte();
            return (uint)length;
        }

        void WriteOffsChunk (Stream file, ImageData image)
        {
            using (var membuf = new MemoryStream (32))
            {
                using (var bin = new BinaryWriter (membuf, Encoding.ASCII, true))
                {
                    bin.Write (Binary.BigEndian ((uint)9));
                    char[] tag = { 'o', 'F', 'F', 's' };
                    bin.Write (tag);
                    bin.Write (Binary.BigEndian ((uint)image.OffsetX));
                    bin.Write (Binary.BigEndian ((uint)image.OffsetY));
                    bin.Write ((byte)PNG_UNIT_PIXELS);
                    bin.Flush();
                    uint crc = Crc32.Compute (membuf.GetBuffer(), 4, 13);
                    bin.Write (Binary.BigEndian (crc));
                }
                file.Write (membuf.GetBuffer(), 0, 9+12); // chunk + size+id+crc
            }
        }

        void SkipBytes (IBinaryStream file, uint num)
        {
            if (file.CanSeek)
                file.Seek (num, SeekOrigin.Current);
            else
            {
                for (int i = 0; i < num / 4; ++i)
                    file.ReadInt32();
                for (int i = 0; i < num % 4; ++i)
                    file.ReadByte();
            }
        }

        /// <summary>Utility method used by extrenal formats to find a PNG chunk.</summary>
        public static long FindChunk (IBinaryStream file, string chunk)
        {
            if (!file.CanSeek)
                return -1L;
            try
            {
                var buf = new byte[4];
                file.Position = 8;
                while (-1 != file.PeekByte())
                {
                    long chunk_offset = file.Position;
                    uint chunk_size   = file.ReadUInt32BE();
                    if (4 != file.Read (buf, 0, 4))
                        break;
                    if (Binary.AsciiEqual (buf, chunk))
                        return chunk_offset;
                    file.Position += chunk_size + 4;
                }
            }
            catch { /* ignore errors */ }
            return -1L;
        }
    }
}
