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
    }

    [Export(typeof(ImageFormat))]
    public class AkbFormat : ImageFormat
    {
        public override string         Tag { get { return "AKB"; } }
        public override string Description { get { return "AI6WIN engine image format"; } }
        public override uint     Signature { get { return 0x20424B41; } } // 'AKB '

        public AkbFormat ()
        {
            Signatures = new uint[] { 0x20424B41, 0x2B424b41 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var info = new AkbMetaData();
            uint signature = file.ReadUInt32();
            bool is_incremental = '+' == (signature >> 24);
            info.Width = file.ReadUInt16();
            info.Height = file.ReadUInt16();
            info.Flags = file.ReadUInt32();
            info.BPP = 0 == (info.Flags & 0x40000000) ? 32 : 24;
            info.Background = file.ReadBytes (4);
            info.OffsetX = file.ReadInt32();
            info.OffsetY = file.ReadInt32();
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

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (AkbMetaData)info;
            byte[] background = null;
            if (!string.IsNullOrEmpty (meta.BaseFileName))
            {
                background = ReadBaseImage (meta.BaseFileName, meta);
            }
            var reader = new AkbReader (file.AsStream, (AkbMetaData)info);
            var image = reader.Unpack (background);
            return ImageData.Create (info, reader.Format, null, image, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AkbFormat.Write not implemented");
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
                        // FIXME what if baseline image is incremental itself?
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
            RestoreDelta (pixels, inner_stride);
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
                        if (0x00 != pixels[src+x] || 0xFF != pixels[src+x+1] || 0x00 != pixels[src+x+2])
                        {
                            for (int i = 0; i < m_pixel_size; ++i)
                                image[dst+x+i] = pixels[src+x+i];
                        }
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

        private void RestoreDelta (byte[] pixels, int stride)
        {
            int src = 0;
            for (int i = m_pixel_size; i < stride; ++i)
                pixels[i] += pixels[src++];
            src = 0;
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
