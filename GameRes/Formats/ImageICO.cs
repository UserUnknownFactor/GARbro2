using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Microsoft
{
    [Export(typeof(ImageFormat))]
    public class IcoFormat : ImageFormat
    {
        public override string         Tag { get { return "ICO"; } }
        public override string Description { get { return "Windows icon format"; } }
        public override uint     Signature { get { return 0x00010000; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            var header = file.ReadHeader(6);
            if (header.ToUInt16(0) != 0 || header.ToUInt16(2) != 1)
                return null;

            int count = header.ToUInt16(4);
            if (count == 0)
                return null;

            var entry = file.ReadBytes(16);
            if (entry.Length < 16)
                return null;

            int width = entry[0] == 0 ? 256 : entry[0];
            int height = entry[1] == 0 ? 256 : entry[1];
            int bpp = entry[6];
            if (bpp == 0)
                bpp = 32;

            return new ImageMetaData
            {
                Width = (uint)width,
                Height = (uint)height,
                BPP = bpp
            };
        }

        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            file.Position = 6;
            int count = file.ReadUInt16();

            file.Seek(6 + 16 - 8, SeekOrigin.Begin); // Seek to size & offset
            uint size = file.ReadUInt32();
            uint offset = file.ReadUInt32();

            file.Position = offset;

            // Check for PNG
            var sig = file.ReadUInt32();
            if (sig == 0x474E5089) // PNG
            {
                file.Position = offset;
                var png_data = file.ReadBytes((int)size);
                using (var png = new BinMemoryStream(png_data))
                    return Png.Read(png, info);
            }

            // Fallback: parse BMP data inside ICO
            file.Position = offset;
            uint headerSize = file.ReadUInt32();
            file.Position = offset;

            byte[] dibHeader = file.ReadBytes((int)headerSize);
            int width = LittleEndian.ToInt32(dibHeader, 4);
            int heightFull = LittleEndian.ToInt32(dibHeader, 8);
            int height = heightFull / 2; // stored height includes mask area
            int bpp = LittleEndian.ToInt16(dibHeader, 14);
            int colorsUsed = LittleEndian.ToInt32(dibHeader, 32);

            int paletteEntries = (bpp <= 8) ? (colorsUsed != 0 ? colorsUsed : 1 << bpp) : 0;
            int paletteSize = paletteEntries * 4;
            int imageStride = ((width * bpp + 31) / 32) * 4;
            int imageSize = imageStride * height;
            int maskStride = ((width + 31) / 32) * 4;
            int maskSize = maskStride * height;

            byte[] pixels;
            PixelFormat pixelFormat = PixelFormats.Bgra32;

            // Move to image data (after palette)
            file.Position = offset + headerSize + paletteSize;
            byte[] imageData = file.ReadBytes(imageSize);
            byte[] mask = file.ReadBytes(maskSize);

            if (bpp == 32)
            {
                // Use provided image data with alpha
                pixels = new byte[width * height * 4];

                for (int y = 0; y < height; ++y)
                {
                    for (int x = 0; x < width; ++x)
                    {
                        int src = y * imageStride + x * 4;
                        int dst = ((height - 1 - y) * width + x) * 4;

                        pixels[dst + 0] = imageData[src + 0]; // B
                        pixels[dst + 1] = imageData[src + 1]; // G
                        pixels[dst + 2] = imageData[src + 2]; // R
                        pixels[dst + 3] = imageData[src + 3]; // A
                    }
                }

                return ImageData.Create(info, PixelFormats.Bgra32, null, pixels);
            }
            else
            {
                // Must apply AND mask for transparency
                byte[] palette = null;
                if (paletteSize > 0)
                {
                    file.Position = offset + headerSize;
                    palette = file.ReadBytes(paletteSize);
                }

                pixels = new byte[width * height * 4];

                for (int y = 0; y < height; ++y)
                {
                    for (int x = 0; x < width; ++x)
                    {
                        int dst = ((height - 1 - y) * width + x) * 4;

                        byte r = 0, g = 0, b = 0;
                        if (bpp == 24)
                        {
                            int i = y * imageStride + x * 3;
                            b = imageData[i];
                            g = imageData[i + 1];
                            r = imageData[i + 2];
                        }
                        else if (bpp == 8)
                        {
                            int index = imageData[y * imageStride + x];
                            b = palette[index * 4 + 0];
                            g = palette[index * 4 + 1];
                            r = palette[index * 4 + 2];
                        }
                        else if (bpp == 4)
                        {
                            int bytePos = y * imageStride + x / 2;
                            int index = (x % 2 == 0) ? (imageData[bytePos] >> 4) : (imageData[bytePos] & 0x0F);
                            b = palette[index * 4 + 0];
                            g = palette[index * 4 + 1];
                            r = palette[index * 4 + 2];
                        }

                        int mbyte = mask[y * maskStride + x / 8];
                        int mbit = 0x80 >> (x % 8);
                        byte a = (byte)((mbyte & mbit) != 0 ? 0 : 0xFF);

                        pixels[dst + 0] = b;
                        pixels[dst + 1] = g;
                        pixels[dst + 2] = r;
                        pixels[dst + 3] = a;
                    }
                }

                return ImageData.Create(info, PixelFormats.Bgra32, null, pixels);
            }
        }


        public override void Write(Stream file, ImageData image)
        {
            throw new NotImplementedException("IcoFormat.Write not implemented");
        }
    }
}
