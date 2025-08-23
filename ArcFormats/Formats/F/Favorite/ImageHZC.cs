using GameRes.Compression;
using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.FVP
{
    internal class HzcMetaData : ImageMetaData
    {
        public int  Type;
        public int  UnpackedSize;
        public int  HeaderSize;
        public int  ImageCount;
        public bool IsTlg;
        public long TlgOffset;
    }

    [Export(typeof(ImageFormat))]
    public class HzcFormat : ImageFormat
    {
        public override string         Tag { get { return "HZC"; } }
        public override string Description { get { return "Favorite View Point image format"; } }
        public override uint     Signature { get { return  0x31637A68; } } // 'hzc1'

        public HzcFormat ()
        {
            Extensions = new string[] { "hzc" };
        }

        static byte[] TLG_WRAPPER = new byte[] {
            (byte)'T', (byte)'L', (byte)'G', (byte)'0', (byte)'.', (byte)'0',
            0x00, (byte)'s', (byte)'d', (byte)'s', 0x1A
        };

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x30);
            if (header.AsciiEqual (0, "hzc1") && header.AsciiEqual (0xC, "NVSG"))
            {
                int type         = header.ToUInt16 (0x12);
                uint width       = header.ToUInt16 (0x14);
                uint height      = header.ToUInt16 (0x16);
                int image_count  =  header.ToInt32 (0x20);

                if (type == 2)
                    height *= (uint)image_count;

                return new HzcMetaData
                {
                    Width   = width,
                    Height  = height,
                    OffsetX = header.ToInt16 (0x18),
                    OffsetY = header.ToInt16 (0x1A),
                    BPP     = (type == 0) ? 24 : (type == 3) ? 8 : 32,
                    Type    = type,
                    UnpackedSize = header.ToInt32 (4),
                    HeaderSize   = header.ToInt32 (8),
                    ImageCount   = image_count,
                    IsTlg = false
                };
            }

            stream.Position = 0x2C;
            byte first = stream.ReadUInt8();
            if (first != 'T')
                while (stream.ReadUInt8() != 0 && stream.Position < stream.Length);
            else
                stream.Position = 0x2C;

            long tlgOffset = stream.Position;

            Stream tlgStream;
            byte[] check = stream.ReadBytes(6);
            stream.Position = tlgOffset;

            if (check.Length >= 6 && check[0] == 'T' && check[1] == 'L' && check[2] == 'G' &&
                check[3] == '0' && check[4] == '.' && check[5] == '0')
                tlgStream = new StreamRegion (stream.AsStream, tlgOffset);
            else
                tlgStream = new PrefixStream (TLG_WRAPPER, new StreamRegion (stream.AsStream, tlgOffset));

            using (var tlgBinaryStream = new BinaryStream (tlgStream, stream.Name, true))
            {
                var tlg_format = ImageFormat.FindByTag ("TLG");
                if (tlg_format == null)
                    return null;

                var tlg_info = tlg_format.ReadMetaData (tlgBinaryStream);
                if (tlg_info == null)
                    return null;

                return new HzcMetaData
                {
                    Width     = tlg_info.Width,
                    Height    = tlg_info.Height,
                    BPP       = tlg_info.BPP,
                    OffsetX   = tlg_info.OffsetX,
                    OffsetY   = tlg_info.OffsetY,
                    IsTlg     = true,
                    TlgOffset = tlgOffset
                };
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (HzcMetaData)info;
            if (meta.IsTlg)
            {
                var tlg_format = ImageFormat.FindByTag ("TLG");
                if (tlg_format == null)
                    throw new NotSupportedException ("TLG format handler not found");

                stream.Position = meta.TlgOffset;

                byte[] check = stream.ReadBytes(6);
                stream.Position = meta.TlgOffset;

                Stream tlgStream;
                if (check.Length >= 6 && check[0] == 'T' && check[1] == 'L' && check[2] == 'G' &&
                    check[3] == '0' && check[4] == '.' && check[5] == '0')
                    tlgStream = new StreamRegion (stream.AsStream, meta.TlgOffset);
                else
                    tlgStream = new PrefixStream (TLG_WRAPPER, new StreamRegion (stream.AsStream, meta.TlgOffset));

                using (var tlgBinaryStream = new BinaryStream (tlgStream, stream.Name))
                {
                    return tlg_format.Read (tlgBinaryStream, info);
                }
            }
            else // NVSG format
            {
                stream.Position = 12 + meta.HeaderSize;
                using (var decoder = new HzcDecoder (stream, meta, true))
                    return decoder.Image;
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("HzcFormat.Write not implemented");
        }
    }

    internal sealed class HzcDecoder : IImageDecoder
    {
        HzcMetaData     m_info;
        ImageData       m_image;
        int             m_stride;
        long            m_frame_offset;
        int             m_frame_size;

        public Stream            Source { get; private set; }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get { return m_info; } }
        public PixelFormat       Format { get; private set; }
        public BitmapPalette    Palette { get; private set; }
        public ImageData Image
        {
            get
            {
                if (null == m_image)
                {
                    var pixels = ReadPixels();
                    m_image = ImageData.Create (Info, Format, Palette, pixels, m_stride);
                }
                return m_image;
            }
        }

        public HzcDecoder (IBinaryStream input, HzcMetaData info, Entry entry) : this (input, info)
        {
            m_frame_offset = entry.Offset;
            m_frame_size = (int)entry.Size;
        }

        public HzcDecoder (IBinaryStream input, HzcMetaData info, bool leave_open = false)
        {
            m_info = info;
            m_stride = (int)m_info.Width * m_info.BPP / 8;
            switch (m_info.Type)
            {
            default: throw new NotSupportedException();
            case 0: Format = PixelFormats.Bgr24; break;
            case 1:
            case 2: Format = PixelFormats.Bgra32; break;
            case 3: Format = PixelFormats.Gray8; break;
            case 4:
                {
                    Format = PixelFormats.Indexed8;
                    var colors = new Color[2] { Color.FromRgb (0,0,0), Color.FromRgb (0xFF,0xFF,0xFF) };
                    Palette = new BitmapPalette (colors);
                    break;
                }
            }
            Source = new ZLibStream (input.AsStream, CompressionMode.Decompress, leave_open);
            m_frame_offset = 0;
            m_frame_size = m_stride * (int)Info.Height;
        }

        byte[] ReadPixels ()
        {
            var pixels = new byte[m_frame_size];
            long offset = 0;
            for (;;)
            {
                if (pixels.Length != Source.Read (pixels, 0, pixels.Length))
                    throw new EndOfStreamException();
                if (offset >= m_frame_offset)
                    break;
                offset += m_frame_size;
            }

            if (m_info.Type <= 2)
                SwapRedBlueChannels(pixels);

            return pixels;
        }

        void SwapRedBlueChannels(byte[] pixels)
        {
            int pixel_size = m_info.BPP / 8;
            for (int i = 0; i < pixels.Length; i += pixel_size)
            {
                // Swap R and B channels (first and third bytes)
                byte temp = pixels[i];
                pixels[i    ] = pixels[i + 2];
                pixels[i + 2] = temp;
            }
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                Source.Dispose();
                m_disposed = true;
            }
            //GC.SuppressFinalize (this);
        }
    }
}