using System;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Silky
{
    internal class AkbWriter
    {
        private Stream m_output;
        private AkbMetaData m_info;
        private ImageData m_image;
        private int m_pixel_size;

        public AkbWriter (Stream output, ImageData image, AkbMetaData info)
        {
            m_output = output;
            m_image = image;
            m_info = info;
            m_pixel_size = m_info.BPP / 8;
        }

        public void Write()
        {
            m_info.WriteHeader (m_output);
            WriteImageData();
        }

        private void WriteImageData()
        {
            if (m_info.InnerWidth == 0 || m_info.InnerHeight == 0)
                return;

            var pixels = GetPixelData();
            var innerPixels = ExtractInnerRegion (pixels);
            var deltaEncoded = CreateDeltaEncoded (innerPixels);
            WriteCompressedData (deltaEncoded);
        }

        private byte[] GetPixelData()
        {
            var bitmap = m_image.Bitmap;
            if (bitmap == null)
                throw new InvalidOperationException ("Image bitmap is null");

            PixelFormat targetFormat;
            if (m_info.BPP == 24)
                targetFormat = PixelFormats.Bgr24;
            else if ((m_info.Flags & 0x80000000) != 0)
                targetFormat = PixelFormats.Bgra32;
            else
                targetFormat = PixelFormats.Bgr32;

            FormatConvertedBitmap converted = new FormatConvertedBitmap (bitmap, targetFormat, null, 0);

            int stride = (int)m_info.Width * m_pixel_size;
            var pixels = new byte[(int)m_info.Height * stride];
            converted.CopyPixels (pixels, stride, 0);

            return pixels;
        }

        private byte[] ExtractInnerRegion (byte[] sourcePixels)
        {
            int sourceStride = (int)m_info.Width * m_pixel_size;
            int innerStride = m_info.InnerWidth * m_pixel_size;
            var innerPixels = new byte[m_info.InnerHeight * innerStride];

            for (int y = 0; y < m_info.InnerHeight; y++)
            {
                int srcOffset = (y + m_info.OffsetY) * sourceStride + m_info.OffsetX * m_pixel_size;
                int dstOffset = y * innerStride;
                Buffer.BlockCopy (sourcePixels, srcOffset, innerPixels, dstOffset, innerStride);
            }

            return innerPixels;
        }

        private byte[] CreateDeltaEncoded (byte[] original)
        {
            int stride = m_info.InnerWidth * m_pixel_size;
            var encoded = new byte[original.Length];
            Buffer.BlockCopy (original, 0, encoded, 0, original.Length);

            // Reverse the second restoration pass (vertical)
            int src = 0;
            for (int i = stride; i < encoded.Length; i++)
                encoded[i] = (byte)(original[i] - original[src++]);

            // Save the current first row after vertical encoding
            var firstRowAfterVertical = new byte[stride];
            Buffer.BlockCopy (encoded, 0, firstRowAfterVertical, 0, stride);

            // Reverse the first restoration pass (horizontal)
            src = 0;
            for (int i = m_pixel_size; i < stride; i++)
                encoded[i] = (byte)(original[i] - firstRowAfterVertical[src++]);

            return encoded;
        }

        private void WriteCompressedData (byte[] pixels)
        {
            int stride = m_info.InnerWidth * m_pixel_size;

            using (var lz = new LzssStream (m_output, LzssMode.Compress, true))
            {
                // Write rows in inverted order (bottom to top)
                for (int pos = pixels.Length - stride; pos >= 0; pos -= stride)
                {
                    lz.Write (pixels, pos, stride);
                }
            }
        }
    }

    public partial class AkbFormat
    {
        public override void Write (Stream file, ImageData image)
        {
            // Try to recover metadata from PNG chunks
            AkbMetaData meta = null;
            if (image is PngImageData pngData && pngData.CustomChunks.TryGetValue ("aKBm", out var chunk))
                meta = AkbMetaData.Deserialize (chunk, image.Width, image.Height);
            else
                meta = AkbMetaData.CreateDefault (image);

            if (meta.IsIncremental)
                WriteIncremental (file, image, meta);
            else
                WriteStandalone (file, image, meta);
        }

        private void WriteStandalone (Stream file, ImageData image, AkbMetaData meta)
        {
            var writer = new AkbWriter (file, image, meta);
            writer.Write();
        }

        private void WriteIncremental (Stream file, ImageData image, AkbMetaData meta)
        {
            byte[] basePixels = null;
            if (!string.IsNullOrEmpty (meta.BaseFileName))
                basePixels = ReadBaseImageForWrite (meta.BaseFileName, image, meta.BPP);

            if (basePixels != null)
            {
                var modifiedImage = CreateIncrementalImage (image, basePixels, meta);
                var writer = new AkbWriter (file, modifiedImage, meta);
                writer.Write();
            }
            else
            {
                // No base image found, fallback to standalone
                meta.BaseFileName = null;
                WriteStandalone (file, image, meta);
            }
        }

        private ImageData CreateIncrementalImage (ImageData image, byte[] basePixels, AkbMetaData meta)
        {
            var bitmap = image.Bitmap;
            if (bitmap == null)
                throw new InvalidOperationException ("Image bitmap cannot be null");

            int pixelSize = meta.BPP / 8;
            int stride = (int)meta.Width * pixelSize;
            var pixels = new byte[(int)meta.Height * stride];

            PixelFormat targetFormat = meta.BPP == 24 ? PixelFormats.Bgr24 : 
                                      (meta.Flags & 0x80000000) != 0 ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
            var converted = new FormatConvertedBitmap (bitmap, targetFormat, null, 0);
            converted.CopyPixels (pixels, stride, 0);

            // Replace unchanged pixels with magic green
            for (int y = 0; y < meta.InnerHeight; y++)
            {
                for (int x = 0; x < meta.InnerWidth; x++)
                {
                    int offset = (y + meta.OffsetY) * stride + (x + meta.OffsetX) * pixelSize;

                    bool unchanged = true;
                    for (int j = 0; j < pixelSize; j++)
                    {
                        if (pixels[offset + j] != basePixels[offset + j])
                        {
                            unchanged = false;
                            break;
                        }
                    }

                    if (unchanged)
                    {
                        pixels[offset    ] = 0x00;     // B
                        pixels[offset + 1] = 0xFF;     // G
                        pixels[offset + 2] = 0x00;     // R
                        if (pixelSize == 4)
                            pixels[offset + 3] = 0xFF; // A (opaque for magic green)
                    }
                }
            }

            var modifiedBitmap = BitmapSource.Create (
                (int)meta.Width, (int)meta.Height,
                96, 96, targetFormat, null, pixels, stride);

            return new ImageData (modifiedBitmap, meta);
        }

        private byte[] ReadBaseImageForWrite (string baseName, ImageData targetImage, int bpp)
        {
            try
            {
                var pattern = Path.GetFileNameWithoutExtension (baseName) + ".*";
                pattern = VFS.CombinePath (VFS.GetDirectoryName (baseName), pattern);

                foreach (var entry in VFS.GetFiles (pattern))
                {
                    using (var base_file = VFS.OpenBinaryStream (entry))
                    {
                        var base_info = ReadMetaData (base_file) as AkbMetaData;
                        if (base_info != null && base_info.Width == targetImage.Width && 
                            base_info.Height == targetImage.Height && base_info.BPP == bpp)
                        {
                            var reader = new AkbReader (base_file.AsStream, base_info);
                            return reader.Unpack();
                        }
                    }
                }
            }
            catch { }
            return null;
        }
    }
}