using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Unknown
{
    [Export(typeof(ImageFormat))]
    [ExportMetadata("Priority", -100)]
    public class CorruptedPngFormat : ImageFormat
    {
        public override string         Tag { get { return "PNG/BAD"; } }
        public override string Description { get { return "PNG with corrupted/missing signature"; } }
        public override uint     Signature { get { return  0; } }

        private static readonly byte[] PngSignature  = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly byte[] IhdrSignature = { 0x49, 0x48, 0x44, 0x52 }; // "IHDR"
        private const int MinHeaderSearchSize = 128;
        private const int MaxImageDimension = 10000;

        public CorruptedPngFormat()
        {
            Extensions = new string[] { "png" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!VFS.GetExtension (file.Name, true)?.Equals ("png", StringComparison.OrdinalIgnoreCase) ?? true)
                return null;

            file.Position = 0;

            var header = file.ReadBytes (8);
            if (IsValidPngSignature (header))
                return null;

            // Search for IHDR chunk
            file.Position = 0;
            var searchSize = Math.Min (MinHeaderSearchSize, (int)file.Length);
            var buffer = file.ReadBytes (searchSize);

            int ihdrOffset = FindIhdrData (buffer);
            if (ihdrOffset < 0 || ihdrOffset + 8 > buffer.Length)
                return null;

            // Read dimensions from IHDR chunk data
            uint width = ReadUInt32BE (buffer, ihdrOffset);
            uint height = ReadUInt32BE (buffer, ihdrOffset + 4);

            if (!IsValidDimension (width) || !IsValidDimension (height))
                return null;

            return new ImageMetaData {
                Width   = width,
                Height  = height,
                BPP     = 32,
                OffsetX = 0,
                OffsetY = 0
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0;
            var fileData = file.ReadBytes((int)file.Length);
            var dataStart = FindPngDataStart (fileData);

            using (var repaired = CreateRepairedPng (fileData, dataStart))
            {
                try
                {
                    var decoder = new PngBitmapDecoder (repaired,
                        BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                        BitmapCacheOption.OnLoad);

                    var frame = decoder.Frames[0];
                    BitmapSource bitmap = frame;

                    if (bitmap.Format != PixelFormats.Bgra32 && bitmap.Format != PixelFormats.Bgr32)
                        bitmap = new FormatConvertedBitmap (frame, PixelFormats.Bgra32, null, 0);

                    int stride = bitmap.PixelWidth * 4;
                    var pixels = new byte[stride * bitmap.PixelHeight];
                    bitmap.CopyPixels (pixels, stride, 0);

                    return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, stride);
                }
                catch (Exception ex)
                {
                    throw new InvalidFormatException ($"Failed to decode repaired PNG: {ex.Message}");
                }
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotSupportedException ("CorruptedPngFormat.Write not supported");
        }

        private static bool IsValidPngSignature (byte[] data)
        {
            if (data.Length < PngSignature.Length) 
                return false;

            for (int i = 0; i < PngSignature.Length; i++)
            {
                if (data[i] != PngSignature[i]) 
                    return false;
            }
            return true;
        }

        private static int FindIhdrData (byte[] buffer)
        {
            var position = FindSignature (buffer, IhdrSignature, 0);
            if (position < 0)
                return -1;

            return position + 4;
        }

        private static int FindPngDataStart (byte[] buffer)
        {
            var ihdrPosition = FindSignature (buffer, IhdrSignature, 0);
            if (ihdrPosition < 0)
            {
                return Math.Min (8, buffer.Length);
            }

            // PNG chunks have 4-byte length before the chunk type
            // So actual chunk data should start 4 bytes before IHDR
            return Math.Max (0, ihdrPosition - 4);
        }

        private static int FindSignature (byte[] buffer, byte[] signature, int startOffset)
        {
            var maxOffset = buffer.Length - signature.Length;
            for (int i = startOffset; i <= maxOffset; i++)
            {
                bool match = true;
                for (int j = 0; j < signature.Length; j++)
                {
                    if (buffer[i + j] != signature[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return i;
            }
            return -1;
        }

        private static MemoryStream CreateRepairedPng (byte[] fileData, int dataStart)
        {
            var repaired = new MemoryStream();
            repaired.Write (PngSignature, 0, PngSignature.Length);

            if (dataStart < fileData.Length)
                repaired.Write (fileData, dataStart, fileData.Length - dataStart);

            repaired.Position = 0;
            return repaired;
        }

        private static uint ReadUInt32BE (byte[] buffer, int offset)
        {
            if (offset + 4 > buffer.Length)
                return 0;

            return ((uint)buffer[offset    ] << 24) |
                   ((uint)buffer[offset + 1] << 16) |
                   ((uint)buffer[offset + 2] << 8) |
                    (uint)buffer[offset + 3];
        }

        private static bool IsValidDimension (uint dimension)
        {
            return dimension > 0 && dimension <= MaxImageDimension;
        }
    }
}