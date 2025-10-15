using GameRes.Compression;
using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Silky
{
    internal class AkbMetaData : ImageMetaData
    {
        public byte[]   Background;
        public int      InnerWidth;
        public int      InnerHeight;
        public uint     Flags;
        public string   BaseFileName;
        public uint     DataOffset;

        public bool IsIncremental => !string.IsNullOrEmpty (BaseFileName);

        public override string GetComment ()
        {
            var comment = base.GetComment();
            if (!string.IsNullOrEmpty (BaseFileName))
                comment += Localization.Format ("BaseImage", Path.GetFileName (BaseFileName));

            if (InnerWidth != Width || InnerHeight != Height)
                comment += $" ({InnerWidth}x{InnerHeight} @ {OffsetX},{OffsetY})";

            if (Background != null && LittleEndian.ToInt32 (Background, 0) != 0)
            {
                uint bgColor = LittleEndian.ToUInt32 (Background, 0);
                comment += $" (BG: #{bgColor:X8})";
            }

            return comment;
        }

        public byte[] Serialize ()
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter (ms))
            {
                SerializeCore (writer);
                return ms.ToArray();
            }
        }

        private void SerializeCore (BinaryWriter writer)
        {
            writer.Write (Flags);
            writer.Write (Background ?? new byte[4]);
            writer.Write (OffsetX);
            writer.Write (OffsetY);
            writer.Write (InnerWidth);
            writer.Write (InnerHeight);

            if (!string.IsNullOrEmpty (BaseFileName))
            {
                var nameBytes = Encoding.UTF8.GetBytes (BaseFileName);
                writer.Write (nameBytes.Length);
                writer.Write (nameBytes);
            }
            else
                writer.Write (0);
        }

        public static AkbMetaData Deserialize (byte[] data, uint width, uint height)
        {
            using (var ms = new MemoryStream (data))
            using (var reader = new BinaryReader (ms))
            {
                return DeserializeCore (reader, width, height);
            }
        }

        private static AkbMetaData DeserializeCore (BinaryReader reader, uint width, uint height)
        {
            var meta = new AkbMetaData
            {
                Width = width,
                Height = height
            };

            meta.Flags       = reader.ReadUInt32();
            meta.Background  = reader.ReadBytes (4);
            meta.OffsetX     = reader.ReadInt32();
            meta.OffsetY     = reader.ReadInt32();
            meta.InnerWidth  = reader.ReadInt32();
            meta.InnerHeight = reader.ReadInt32();

            int nameLength = reader.ReadInt32();
            if (nameLength > 0)
            {
                var nameBytes = reader.ReadBytes (nameLength);
                meta.BaseFileName = Encoding.UTF8.GetString (nameBytes);
            }

            meta.BPP = 0 == (meta.Flags & 0x40000000) ? 32 : 24;

            return meta;
        }

        public void WriteHeader (Stream stream)
        {
            using (var writer = new BinaryWriter (stream, Encoding.ASCII, true))
            {
                uint signature = IsIncremental ? 0x2B424B41u : 0x20424B41u; // 'AKB+' or 'AKB '
                writer.Write (signature);

                writer.Write ((ushort)Width);
                writer.Write ((ushort)Height);

                writer.Write (Flags);
                writer.Write (Background ?? new byte[4]);

                writer.Write (OffsetX);
                writer.Write (OffsetY);
                writer.Write (OffsetX + InnerWidth);  // Right
                writer.Write (OffsetY + InnerHeight); // Bottom

                if (IsIncremental)
                {
                    var baseNameBytes = new byte[0x20];
                    if (!string.IsNullOrEmpty (BaseFileName))
                    {
                        // NOTE: AKB format uses ASCII for filenames in header
                        var nameBytes = Encoding.ASCII.GetBytes (Path.GetFileName (BaseFileName));
                        Buffer.BlockCopy (nameBytes, 0, baseNameBytes, 0, Math.Min (nameBytes.Length, 0x20));
                    }
                    writer.Write (baseNameBytes);
                }
            }
        }

        public static AkbMetaData ReadHeader (IBinaryStream file)
        {
            var info = new AkbMetaData();
            uint signature = file.ReadUInt32();
            bool is_incremental = '+' == (signature >> 24);

            info.Width      = file.ReadUInt16();
            info.Height     = file.ReadUInt16();
            info.Flags      = file.ReadUInt32();
            info.BPP        = 0 == (info.Flags & 0x40000000) ? 32 : 24;
            info.Background = file.ReadBytes (4);
            info.OffsetX    = file.ReadInt32();
            info.OffsetY    = file.ReadInt32();

            if (info.Width == 0 || info.Height == 0 || 
                info.Width > 20000 || info.Height > 20000)
                return null;

            // NOTE: Format stores right/bottom, we need width/height
            int right = file.ReadInt32();
            int bottom = file.ReadInt32();
            info.InnerWidth = right - info.OffsetX;
            info.InnerHeight = bottom - info.OffsetY;

            if (info.InnerWidth < 0 || info.InnerHeight < 0 || 
                info.InnerWidth > info.Width || info.InnerHeight > info.Height)
                return null;

            if (is_incremental)
                info.BaseFileName = file.ReadCString (0x20);

            info.DataOffset = (uint)file.Position;
            return info;
        }

        public static AkbMetaData CreateDefault (ImageData image)
        {
            return new AkbMetaData
            {
                Width        = image.Width,
                Height       = image.Height,
                BPP          = 32,
                Flags        = 0,
                Background   = new byte[4],
                OffsetX      = 0,
                OffsetY      = 0,
                InnerWidth   = (int)image.Width,
                InnerHeight  = (int)image.Height,
                BaseFileName = null,
                DataOffset   = 0x1C // for non-incremental
            };
        }
    }

    [Export(typeof(ImageFormat))]
    public partial class AkbFormat : ImageFormat
    {
        public override string         Tag { get { return "AKB"; } }
        public override string Description { get { return "AI6WIN engine image format"; } }
        public override uint     Signature { get { return  0x20424B41; } } // 'AKB '
        public override bool      CanWrite { get { return  true; } }

        public AkbFormat ()
        {
            Signatures = new uint[] { 0x20424B41, 0x2B424B41 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            return AkbMetaData.ReadHeader (file);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (AkbMetaData)info;
            byte[] background = null;
            if (!string.IsNullOrEmpty (meta.BaseFileName))
                background = ReadBaseImage (meta.BaseFileName, meta);

            var reader = new AkbReader (file.AsStream, meta);
            var image = reader.Unpack (background);
            var imageData = ImageData.Create (info, reader.Format, null, image, reader.Stride);

            var pngImage = imageData as PngImageData ?? new PngImageData (imageData.Bitmap, meta);
            pngImage.CustomChunks["aKBm"] = meta.Serialize();
            return pngImage;
        }

        byte[] ReadBaseImage (string filename, AkbMetaData overlay_info)
        {
            var pattern = Path.GetFileNameWithoutExtension (filename) + ".*";
            pattern = VFS.CombinePath (VFS.GetDirectoryName (filename), pattern);
            foreach (var entry in VFS.GetFiles (pattern))
            {
                if (entry.Name == overlay_info.FileName)
                    continue;
                using (var base_file = VFS.OpenBinaryStream (entry))
                {
                    var base_info = ReadMetaData (base_file) as AkbMetaData;
                    if (null != base_info && base_info.BPP == overlay_info.BPP
                        && base_info.Width == overlay_info.Width && base_info.Height == overlay_info.Height)
                    {
                        var reader = new AkbReader (base_file.AsStream, base_info);
                        return reader.Unpack();
                    }
                }
            }
            return null;
        }
    }

    internal class AkbReader
    {
        Stream          m_input;
        AkbMetaData     m_info;
        int             m_pixel_size;

        public PixelFormat Format { get; private set; }
        public int         Stride { get; private set; }

        public AkbReader (Stream input, AkbMetaData info)
        {
            m_input = input;
            m_info = info;
            m_pixel_size = m_info.BPP / 8;
            Stride = (int)m_info.Width * m_pixel_size;
            if (24 == m_info.BPP)
                Format = PixelFormats.Bgr24;
            else if (0 == (m_info.Flags & 0x80000000))
                Format = PixelFormats.Bgr32;
            else
                Format = PixelFormats.Bgra32;
        }

        public byte[] Unpack (byte[] background = null)
        {
            if (0 == m_info.InnerWidth || 0 == m_info.InnerHeight)
                return background ?? CreateBackground();

            m_input.Position = m_info.DataOffset;
            int inner_stride = m_info.InnerWidth * m_pixel_size;
            var pixels = new byte[m_info.InnerHeight * inner_stride];
            using (var lz = new LzssStream (m_input, LzssMode.Decompress, true))
            {
                for (int pos = pixels.Length - inner_stride; pos >= 0; pos -= inner_stride)
                {
                    if (inner_stride != lz.Read (pixels, pos, inner_stride))
                        throw new InvalidFormatException();
                }
            }

            //File.WriteAllBytes("decompressed_before_delta.bin", pixels);
            RestoreDelta (pixels, inner_stride);
            //File.WriteAllBytes("decompressed_after_delta.bin", pixels);

            if (m_info.IsIncremental && null == background)
                return ConvertMagicGreenToTransparent (pixels);

            if (null == background && m_info.InnerWidth == m_info.Width && m_info.InnerHeight == m_info.Height)
                return pixels;

            var image = background ?? CreateBackground();
            int src = 0;
            int dst = m_info.OffsetY * Stride + m_info.OffsetX * m_pixel_size;
            Action blend_row;
            if (null == background)
            {
                blend_row = () => Buffer.BlockCopy (pixels, src, image, dst, inner_stride);
            }
            else
            {
                blend_row = () => {
                    for (int x = 0; x < inner_stride; x += m_pixel_size)
                    {
                        // Check for magic green (BGR format: B=0x00, G=0xFF, R=0x00)
                        if (0x00 != pixels[src+x] || 0xFF != pixels[src+x+1] || 0x00 != pixels[src+x+2])
                        {
                            for (int i = 0; i < m_pixel_size; ++i)
                                image[dst+x+i] = pixels[src+x+i];
                        }
                        // If it is, base image pixels remain unchanged
                    }
                };
            }
            for (int y = 0; y < m_info.InnerHeight; ++y)
            {
                blend_row();
                dst += Stride;
                src += inner_stride;
            }
            return image;
        }

        private byte[] ConvertMagicGreenToTransparent (byte[] pixels)
        {
            // Convert to BGRA32 with alpha for magic green pixels
            int src_stride = m_info.InnerWidth * m_pixel_size;
            int dst_stride = (int)m_info.Width * 4;
            var result = new byte[(int)m_info.Height * dst_stride];

            // Fill with transparent black initially
            for (int y = 0; y < m_info.InnerHeight; ++y)
            for (int x = 0; x < m_info.InnerWidth; ++x)
            {
                int src_offset = y * src_stride + x * m_pixel_size;
                int dst_offset = (y + m_info.OffsetY) * dst_stride + (x + m_info.OffsetX) * 4;

                // Check for magic green
                if (pixels[src_offset    ] == 0x00 && 
                    pixels[src_offset + 1] == 0xFF && 
                    pixels[src_offset + 2] == 0x00)
                {
                    // Make transparent
                    result[dst_offset    ] = 0; // B
                    result[dst_offset + 1] = 0; // G
                    result[dst_offset + 2] = 0; // R
                    result[dst_offset + 3] = 0; // A (transparent)
                }
                else
                {
                    // Copy pixel data
                    result[dst_offset    ] = pixels[src_offset    ]; // B
                    result[dst_offset + 1] = pixels[src_offset + 1]; // G
                    result[dst_offset + 2] = pixels[src_offset + 2]; // R
                    result[dst_offset + 3] = 255; // A (opaque)

                    if (m_pixel_size == 4)
                        result[dst_offset + 3] = pixels[src_offset + 3];
                }
            }

            // Update format to include alpha
            Format = PixelFormats.Bgra32;
            Stride = dst_stride;
            m_info.BPP = 32;
            m_info.Flags |= 0x80000000; // Set alpha flag

            return result;
        }

        private void RestoreDelta (byte[] pixels, int stride)
        {
            int src = 0;
            // First loop: restore horizontal delta within first row
            for (int i = m_pixel_size; i < stride; ++i)
                pixels[i] += pixels[src++];

            src = 0;
            // Second loop: restore vertical delta for remaining pixels
            for (int i = stride; i < pixels.Length; ++i)
                pixels[i] += pixels[src++];
        }

        private byte[] CreateBackground ()
        {
            var pixels = new byte[Stride * (int)m_info.Height];
            if (0 != LittleEndian.ToInt32 (m_info.Background, 0))
            {
                for (int i = 0; i < Stride; i += m_pixel_size)
                    Buffer.BlockCopy (m_info.Background, 0, pixels, i, m_pixel_size);
                Binary.CopyOverlapped (pixels, 0, Stride, pixels.Length-Stride);
            }
            return pixels;
        }
    }
}
