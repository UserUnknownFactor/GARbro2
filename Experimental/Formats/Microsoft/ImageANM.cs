using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats
{
    public class AnmMetaData : ImageMetaData
    {
        public int          FrameCount { get; set; }
        public List<long> FrameOffsets { get; set; }
        public List<int>    FrameSizes { get; set; }
        public bool         IsAnimated { get { return FrameCount > 1; } }

        public override string GetComment()
        {
            var n_frames = IsAnimated ? FrameCount.Pluralize ("n_frames") : "";
            return Localization.Format ("MsgImageSize", Width, Height, BPP, n_frames);
        }
    }

    [Export(typeof(ImageFormat))]
    public class AnmFormat : ImageFormat
    {
        public override string         Tag { get { return "ANM"; } }
        public override string Description { get { return "Sequential JPEG animation format"; } }
        public override uint     Signature { get { return  0xE0FFD8FF; } } // FF D8 FF E0 (little-endian)
        // JPEG markers
        const ushort JpegSOI = 0xFFD8; // Start of Image
        const ushort JpegEOI = 0xFFD9; // End of Image

        public AnmFormat()
        {
            Extensions = new[] { "anm" };
        }

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            string extension = Path.GetExtension (file.Name);

            var sig = file.Signature;
            if ((sig & 0xFFFF) != 0xD8FF) // Check for JPEG SOI
                return null;

            file.Position = 0;

            // Find all JPEG frames by scanning for EOI+SOI pattern
            var frameOffsets = new List<long>();
            var frameSizes = new List<int>();

            // First frame starts at position 0
            frameOffsets.Add(0);

            // Scan for EOI followed by SOI (FF D9 FF D8)
            byte[] buffer = new byte[4096];
            long pos = 0;

            while (pos < file.Length - 3)
            {
                file.Position = pos;
                int bytesToRead = Math.Min(buffer.Length, (int)(file.Length - pos));
                int bytesRead = file.Read(buffer, 0, bytesToRead);

                for (int i = 0; i < bytesRead - 3; i++)
                {
                    // Look for FF D9 FF D8 pattern
                    if (buffer[i] == 0xFF && buffer[i + 1] == 0xD9 && 
                        buffer[i + 2] == 0xFF && buffer[i + 3] == 0xD8)
                    {
                        long currentFrameEnd = pos + i + 2; // Position after FF D9
                        long nextFrameStart = pos + i + 2;  // Position of FF D8

                        // Calculate size of current frame
                        if (frameOffsets.Count > 0)
                            frameSizes.Add((int)(currentFrameEnd - frameOffsets[frameOffsets.Count - 1]));

                        frameOffsets.Add(nextFrameStart);

                        // Skip past the SOI we just found
                        i += 3;
                    }
                }

                // Move forward, but overlap by 3 bytes to catch patterns on buffer boundaries
                pos += Math.Max(1, bytesRead - 3);
            }

            // Add size of last frame
            if (frameOffsets.Count > 0)
                frameSizes.Add((int)(file.Length - frameOffsets[frameOffsets.Count - 1]));

            if (frameOffsets.Count == 0)
                return null;

            // Read first frame to get dimensions
            file.Position = frameOffsets[0];
            var firstFrameData = file.ReadBytes(frameSizes[0]);

            using (var ms = new MemoryStream(firstFrameData))
            {
                try
                {
                    var decoder = new JpegBitmapDecoder(ms, 
                        BitmapCreateOptions.PreservePixelFormat, 
                        BitmapCacheOption.None);

                    var frame = decoder.Frames[0];

                    return new AnmMetaData
                    {
                        Width        = (uint)frame.PixelWidth,
                        Height       = (uint)frame.PixelHeight,
                        BPP          = frame.Format.BitsPerPixel,
                        FrameCount   = frameOffsets.Count,
                        FrameOffsets = frameOffsets,
                        FrameSizes   = frameSizes
                    };
                }
                catch
                {
                    return null;
                }
            }
        }

        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            var meta = (AnmMetaData)info;

            if (meta.FrameCount == 1)
            {
                // Single frame - just decode the JPEG
                file.Position = meta.FrameOffsets[0];
                var jpegData = file.ReadBytes(meta.FrameSizes[0]);

                using (var ms = new MemoryStream(jpegData))
                {
                    var decoder = new JpegBitmapDecoder(ms,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);

                    var frame = decoder.Frames[0];
                    frame.Freeze();
                    return new ImageData(frame, info);
                }
            }
            else
            {
                // Multiple frames - create animation
                var frames = new List<BitmapSource>();
                var delays = new List<int>();

                for (int i = 0; i < meta.FrameCount; i++)
                {
                    file.Position = meta.FrameOffsets[i];
                    var jpegData = file.ReadBytes(meta.FrameSizes[i]);

                    using (var ms = new MemoryStream(jpegData))
                    {
                        try
                        {
                            var decoder = new JpegBitmapDecoder(ms,
                                BitmapCreateOptions.PreservePixelFormat,
                                BitmapCacheOption.OnLoad);

                            var frame = decoder.Frames[0];

                            // Convert to consistent format
                            BitmapSource processedFrame = frame;
                            if (frame.Format != PixelFormats.Bgr24 && frame.Format != PixelFormats.Bgra32)
                            {
                                processedFrame = new FormatConvertedBitmap(frame, 
                                    PixelFormats.Bgra32, null, 0);
                            }

                            processedFrame.Freeze();
                            frames.Add(processedFrame);

                            // Default 100ms delay (10 fps)
                            delays.Add(100);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Trace.WriteLine($"Failed to decode frame {i}: {ex.Message}");
                            // Skip corrupted frames
                            continue;
                        }
                    }
                }

                if (frames.Count == 0)
                    throw new InvalidFormatException("No valid frames found in ANM file");

                return new AnimatedImageData(frames, delays, info);
            }
        }

        public override void Write(Stream file, ImageData image)
        {
            if (image is AnimatedImageData animData && animData.Frames.Count > 1)
            {
                // Write concatenated JPEGs
                var encoder = new JpegBitmapEncoder();
                encoder.QualityLevel = 90;

                foreach (var frame in animData.Frames)
                {
                    encoder.Frames.Clear();
                    encoder.Frames.Add(BitmapFrame.Create(frame));
                    encoder.Save(file);
                }
            }
            else
            {
                // Single frame
                var encoder = new JpegBitmapEncoder();
                encoder.QualityLevel = 90;
                encoder.Frames.Add(BitmapFrame.Create(image.Bitmap));
                encoder.Save(file);
            }
        }
    }
}