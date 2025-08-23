using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Google
{
    internal class WebPMetaData : ImageMetaData
    {
        public WebPFeature  Flags;
        public bool         IsLossless;
        public bool         HasAlpha;
        public long         DataOffset;
        public int          DataSize;
        public long         AlphaOffset;
        public int          AlphaSize;
    }

    [Flags]
    internal enum WebPFeature : uint
    {
        Fragments  = 0x0001,
        Animation  = 0x0002,
        Xmp        = 0x0004,
        Exif       = 0x0008,
        Alpha      = 0x0010,
        Iccp       = 0x0020,
    }

    [Export(typeof(ImageFormat))]
    public class WebPFormat : ImageFormat
    {
        public override string         Tag { get { return "WEBP"; } }
        public override string Description { get { return "Google WebP image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            if (0x46464952 != stream.Signature) // 'RIFF'
                return null;
            if (!stream.ReadHeader (12).AsciiEqual (8, "WEBP"))
                return null;
            var header = new byte[0x10];
            bool found_vp8x = false;
            var info = new WebPMetaData { BPP = 32 };
            int chunk_size;
            for (;;)
            {
                if (8 != stream.Read (header, 0, 8))
                    return null;
                chunk_size = LittleEndian.ToInt32 (header, 4);
                int aligned_size = (chunk_size + 1) & ~1;
                if (!found_vp8x && Binary.AsciiEqual (header, 0, "VP8X"))
                {
                    found_vp8x = true;
                    if (chunk_size < 10)
                        return null;
                    if (chunk_size > header.Length)
                        header = new byte[chunk_size];
                    if (chunk_size != stream.Read (header, 0, chunk_size))
                        return null;
                    info.Flags = (WebPFeature)LittleEndian.ToUInt32 (header, 0);
                    info.Width  = 1 + (uint)header.ToInt24 (4);
                    info.Height = 1 + (uint)header.ToInt24 (7);
                    if ((long)info.Width * info.Height >= (1L << 32))
                        return null;
                    continue;
                }
                if (Binary.AsciiEqual (header, 0, "VP8 ") || Binary.AsciiEqual (header, 0, "VP8L"))
                {
                    info.IsLossless = header[3] == 'L';
                    info.DataOffset = stream.Position;
                    info.DataSize = aligned_size;
                    if (!found_vp8x)
                    {
                        if (chunk_size < 10 || 10 != stream.Read (header, 0, 10))
                            return null;
                        if (info.IsLossless)
                        {
                            if (header[0] != 0x2F || (header[4] >> 5) != 0)
                                return null;
                            uint wh = LittleEndian.ToUInt32 (header, 1);
                            info.Width  = (wh & 0x3FFFu) + 1;
                            info.Height = ((wh >> 14) & 0x3FFFu) + 1;
                            info.HasAlpha = 0 != (header[4] & 0x10);
                        }
                        else
                        {
                            if (header[3] != 0x9D || header[4] != 1 || header[5] != 0x2A)
                                return null;
                            if (0 != (header[0] & 1)) // not a keyframe
                                return null;
                            info.Width  = LittleEndian.ToUInt16 (header, 6) & 0x3FFFu;
                            info.Height = LittleEndian.ToUInt16 (header, 8) & 0x3FFFu;
                        }
                    }
                    break;
                }
                if (Binary.AsciiEqual (header, 0, "ALPH"))
                {
                    info.AlphaOffset = stream.Position;
                    info.AlphaSize   = chunk_size;
                }
                stream.Seek (aligned_size, SeekOrigin.Current);
            }
            if (0 == info.Width || 0 == info.Height)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = new WebPDecoder (stream, (WebPMetaData)info);
            reader.Decode();
            return ImageData.Create (info, reader.Format, null, reader.Output);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("WebPFormat.Write not implemented");
        }
    }
}
