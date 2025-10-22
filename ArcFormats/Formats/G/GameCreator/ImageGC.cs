using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Custom
{
    [Export(typeof(ImageFormat))]
    [ExportMetadata("Priority", -1)]
    public class GCImageFormat : ImageFormat
    {
        public override string         Tag { get { return "GC/IMG"; } }
        public override string Description { get { return "GameCreator image format"; } }
        public override uint     Signature { get { return  0; } }

        private static readonly byte[][] KnownSignatures = new byte[][]
        {
            new byte[] { 0x89, 0x50, 0x4E, 0x47 }, // PNG
            new byte[] { 0xFF, 0xD8, 0xFF },       // JPEG
            new byte[] { 0x42, 0x4D },             // BMP
            new byte[] { 0x47, 0x49, 0x46 },       // GIF
            new byte[] { 0x52, 0x49, 0x46, 0x46 }, // RIFF
        };

        public GCImageFormat()
        {
            Extensions = new string[] { "png", "jpg", "gif", "webp" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var ext = VFS.GetExtension (stream.Name, true).ToLowerInvariant();
            if (!Extensions.Contains (ext))
                return null;

            long markerPos = (stream.Length - 1) / 2;

            stream.Position = markerPos;
            if (stream.ReadByte() != 1)
                return null;

            stream.Position = 0;
            var headerSize = Math.Min (64, stream.Length);
            var header = new byte[headerSize];
            stream.Read (header, 0, header.Length);
            if (header.Length > 2)
            {
                byte temp = header[1];
                header[1] = header[2];
                header[2] = temp;
            }

            byte[] decryptedHeader;
            if (markerPos < headerSize)
            {
                decryptedHeader = new byte[headerSize - 1];
                int mPos = (int)markerPos;
                Array.Copy (header, 0, decryptedHeader, 0, mPos);
                Array.Copy (header, mPos + 1, decryptedHeader, mPos, header.Length - mPos - 1);
            }
            else
                decryptedHeader = header;

            var dimensions = GetImageDimensions (decryptedHeader);
            if (dimensions == null)
            {
                var decrypted = DecryptData (stream);
                using (var decryptedStream = new BinMemoryStream (decrypted))
                {
                    var format = ImageFormat.FindFormat (decryptedStream);
                    if (format == null)
                        return null;

                    return new ObfuscatedMetaData {
                        Width = format.Item2.Width,
                        Height = format.Item2.Height,
                        BPP = format.Item2.BPP,
                        MarkerPosition = markerPos,
                        ActualFormat = format.Item1,
                        ActualMetaData = format.Item2
                    };
                }
            }

            return new ObfuscatedMetaData {
                Width = dimensions.Item1,
                Height = dimensions.Item2,
                BPP = dimensions.Item3,
                MarkerPosition = markerPos
            };
        }

        private Tuple<uint, uint, int> GetImageDimensions (byte[] header)
        {
            if (header.Length < 24)
                return null;

            // PNG
            if (header.Length >= 24 && 
                header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            {
                uint width = (uint)((header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19]);
                uint height = (uint)((header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23]);
                if (header.Length > 25)
                {
                    byte bitDepth = header[24];
                    byte colorType = header[25];
                    int bpp;
                    switch (colorType)
                    {
                    case 0: // Grayscale
                        bpp = bitDepth; 
                        break;
                    case 2: // RGB (3 channels)
                        bpp = bitDepth * 3; 
                        break;
                    case 3: // Palette-based
                        bpp = 8; // Always 8-bit indices, but displays as 24-bit
                        break;
                    case 4: // Grayscale + Alpha (2 channels)
                        bpp = bitDepth * 2; 
                        break;
                    case 6: // RGBA (4 channels)
                        bpp = bitDepth * 4; 
                        break;
                    default:
                        bpp = 32;
                        break;
                    }
                    return Tuple.Create(width, height, bpp);
                }
                return Tuple.Create(width, height, 32);
            }

            // JPEG
            if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                return null;
            }

            // BMP
            if (header.Length >= 30 && header[0] == 0x42 && header[1] == 0x4D)
            {
                uint width  = BitConverter.ToUInt32 (header, 18);
                uint height = BitConverter.ToUInt32 (header, 22);
                ushort bpp  = BitConverter.ToUInt16 (header, 28);
                return Tuple.Create (width, height, (int)bpp);
            }

            // GIF
            if (header.Length >= 11 && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46)
            {
                uint width  = BitConverter.ToUInt16 (header, 6);
                uint height = BitConverter.ToUInt16 (header, 8);
                byte packed = header[10];
                bool hasGlobalColorTable = (packed & 0x80) != 0;
                if (hasGlobalColorTable)
                {
                    int colorTableSize = 2 << (packed & 0x07); // 2^(N+1) colors
                    return Tuple.Create (width, height, 8);
                }
                return Tuple.Create (width, height, 8);
            }

            return null;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (ObfuscatedMetaData)info;

            if (meta.ActualFormat != null && meta.ActualMetaData != null)
            {
                var decrypted = DecryptData (stream);
                using (var decryptedStream = new BinMemoryStream (decrypted))
                {
                    return meta.ActualFormat.Read (decryptedStream, meta.ActualMetaData);
                }
            }

            var decryptedData = DecryptData (stream);
            using (var decryptedStream = new BinMemoryStream (decryptedData))
            {
                var format = ImageFormat.FindFormat (decryptedStream);
                if (format == null)
                    throw new InvalidFormatException ("Decrypted data is not a valid image");

                decryptedStream.Position = 0;
                return format.Item1.Read (decryptedStream, format.Item2);
            }
        }

        private byte[] DecryptData (IBinaryStream stream)
        {
            stream.Position = 0;
            var encryptedData = new byte[stream.Length];
            stream.Read (encryptedData, 0, encryptedData.Length);

            if (encryptedData.Length > 2)
            {
                byte temp = encryptedData[1];
                encryptedData[1] = encryptedData[2];
                encryptedData[2] = temp;
            }

            var markerPos = (int)((encryptedData.Length - 1) / 2);
            var decrypted = new byte[encryptedData.Length - 1];

            Array.Copy (encryptedData, 0, decrypted, 0, markerPos);
            Array.Copy (encryptedData, markerPos + 1, decrypted, markerPos, encryptedData.Length - markerPos - 1);

            return decrypted;
        }

        private bool MatchesSignature (byte[] data, byte[] signature)
        {
            if (data.Length < signature.Length)
                return false;

            for (int i = 0; i < signature.Length; i++)
            {
                if (data[i] != signature[i])
                    return false;
            }
            return true;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GCImageFormat.Write not implemented");
        }

        private class ObfuscatedMetaData : ImageMetaData
        {
            public          long MarkerPosition { get; set; }
            public     ImageFormat ActualFormat { get; set; }
            public ImageMetaData ActualMetaData { get; set; }
        }
    }
}