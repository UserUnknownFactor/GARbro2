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
        public int FrameCount { get; set; } = 1;
        public int PlayCount { get; set; } = 0; // 0 = infinite
        public bool IsAnimated { get { return FrameCount > 1; } }

        public override string GetComment()
        {
            var n_frames = IsAnimated ? FrameCount.Pluralize ("n_frames") : "";
            return Localization.Format ("MsgImageSize", Width, Height, BPP, n_frames);
        }
    }

    [Export(typeof(ImageFormat))]
    [ExportMetadata("Priority", 100)] // makes PNG first format in list
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
            return new ImageData (frame, info);
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

            foreach (var frame in apngData.Frames)
            {
                // Handle disposal of previous frame
                if (frames.Count > 0)
                {
                    var prevFrame = apngData.Frames[frames.Count - 1];
                    ApplyDisposal (canvas, previousCanvas, prevFrame, canvasWidth, canvasHeight);
                }

                if (frame.DisposeOp == APNG_DISPOSE_OP_PREVIOUS)
                    Array.Copy (canvas, previousCanvas, canvas.Length);

                CompositeApngFrame (canvas, frame, canvasWidth, canvasHeight);

                var frameData = new byte[canvas.Length];
                Array.Copy (canvas, frameData, canvas.Length);

                var bitmap = BitmapSource.Create (canvasWidth, canvasHeight, 96, 96,
                                PixelFormats.Bgra32, null, frameData, canvasWidth * 4);
                bitmap.Freeze();
                frames.Add (bitmap);

                // Convert delay from fractions to milliseconds
                int delayMs = frame.DelayDen > 0
                    ? (frame.DelayNum * 1000) / frame.DelayDen
                    : 100;
                delays.Add (Math.Max (delayMs, 10)); // Minimum 10ms
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
        }

        private ApngData ParseApng (IBinaryStream file)
        {
            var apng        = new ApngData();
            var chunks      = new List<byte[]>();
            var frameChunks = new Dictionary<int, List<byte[]>>();
            ApngFrame currentFrame = null;
            int sequence    = 0;
            bool hasActl    = false;

            file.Position   = 8; // Skip PNG header

            while (file.Position < file.Length)
            {
                long chunkStart  = file.Position;
                uint chunkLength = file.ReadUInt32BE();
                var chunkType    = file.ReadBytes (4);

                if (chunkLength > int.MaxValue || file.Position + chunkLength + 4 > file.Length)
                    break;

                var chunkData = file.ReadBytes((int)chunkLength);
                file.ReadUInt32(); // CRC

                string chunkName = Encoding.ASCII.GetString (chunkType);

                switch (chunkName)
                {
                case CHUNK_ACTL: // Animation control
                    if (chunkLength >= 8)
                    {
                        apng.NumFrames = Binary.BigEndian (BitConverter.ToInt32 (chunkData, 0));
                        apng.NumPlays  = Binary.BigEndian (BitConverter.ToInt32 (chunkData, 4));
                        hasActl = true;
                    }
                    break;

                case CHUNK_FCTL: // Frame control
                    if (chunkLength >= 26)
                    {
                        var frameSeq  =      Binary.BigEndian (BitConverter.ToUInt32 (chunkData, 0));

                        currentFrame  = new ApngFrame {
                            Width     = (int)Binary.BigEndian (BitConverter.ToUInt32 (chunkData, 4)),
                            Height    = (int)Binary.BigEndian (BitConverter.ToUInt32 (chunkData, 8)),
                            X         = (int)Binary.BigEndian (BitConverter.ToUInt32 (chunkData, 12)),
                            Y         = (int)Binary.BigEndian (BitConverter.ToUInt32 (chunkData, 16)),
                            DelayNum  =      Binary.BigEndian (BitConverter.ToUInt16 (chunkData, 20)),
                            DelayDen  =      Binary.BigEndian (BitConverter.ToUInt16 (chunkData, 22)),
                            DisposeOp = chunkData[24],
                            BlendOp   = chunkData[25]
                        };

                        if (!frameChunks.ContainsKey (sequence))
                            frameChunks[sequence] = new List<byte[]>();
                    }
                    break;

                case CHUNK_IDAT: // Default image data
                    if (!hasActl || currentFrame == null)
                        return apng; // Regular PNG

                    if (!frameChunks.ContainsKey (sequence))
                        frameChunks[sequence] = new List<byte[]>();
                    frameChunks[sequence].Add (chunkData);
                    break;

                case CHUNK_FDAT: // Frame data
                    if (chunkLength >= 4 && currentFrame != null)
                    {
                        // Skip sequence number (first 4 bytes)
                        var imageData = new byte[chunkLength - 4];
                        Array.Copy (chunkData, 4, imageData, 0, imageData.Length);

                        if (!frameChunks.ContainsKey (sequence))
                            frameChunks[sequence] = new List<byte[]>();
                        frameChunks[sequence].Add (imageData);
                    }
                    break;

                case CHUNK_IEND:
                    goto LAST_FRAME;
                }

                // If we finished collecting data for current frame
                if (currentFrame != null && chunkName == "IDAT" || chunkName == "fdAT")
                {
                    var nextChunkPos = file.Position;
                    if (nextChunkPos + 8 <= file.Length)
                    {
                        file.ReadUInt32(); // Next chunk length
                        var nextType = file.ReadBytes (4);
                        file.Position = nextChunkPos; // Restore position

                        string nextChunkName = Encoding.ASCII.GetString (nextType);
                        if (nextChunkName == "fcTL" || nextChunkName == "IEND")
                        {
                            if (frameChunks.ContainsKey (sequence))
                            {
                                var frameData = CombineChunks (frameChunks[sequence]);
                                currentFrame.ImageData = DecodePngFrame (frameData,
                                    currentFrame.Width, currentFrame.Height);
                                apng.Frames.Add (currentFrame);
                                sequence++;
                            }
                        }
                    }
                }
            }
        LAST_FRAME:
            if (currentFrame != null && frameChunks.ContainsKey (sequence))
            {
                var frameData = CombineChunks (frameChunks[sequence]);
                currentFrame.ImageData = DecodePngFrame (frameData,
                    currentFrame.Width, currentFrame.Height);
                apng.Frames.Add (currentFrame);
            }

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

            for (int y = 0; y < frame.Height; y++)
            {
                for (int x = 0; x < frame.Width; x++)
                {
                    int canvasX = frame.X + x;
                    int canvasY = frame.Y + y;

                    if (canvasX >= 0 && canvasX < canvasWidth &&
                        canvasY >= 0 && canvasY < canvasHeight)
                    {
                        int srcIdx = (y * frame.Width + x) * 4;
                        int dstIdx = (canvasY * canvasWidth + canvasX) * 4;

                        if (srcIdx + 3 < frame.ImageData.Length && dstIdx + 3 < canvas.Length)
                        {
                            if (frame.BlendOp == APNG_BLEND_OP_SOURCE)
                            {
                                // Replace
                                canvas[dstIdx] = frame.ImageData[srcIdx];         // B
                                canvas[dstIdx + 1] = frame.ImageData[srcIdx + 1]; // G
                                canvas[dstIdx + 2] = frame.ImageData[srcIdx + 2]; // R
                                canvas[dstIdx + 3] = frame.ImageData[srcIdx + 3]; // A
                            }
                            else // APNG_BLEND_OP_OVER
                            {
                                // Alpha blend
                                byte srcA = frame.ImageData[srcIdx + 3];
                                if (srcA == 255)
                                {
                                    canvas[dstIdx] = frame.ImageData[srcIdx];
                                    canvas[dstIdx + 1] = frame.ImageData[srcIdx + 1];
                                    canvas[dstIdx + 2] = frame.ImageData[srcIdx + 2];
                                    canvas[dstIdx + 3] = 255;
                                }
                                else if (srcA > 0)
                                {
                                    byte dstA = canvas[dstIdx + 3];
                                    int outA = srcA + (dstA * (255 - srcA)) / 255;

                                    if (outA > 0)
                                    {
                                        canvas[dstIdx] = (byte)((frame.ImageData[srcIdx] * srcA +
                                            canvas[dstIdx] * dstA * (255 - srcA) / 255) / outA);
                                        canvas[dstIdx + 1] = (byte)((frame.ImageData[srcIdx + 1] * srcA +
                                            canvas[dstIdx + 1] * dstA * (255 - srcA) / 255) / outA);
                                        canvas[dstIdx + 2] = (byte)((frame.ImageData[srcIdx + 2] * srcA +
                                            canvas[dstIdx + 2] * dstA * (255 - srcA) / 255) / outA);
                                        canvas[dstIdx + 3] = (byte)outA;
                                    }
                                }
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

            while (file.Position < file.Length)
            {
                uint chunk_size    = file.ReadUInt32BE();
                byte[] chunk_type  = file.ReadBytes (4);

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
                    break;
                else
                    SkipBytes (file, chunk_size + 4); // Skip chunk data + CRC

                if (foundIhdr && (foundActl || file.Position > file.Length / 2))
                    break;
            }

            return foundIhdr ? meta : null;
        }

        public override void Write (Stream file, ImageData image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add (BitmapFrame.Create (image.Bitmap, null, null, null));
            if (0 == image.OffsetX && 0 == image.OffsetY)
            {
                encoder.Save (file);
                return;
            }
            using (var mem_stream = new MemoryStream())
            {
                encoder.Save (mem_stream);
                byte[] buf = mem_stream.GetBuffer();
                long header_pos = 8;
                mem_stream.Position = header_pos;
                uint header_length = ReadChunkLength (mem_stream);
                file.Write (buf, 0, (int)(header_pos+header_length+12));
                WriteOffsChunk (file, image);
                mem_stream.Position = header_pos+header_length+12;
                mem_stream.CopyTo (file);
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
