using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes;
using ImageMagick;

namespace GARbro.ImageFormats
{
    public class ImageMagickMetaData : ImageMetaData
    {
        public        int FrameCount { get; set; } = 1;
        public       bool IsAnimated { get; set; } = false;
        public string OriginalFormat { get; set; }

        public    override string GetComment() {
             return Localization.Format (
                 "MsgImageSize", 
                 OriginalFormat.ToUpper() ?? Localization._T ("Unknown"), Width, Height,
                 BPP != 0 ? Localization.Format ("MsgBPP", BPP) : ""
             );
        }
    }

    [Export(typeof(ImageFormat))]
    [ExportMetadata("Priority", -1)] // try last
    public class ImageMagickFormat : ImageFormat
    {
        public override         string Tag { get { return "IMGMAGICK"; } }
        public override string Description { get { return "ImageMagick Universal Image Format"; } }
        public override     uint Signature { get { return  0; } }
        public override      bool CanWrite { get { return  true; } }

        static readonly string[] SupportedExtensions = {
            "bmp", "gif", "jpg", "jpeg", "png", "tiff", "tif", "webp", "ico",
            "psd", "pdf", "svg", "dds", "tga", "pcx", "pbm", "pgm", "ppm",
            "pnm", "xpm", "xbm", "jp2", "j2k", "jpf", "jpx", "jpm", "mj2",
            "exr", "hdr", "pic", "pict", "pct", "sgi", "rgb", "rgba", "bw",
            "dpx", "cin", "fits", "fit", "fts", "miff", "mtv", "otb", "palm",
            "pam", "pdb", "pfm", "picon", "pix", "pwp", "rad", "rgf", "rla",
            "rle", "sct", "sfw", "sun", "vicar", "viff", "wbmp", "wpg", "xwd",
            "xcf", "dcm", "dcx", "dib", "dng", "fax", "jng", "mat", "ora",
            "avif", "heic", "heif", "jxl", "flif", "apng"
        };

        public ImageMagickFormat()
        {
            Extensions = SupportedExtensions.Select(ext => ext.ToLowerInvariant()).ToArray();
            Signatures = new uint[] { 0 };
        }

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            try
            {
                var stream = file.AsStream;
                stream.Position = 0;

                // Create a temporary copy for metadata reading
                var tempData = new byte[Math.Min(stream.Length, 1024 * 1024)];
                var bytesRead = stream.Read(tempData, 0, tempData.Length);

                using (var tempStream = new MemoryStream(tempData, 0, bytesRead))
                using (var image = new MagickImage())
                {
                    try
                    {
                        image.Ping(tempStream); // Ping from the temporary stream

                        stream.Position = 0;
                        int frameCount = 1;
                        bool isAnimated = false;

                        var collectionData = new byte[stream.Length];
                        stream.Read(collectionData, 0, collectionData.Length);

                        using (var collectionStream = new MemoryStream(collectionData))
                        using (var collection = new MagickImageCollection())
                        {
                            try
                            {
                                collection.Ping(collectionStream);
                                frameCount = collection.Count;
                                isAnimated = frameCount > 1;
                            }
                            catch { }
                        }

                        stream.Position = 0;

                        return new ImageMagickMetaData
                        {
                            Width = (uint)image.Width,
                            Height = (uint)image.Height,
                            BPP = (int)image.ChannelCount * (int)image.Depth,
                            OffsetX = image.Page.X,
                            OffsetY = image.Page.Y,
                            FrameCount = frameCount,
                            IsAnimated = isAnimated,
                            OriginalFormat = image.Format.ToString(),
                        };
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
            catch (MagickException)
            {
                return null;
            }
        }

        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            var meta = info as ImageMagickMetaData;

            var stream = file.AsStream;
            stream.Position = 0;

            var imageData = new byte[stream.Length];
            stream.Read(imageData, 0, imageData.Length);

            using (var memoryStream = new MemoryStream(imageData))
            using (var collection = new MagickImageCollection())
            {
                collection.Read(memoryStream);

                if (collection.Count == 0)
                    throw new InvalidFormatException("No image data found in file");

                // Single frame image
                if (collection.Count == 1)
                {
                    var bitmap = ConvertToBitmapSource(collection[0] as MagickImage);
                    return new ImageData(bitmap, info);
                }

                // Multi-frame image
                var frames = new List<BitmapSource>();
                var delays = new List<int>();

                foreach (MagickImage frame in collection)
                {
                    frames.Add(ConvertToBitmapSource(frame));

                    int delay = 100; // Default 100ms
                    if (frame.AnimationDelay > 0)
                    {
                        // AnimationDelay is in ticks (1/100 second for GIF)
                        delay = (int)(frame.AnimationDelay * 10);
                    }
                    delays.Add(delay);
                }

                return new AnimatedImageData(frames, delays, info);
            }
        }

        public override void Write(Stream file, ImageData image)
        {
            using (var collection = new MagickImageCollection())
            {
                if (image is AnimatedImageData animatedImage && animatedImage.IsAnimated)
                {
                    for (int i = 0; i < animatedImage.Frames.Count; i++)
                    {
                        var frame = animatedImage.Frames[i];
                        var magickImage = ConvertFromBitmapSource(frame);

                        if (i < animatedImage.FrameDelays.Count)
                        {
                            magickImage.AnimationDelay = (uint)animatedImage.FrameDelays[i] / 10; // Convert ms to ticks
                        }

                        collection.Add(magickImage);
                    }
                }
                else
                {
                    var magickImage = ConvertFromBitmapSource(image.Bitmap);
                    collection.Add(magickImage);
                }

                var format = MagickFormat.Png; // Default
                if (file is FileStream fs)
                {
                    var ext = Path.GetExtension(fs.Name).TrimStart('.').ToLowerInvariant();
                    format = GetMagickFormat(ext);
                }

                if (format == MagickFormat.Gif && collection.Count > 1)
                {
                    collection.Optimize();
                    collection.OptimizeTransparency();
                }

                collection.Write(file, format);
            }
        }

        private BitmapSource ConvertToBitmapSource(MagickImage image)
        {
            byte[] pixelData;
            PixelFormat pixelFormat;
            int stride;

            // Get pixel data based on the image's channel count
            using (var pixels = image.GetPixelsUnsafe())
            {
                switch (image.ChannelCount)
                {
                    case 1: // Grayscale
                        pixelFormat = PixelFormats.Gray8;
                        stride = (int)image.Width;
                        pixelData = pixels.ToByteArray("I");
                        break;

                    case 2: // Grayscale + Alpha
                            // WPF doesn't have a native 8-bit gray+alpha format
                            // Convert to BGRA32 instead
                        pixelFormat = PixelFormats.Bgra32;
                        stride = (int)image.Width * 4;

                        // Get intensity and alpha channels
                        var intensity = pixels.ToByteArray("I");
                        var alpha = pixels.ToByteArray("A");

                        pixelData = new byte[stride * image.Height];
                        for (int i = 0; i < intensity.Length; i++)
                        {
                            int pixelIndex = i * 4;
                            pixelData[pixelIndex + 0] = intensity[i]; // B
                            pixelData[pixelIndex + 1] = intensity[i]; // G
                            pixelData[pixelIndex + 2] = intensity[i]; // R
                            pixelData[pixelIndex + 3] = alpha[i];     // A
                        }
                        break;

                    case 3: // RGB
                        pixelFormat = PixelFormats.Bgr24;
                        stride = (((int)image.Width * 3 + 3) & ~3); // 4-byte alignment
                        pixelData = ConvertToBgr(pixels, (int)image.Width, (int)image.Height, false);
                        break;

                    case 4: // RGBA
                    default:
                        pixelFormat = PixelFormats.Bgra32;
                        stride = (int)image.Width * 4;
                        pixelData = ConvertToBgr(pixels, (int)image.Width, (int)image.Height, true);
                        break;
                }
            }

            var bitmap = BitmapSource.Create(
                (int)image.Width,
                (int)image.Height,
                96, 96,
                pixelFormat,
                null,
                pixelData,
                stride);

            bitmap.Freeze();
            return bitmap;
        }

        private byte[] ConvertToBgr(IUnsafePixelCollection<byte> pixels, int width, int height, bool includeAlpha)
        {
            var channelCount = includeAlpha ? 4 : 3;
            var stride = includeAlpha ? width * 4 : ((width * 3 + 3) & ~3);
            var result = new byte[stride * height];

            if (includeAlpha)
            {
                // Get RGBA data and convert to BGRA
                var rgba = pixels.ToByteArray(PixelMapping.RGBA);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int srcIdx = (y * width + x) * 4;
                        int dstIdx = y * stride + x * 4;

                        result[dstIdx + 0] = rgba[srcIdx + 2]; // B
                        result[dstIdx + 1] = rgba[srcIdx + 1]; // G
                        result[dstIdx + 2] = rgba[srcIdx + 0]; // R
                        result[dstIdx + 3] = rgba[srcIdx + 3]; // A
                    }
                }
            }
            else
            {
                // Get RGB data and convert to BGR with padding
                var rgb = pixels.ToByteArray(PixelMapping.RGB);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int srcIdx = (y * width + x) * 3;
                        int dstIdx = y * stride + x * 3;

                        result[dstIdx + 0] = rgb[srcIdx + 2]; // B
                        result[dstIdx + 1] = rgb[srcIdx + 1]; // G
                        result[dstIdx + 2] = rgb[srcIdx + 0]; // R
                    }
                }
            }

            return result;
        }

        private MagickImage ConvertFromBitmapSource(BitmapSource bitmap)
        {
            // Convert to a common format
            BitmapSource source = bitmap;
            if (bitmap.Format != PixelFormats.Bgra32 && bitmap.Format != PixelFormats.Bgr24)
                source = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);

            var width = source.PixelWidth;
            var height = source.PixelHeight;
            var stride = (source.Format.BitsPerPixel * width + 7) / 8;
            var pixels = new byte[stride * height];

            source.CopyPixels(pixels, stride, 0);

            var magickImage = new MagickImage();
            var settings = new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.BGRA);

            if (source.Format == PixelFormats.Bgra32)
            {
                magickImage.ReadPixels(pixels, settings);
            }
            else if (source.Format == PixelFormats.Bgr24)
            {
                // Convert BGR to BGRA with stride handling
                var bgra = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int srcIdx = y * stride + x * 3;
                        int dstIdx = (y * width + x) * 4;

                        bgra[dstIdx + 0] = pixels[srcIdx + 0]; // B
                        bgra[dstIdx + 1] = pixels[srcIdx + 1]; // G
                        bgra[dstIdx + 2] = pixels[srcIdx + 2]; // R
                        bgra[dstIdx + 3] = 255; // A
                    }
                }
                magickImage.ReadPixels(bgra, settings);
            }
            return magickImage;
        }

        private MagickFormat GetMagickFormat(string extension)
        {
            switch (extension.ToLowerInvariant())
            {
                case "bmp":  return MagickFormat.Bmp;
                case "gif":  return MagickFormat.Gif;
                case "jpg":
                case "jpeg": return MagickFormat.Jpeg;
                case "png":  return MagickFormat.Png;
                case "tif":
                case "tiff": return MagickFormat.Tiff;
                case "webp": return MagickFormat.WebP;
                case "ico":  return MagickFormat.Ico;
                case "psd":  return MagickFormat.Psd;
                case "pdf":  return MagickFormat.Pdf;
                case "svg":  return MagickFormat.Svg;
                case "dds":  return MagickFormat.Dds;
                case "tga":  return MagickFormat.Tga;
                case "exr":  return MagickFormat.Exr;
                case "jp2":  return MagickFormat.Jp2;
                case "heic": return MagickFormat.Heic;
                case "avif": return MagickFormat.Avif;
                case "jxl":  return MagickFormat.Jxl;
                default:     return MagickFormat.Png;
            }
        }
    }
}