using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Collections.Generic;
using GameRes.Utility;
using System.Windows.Media;
using System.Text;
using System.Linq;

namespace GameRes.Formats.Apple
{
    [Export(typeof(ImageFormat))]
    public class IcnsFormat : ImageFormat
    {
        public override string         Tag { get { return "ICNS"; } }
        public override string Description { get { return "macOS icon format"; } }
        public override uint     Signature { get { return 0x736E6369; } } // 'icns'
        public override bool      CanWrite { get { return false; } }

        static readonly Dictionary<uint, IconType> IconTypes = new Dictionary<uint, IconType>
        {
            // 16x16 icons
            { 0x69637334, new IconType { Width = 16, Height = 16, Format = "ARGB" } },   // 'ics4' - 16x16 4-bit
            { 0x69637338, new IconType { Width = 16, Height = 16, Format = "8BIT" } },   // 'ics8' - 16x16 8-bit
            { 0x69733332, new IconType { Width = 16, Height = 16, Format = "RGB", Compressed = true } },  // 'is32' - 16x16 24-bit RGB
            { 0x73386D6B, new IconType { Width = 16, Height = 16, Format = "MASK" } },   // 's8mk' - 16x16 8-bit mask
            { 0x69637034, new IconType { Width = 16, Height = 16, Format = "MIXED" } },  // 'icp4' - 16x16 PNG/JP2/RGB
            { 0x69633131, new IconType { Width = 32, Height = 32, Format = "PNG" } },    // 'ic11' - 16x16@2x retina
            { 0x69633034, new IconType { Width = 16, Height = 16, Format = "ARGB", Compressed = true } }, // 'ic04' - 16x16 ARGB
            
            // 32x32 icons
            { 0x69636C34, new IconType { Width = 32, Height = 32, Format = "4BIT" } },   // 'icl4' - 32x32 4-bit
            { 0x69636C38, new IconType { Width = 32, Height = 32, Format = "8BIT" } },   // 'icl8' - 32x32 8-bit
            { 0x696C3332, new IconType { Width = 32, Height = 32, Format = "RGB", Compressed = true } },  // 'il32' - 32x32 24-bit RGB
            { 0x6C386D6B, new IconType { Width = 32, Height = 32, Format = "MASK" } },   // 'l8mk' - 32x32 8-bit mask
            { 0x69637035, new IconType { Width = 32, Height = 32, Format = "MIXED" } },  // 'icp5' - 32x32 PNG/JPEG2000/RGB
            { 0x69633132, new IconType { Width = 64, Height = 64, Format = "PNG" } },    // 'ic12' - 32x32@2x retina
            { 0x69633035, new IconType { Width = 32, Height = 32, Format = "ARGB", Compressed = true } }, // 'ic05' - 32x32 ARGB
            
            // 48x48 icons
            { 0x69636834, new IconType { Width = 48, Height = 48, Format = "4BIT" } },   // 'ich4' - 48x48 4-bit
            { 0x69636838, new IconType { Width = 48, Height = 48, Format = "8BIT" } },   // 'ich8' - 48x48 8-bit
            { 0x69683332, new IconType { Width = 48, Height = 48, Format = "RGB", Compressed = true } },  // 'ih32' - 48x48 24-bit RGB
            { 0x68386D6B, new IconType { Width = 48, Height = 48, Format = "MASK" } },   // 'h8mk' - 48x48 8-bit mask
            { 0x69637036, new IconType { Width = 48, Height = 48, Format = "MIXED" } },  // 'icp6' - 48x48 PNG/JPEG2000
            
            // 128x128 icons
            { 0x69743332, new IconType { Width = 128, Height = 128, Format = "RGB", Compressed = true, HasHeader = true } }, // 'it32' - 128x128 24-bit RGB
            { 0x74386D6B, new IconType { Width = 128, Height = 128, Format = "MASK" } }, // 't8mk' - 128x128 8-bit mask
            { 0x69633037, new IconType { Width = 128, Height = 128, Format = "MIXED" } }, // 'ic07' - 128x128 PNG/JPEG2000
            { 0x69633133, new IconType { Width = 256, Height = 256, Format = "PNG" } },  // 'ic13' - 128x128@2x retina
            
            // PNG/JPEG2000 compressed icons
            { 0x69633038, new IconType { Width = 256, Height = 256, Format = "MIXED" } },  // 'ic08' - 256x256
            { 0x69633039, new IconType { Width = 512, Height = 512, Format = "MIXED" } },  // 'ic09' - 512x512
            { 0x69633130, new IconType { Width = 1024, Height = 1024, Format = "MIXED" } }, // 'ic10' - 1024x1024 (or 512x512@2x)
            { 0x69633134, new IconType { Width = 512, Height = 512, Format = "MIXED" } },  // 'ic14' - 256x256@2x retina
        };

        class IconType
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public string Format { get; set; }
            public bool Compressed { get; set; }
            public bool HasHeader { get; set; }
        }

        class IconEntry
        {
            public uint Type { get; set; }
            public uint Size { get; set; }
            public long Position { get; set; }
            public IconType TypeInfo { get; set; }
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            if (header.ToUInt32 (0) != Signature)
                return null;

            uint file_size = Binary.BigEndian (header.ToUInt32(4));
            if (file_size != file.Length)
                return null;

            // Scan all icons
            var icons = new List<IconEntry>();
            file.Position = 8;

            while (file.Position < file.Length - 8)
            {
                uint type = Binary.BigEndian (file.ReadUInt32());
                uint size = Binary.BigEndian (file.ReadUInt32());

                if (IconTypes.TryGetValue (type, out var icon_type))
                {
                    icons.Add (new IconEntry
                    {
                        Type = type,
                        Size = size,
                        Position = file.Position,
                        TypeInfo = icon_type
                    });
                }

                file.Seek (size - 8, SeekOrigin.Current);
            }

            if (icons.Count == 0)
                return null;

            // Find best icon (prefer PNG, then largest size)
            var bestIcon = icons
                .OrderByDescending (i => i.TypeInfo.Format == "PNG" ? 1 : 0)
                .ThenByDescending (i => i.TypeInfo.Width)
                .First();

            return new ImageMetaData
            {
                Width = (uint)bestIcon.TypeInfo.Width,
                Height = (uint)bestIcon.TypeInfo.Height,
                BPP = 32
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var icons = new List<IconEntry>();
            file.Position = 8;

            while (file.Position < file.Length - 8)
            {
                uint type = Binary.BigEndian (file.ReadUInt32());
                uint size = Binary.BigEndian (file.ReadUInt32());

                if (IconTypes.TryGetValue(type, out var icon_type))
                {
                    icons.Add (new IconEntry
                    {
                        Type = type,
                        Size = size,
                        Position = file.Position,
                        TypeInfo = icon_type
                    });
                }

                file.Seek(size - 8, SeekOrigin.Current);
            }

            var targetIcon = icons.FirstOrDefault (i => i.TypeInfo.Width == info.Width && i.TypeInfo.Height == info.Height);
            if (targetIcon == null)
                targetIcon = icons.OrderByDescending (i => i.TypeInfo.Width).First();

            file.Position = targetIcon.Position;
            var data = file.ReadBytes ((int)(targetIcon.Size - 8));

            switch (targetIcon.TypeInfo.Format)
            {
                case "PNG":
                case "MIXED": // Could be PNG or JPEG 2000
                    using (var bms_data = new BinMemoryStream(data))
                    {
                        var formats = FormatCatalog.Instance.LookupSignature<ImageFormat>(bms_data.Signature);
                        foreach (var format in formats)
                        {
                            try
                            {
                                bms_data.Position = 0;
                                var result = format.Read (bms_data, info);
                                if (result != null)
                                    return result;
                            }
                            catch
                            {
                                if (formats.Count<ImageFormat>() == 1)
                                    throw;
                                continue;
                            }
                        }
    
                        throw new InvalidFormatException ("Unsupported MIXED icon format");
                    }
                case "RGB":
                    return ReadCompressedRGB (data, targetIcon.TypeInfo, targetIcon.TypeInfo.HasHeader);

                case "ARGB":
                    return ReadCompressedARGB (data, targetIcon.TypeInfo);
            }

            throw new InvalidFormatException ($"Unknown icon format: 0x{targetIcon.Type:X}");
        }

        private ImageData ReadCompressedRGB (byte[] data, IconType iconType, bool hasHeader)
        {
            int offset = hasHeader ? 4 : 0; // Skip 4-byte header for it32
            int pixelCount = iconType.Width * iconType.Height;

            var r = DecompressChannel (data, ref offset, pixelCount);
            var g = DecompressChannel (data, ref offset, pixelCount);
            var b = DecompressChannel (data, ref offset, pixelCount);

            var pixels = new byte[pixelCount * 4];
            for (int i = 0; i < pixelCount; i++)
            {
                pixels[i * 4 + 0] = b[i]; // B
                pixels[i * 4 + 1] = g[i]; // G
                pixels[i * 4 + 2] = r[i]; // R
                pixels[i * 4 + 3] = 255;  // A (opaque)
            }

            return ImageData.Create (new ImageMetaData
            {
                Width = (uint)iconType.Width,
                Height = (uint)iconType.Height,
                BPP = 32
            }, PixelFormats.Bgra32, null, pixels);
        }

        private ImageData ReadCompressedARGB (byte[] data, IconType iconType)
        {
            // Skip 'ARGB' header
            int offset = 4;
            int pixelCount = iconType.Width * iconType.Height;

            var a = DecompressChannel (data, ref offset, pixelCount);
            var r = DecompressChannel (data, ref offset, pixelCount);
            var g = DecompressChannel (data, ref offset, pixelCount);
            var b = DecompressChannel (data, ref offset, pixelCount);

            var pixels = new byte[pixelCount * 4];
            for (int i = 0; i < pixelCount; i++)
            {
                pixels[i * 4 + 0] = b[i]; // B
                pixels[i * 4 + 1] = g[i]; // G
                pixels[i * 4 + 2] = r[i]; // R
                pixels[i * 4 + 3] = a[i]; // A
            }

            return ImageData.Create (new ImageMetaData
            {
                Width = (uint)iconType.Width,
                Height = (uint)iconType.Height,
                BPP = 32
            }, PixelFormats.Bgra32, null, pixels);
        }

        private byte[] DecompressChannel (byte[] data, ref int offset, int pixelCount)
        {
            var output = new byte[pixelCount];
            int outputIndex = 0;

            while (outputIndex < pixelCount && offset < data.Length)
            {
                byte control = data[offset++];

                if (control < 0x80)
                {
                    // Copy next (control + 1) bytes
                    int count = control + 1;
                    for (int i = 0; i < count && outputIndex < pixelCount && offset < data.Length; i++)
                    {
                        output[outputIndex++] = data[offset++];
                    }
                }
                else
                {
                    // Repeat next byte (control - 0x80 + 3) times
                    int count = control - 0x80 + 3;
                    if (offset < data.Length)
                    {
                        byte value = data[offset++];
                        for (int i = 0; i < count && outputIndex < pixelCount; i++)
                        {
                            output[outputIndex++] = value;
                        }
                    }
                }
            }

            return output;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("IcnsFormat.Write not implemented");
        }
    }
}