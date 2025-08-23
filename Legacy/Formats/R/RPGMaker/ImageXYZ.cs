using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;

namespace GameRes.Formats.RPGMaker
{
    internal class XyzMetaData : ImageMetaData
    {
        public int UnpackedSize { get; set; }
    }

    [Export(typeof(ImageFormat))]
    public class XyzFormat : ImageFormat
    {
        public override string         Tag { get { return "XYZ"; } }
        public override string Description { get { return "RPG Maker 2000/2003 image"; } }
        public override uint     Signature { get { return  0x315A5958; } } // 'XYZ1'

        public XyzFormat() { Extensions = new string[] { "xyz" }; }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            if (!header.AsciiEqual ("XYZ1"))
                return null;

            int width = header.ToUInt16 (4);
            int height = header.ToUInt16 (6);

            if (width == 0 || height == 0 || width > 4096 || height > 4096)
                return null;

            // Calculate uncompressed size: 256 color palette (768 bytes) + pixel data
            int paletteSize = 256 * 3;
            int imageSize = width * height;
            int unpackedSize = paletteSize + imageSize;

            return new XyzMetaData
            {
                Width = (uint)width,
                Height = (uint)height,
                BPP = 8,
                UnpackedSize = unpackedSize
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (XyzMetaData)info;
            file.Position = 8;

            int compressedSize = (int)(file.Length - 8);
            var compressedData = new byte[compressedSize];
            file.Read (compressedData, 0, compressedSize);

            var unpackedData = new byte[meta.UnpackedSize];
            using (var input = new BinMemoryStream (compressedData))
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
            {
                int totalRead = 0;
                while (totalRead < meta.UnpackedSize)
                {
                    int bytesRead = zstream.Read (unpackedData, totalRead, meta.UnpackedSize - totalRead);
                    if (bytesRead == 0)
                        break;
                    totalRead += bytesRead;
                }
                
                if (totalRead != meta.UnpackedSize)
                    throw new InvalidFormatException ("Failed to decompress XYZ image data");
            }

            var palette = new BitmapPalette (BuildPalette(unpackedData));

            int paletteSize = 256 * 3;
            int stride = (int)meta.Width;
            var pixels = new byte[meta.Height * stride];
            Buffer.BlockCopy (unpackedData, paletteSize, pixels, 0, pixels.Length);

            // Set first color as transparent
            return ImageData.Create (info, PixelFormats.Indexed8, palette, pixels, stride);
        }

        private Color[] BuildPalette (byte[] data)
        {
            var colors = new Color[256];
            for (int i = 0; i < 256; i++)
            {
                int offset = i * 3;
                colors[i] = Color.FromRgb(
                    data[offset    ], // R
                    data[offset + 1], // G
                    data[offset + 2]  // B
                );
            }
            return colors;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("XYZ writing not supported");
        }
    }
}