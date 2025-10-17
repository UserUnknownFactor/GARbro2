using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Liar
{
    internal class LimMetaData : ImageMetaData
    {
        public int Flags;
    }

    [Export(typeof(ImageFormat))]
    public class LimFormat : ImageFormat
    {
        public override string         Tag { get { return "LIM"; } }
        public override string Description { get { return "Liar-soft image format"; } }
        public override uint     Signature { get { return  0; } }
        public override bool      CanWrite { get { return  false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (0x4C != file.ReadByte() || 0x4D != file.ReadByte())
                return null;
            int flag = file.ReadUInt16();
            if ((flag & 0xF) != 2 && (flag & 0xF) != 3)
                return null;
            int bpp = 0x10 == file.ReadUInt16() ? 16 : 32;
            var meta = new LimMetaData { BPP = bpp, Flags = flag };
            file.ReadUInt16();
            meta.Width  = file.ReadUInt32();
            meta.Height = file.ReadUInt32();
            return meta;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Reader (file, (LimMetaData)info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            var writer = new Writer (file, image);
            writer.Pack();
        }

        internal class Writer
        {
            Stream          m_output;
            ImageData       m_image;
            byte[]          m_pixels;
            int             m_width;
            int             m_height;
            int             m_bpp;
            int             m_stride;

            public Writer (Stream output, ImageData image)
            {
                m_output = output;
                m_image = image;
                m_width = (int)image.Width;
                m_height = (int)image.Height;

                var source = image.Bitmap;

                // Determine output format based on source
                if (source.Format == PixelFormats.Bgra32 || 
                    source.Format == PixelFormats.Bgr32 ||
                    source.Format.BitsPerPixel == 32)
                {
                    m_bpp = 32;
                    m_stride = m_width * 4;
                    m_pixels = new byte[m_stride * m_height];

                    if (source.Format != PixelFormats.Bgra32)
                    {
                        source = new FormatConvertedBitmap (source, PixelFormats.Bgra32, null, 0);
                    }
                    source.CopyPixels (m_pixels, m_stride, 0);
                }
                else
                {
                    m_bpp = 16;
                    m_stride = m_width * 2;
                    m_pixels = new byte[m_stride * m_height];

                    // Convert to BGR565
                    if (source.Format != PixelFormats.Bgr565)
                    {
                        // First convert to Bgr24 for easier processing
                        if (source.Format != PixelFormats.Bgr24)
                        {
                            source = new FormatConvertedBitmap (source, PixelFormats.Bgr24, null, 0);
                        }

                        var temp = new byte[m_width * m_height * 3];
                        source.CopyPixels (temp, m_width * 3, 0);
                        ConvertBgr24ToBgr565 (temp, m_pixels);
                    }
                    else
                    {
                        source.CopyPixels (m_pixels, m_stride, 0);
                    }
                }
            }

            public void Pack()
            {
                // Write header
                m_output.WriteByte (0x4C); // 'L'
                m_output.WriteByte (0x4D); // 'M'

                ushort flags = 3; // Base type
                if (m_bpp == 32)
                {
                    // 32-bit BGRA with compression
                    flags |= 0xE00; // Enable compression for all channels
                }
                else // 16-bit
                {
                    flags |= 0x10;  // Has 16-bit data
                    flags |= 0xE0;  // Enable compression

                    // Check if we have alpha channel to add
                    if (m_image.Bitmap.Format == PixelFormats.Bgra32 || 
                        m_image.Bitmap.Format == PixelFormats.Pbgra32)
                    {
                        flags |= 0x100; // Has alpha
                        flags |= 0xE00; // Compress alpha
                    }
                }

                var header = new byte[14];
                LittleEndian.Pack (flags, header, 0);
                LittleEndian.Pack((ushort)(m_bpp == 16 ? 0x10 : 0x20), header, 2);
                LittleEndian.Pack((ushort)0, header, 4); // Reserved
                LittleEndian.Pack((uint)m_width, header, 6);
                LittleEndian.Pack((uint)m_height, header, 10);

                m_output.Write (header, 0, header.Length);

                if (m_bpp == 32)
                {
                    Pack32bpp();
                }
                else
                {
                    Pack16bpp((flags & 0x100) != 0);
                }
            }

            void Pack32bpp()
            {
                byte[] channel = new byte[m_width * m_height];

                // Pack each channel (BGRA order, with XOR mask)
                byte mask = 0xFF;
                for (int i = 3; i >= 0; --i)
                {
                    // Extract channel
                    int src = i;
                    for (int p = 0; p < channel.Length; p++)
                    {
                        channel[p] = (byte)(m_pixels[src] ^ mask);
                        src += 4;
                    }
                    mask = 0;

                    // Compress and write channel
                    PackChannel (channel, 3);
                }
            }

            void Pack16bpp (bool hasAlpha)
            {
                // Pack 16-bit color data
                using (var ms = new MemoryStream())
                {
                    PackColorData (ms);

                    var compressedData = ms.ToArray();
                    m_output.Write (compressedData, 0, compressedData.Length);
                }

                // Pack alpha channel if present
                if (hasAlpha)
                {
                    var alpha = ExtractAlphaChannel();
                    PackChannel (alpha, 3);
                }
            }

            void PackColorData (Stream output)
            {
                // Build color index
                var colorIndex = new Dictionary<ushort, int>();
                var indexColors = new List<ushort>();

                for (int i = 0; i < m_pixels.Length; i += 2)
                {
                    ushort color = LittleEndian.ToUInt16 (m_pixels, i);
                    if (!colorIndex.ContainsKey (color))
                    {
                        colorIndex[color] = indexColors.Count;
                        indexColors.Add (color);
                    }
                }

                // Determine compression parameters
                int card = indexColors.Count > 4096 ? 4 : 3;

                // Calculate compressed size
                var counter = new BitCounter();
                for (int i = 0; i < m_pixels.Length; i += 2)
                {
                    ushort color = LittleEndian.ToUInt16 (m_pixels, i);
                    int index = colorIndex[color];

                    // Check for runs
                    int runLength = 1;
                    for (int j = i + 2; j < m_pixels.Length && runLength < 17; j += 2)
                    {
                        ushort nextColor = LittleEndian.ToUInt16 (m_pixels, j);
                        if (nextColor != color)
                            break;
                        runLength++;
                    }

                    if (runLength >= 2)
                    {
                        counter.AddBits (card); // 0 marker
                        counter.AddBits (4);    // run length
                        counter.AddBits (card); // color bits
                        counter.AddIndexBits (index, card);
                        i += (runLength - 1) * 2;
                    }
                    else
                    {
                        counter.AddBits (card); // non-zero marker
                        counter.AddIndexBits (index, card);
                    }
                }

                // Write header
                int imageSize = m_pixels.Length;
                int compressedSize = (counter.BitCount + 7) / 8;

                var header = new byte[8];
                LittleEndian.Pack (imageSize, header, 0);
                LittleEndian.Pack (compressedSize, header, 4);
                output.Write (header, 0, 8);

                // Write index
                var indexHeader = new byte[4];
                LittleEndian.Pack((ushort)(indexColors.Count * 2), indexHeader, 0);
                LittleEndian.Pack((ushort)0, indexHeader, 2);
                output.Write (indexHeader, 0, 4);

                foreach (var color in indexColors)
                {
                    var colorBytes = new byte[2];
                    LittleEndian.Pack (color, colorBytes, 0);
                    output.Write (colorBytes, 0, 2);
                }

                // Write compressed data
                using (var bits = new BitWriter (output))
                {
                    for (int i = 0; i < m_pixels.Length; i += 2)
                    {
                        ushort color = LittleEndian.ToUInt16 (m_pixels, i);
                        int index = colorIndex[color];

                        // Check for runs
                        int runLength = 1;
                        for (int j = i + 2; j < m_pixels.Length && runLength < 17; j += 2)
                        {
                            ushort nextColor = LittleEndian.ToUInt16 (m_pixels, j);
                            if (nextColor != color)
                                break;
                            runLength++;
                        }

                        if (runLength >= 2)
                        {
                            bits.WriteBits (0, card);           // Run marker
                            bits.WriteBits (runLength - 2, 4);  // Count
                            WriteIndex (bits, index, card);
                            i += (runLength - 1) * 2;
                        }
                        else
                        {
                            WriteIndex (bits, index, card);
                        }
                    }
                    bits.Flush();
                }
            }

            void PackChannel (byte[] channel, int card)
            {
                // Build byte index
                var byteIndex = new Dictionary<byte, int>();
                var indexBytes = new List<byte>();

                foreach (byte b in channel)
                {
                    if (!byteIndex.ContainsKey (b))
                    {
                        byteIndex[b] = indexBytes.Count;
                        indexBytes.Add (b);
                    }
                }

                // Calculate compressed size
                var counter = new BitCounter();
                for (int i = 0; i < channel.Length; i++)
                {
                    byte value = channel[i];
                    int index = byteIndex[value];

                    // Check for runs
                    int runLength = 1;
                    for (int j = i + 1; j < channel.Length && runLength < 17; j++)
                    {
                        if (channel[j] != value)
                            break;
                        runLength++;
                    }

                    if (runLength >= 2)
                    {
                        counter.AddBits (card);  // 0 marker
                        counter.AddBits (4);     // run length
                        counter.AddBits (card);  // value bits
                        counter.AddIndexBits (index, card);
                        i += runLength - 1;
                    }
                    else
                    {
                        counter.AddBits (card);  // non-zero marker
                        counter.AddIndexBits (index, card);
                    }
                }

                // Write channel header
                var header = new byte[8];
                LittleEndian.Pack (channel.Length, header, 0);
                int compressedSize = (counter.BitCount + 7) / 8;
                LittleEndian.Pack (compressedSize, header, 4);
                m_output.Write (header, 0, 8);

                // Write index
                var indexHeader = new byte[4];
                LittleEndian.Pack((ushort)indexBytes.Count, indexHeader, 0);
                LittleEndian.Pack((ushort)0, indexHeader, 2);
                m_output.Write (indexHeader, 0, 4);
                m_output.Write (indexBytes.ToArray(), 0, indexBytes.Count);

                // Write compressed data
                using (var bits = new BitWriter (m_output))
                {
                    for (int i = 0; i < channel.Length; i++)
                    {
                        byte value = channel[i];
                        int index = byteIndex[value];

                        // Check for runs
                        int runLength = 1;
                        for (int j = i + 1; j < channel.Length && runLength < 17; j++)
                        {
                            if (channel[j] != value)
                                break;
                            runLength++;
                        }

                        if (runLength >= 2)
                        {
                            bits.WriteBits (0, card);           // Run marker
                            bits.WriteBits (runLength - 2, 4);  // Count
                            WriteIndex (bits, index, card);
                            i += runLength - 1;
                        }
                        else
                        {
                            WriteIndex (bits, index, card);
                        }
                    }
                    bits.Flush();
                }
            }

            void WriteIndex (BitWriter bits, int index, int card)
            {
                int threshold = (card == 4) ? 14 : 6;
                int limit = (card == 4) ? 16 : 12;

                if (index < 2)
                {
                    bits.WriteBits (1, card);
                    bits.WriteBits (index, 1);
                }
                else
                {
                    // Find the right bit length for this index
                    int bitLength = 1;
                    int maxValue = 2;

                    while (index >= maxValue && bitLength < limit)
                    {
                        bitLength++;
                        maxValue = (1 << bitLength);
                    }

                    if (bitLength <= threshold)
                    {
                        bits.WriteBits (bitLength, card);
                        if (bitLength > 1)
                            bits.WriteBits (index - (1 << (bitLength - 1)), bitLength - 1);
                    }
                    else
                    {
                        // Use extended encoding
                        for (int i = threshold; i < bitLength - 1; i++)
                        {
                            bits.WriteBits (1, 1);
                        }
                        bits.WriteBits (0, 1);
                        bits.WriteBits (index - (1 << (bitLength - 1)), bitLength - 1);
                    }
                }
            }

            byte[] ExtractAlphaChannel()
            {
                var source = m_image.Bitmap;
                if (source.Format != PixelFormats.Bgra32)
                {
                    source = new FormatConvertedBitmap (source, PixelFormats.Bgra32, null, 0);
                }

                var bgra = new byte[m_width * m_height * 4];
                source.CopyPixels (bgra, m_width * 4, 0);

                var alpha = new byte[m_width * m_height];
                for (int i = 0, j = 3; i < alpha.Length; i++, j += 4)
                {
                    alpha[i] = (byte)~bgra[j]; // Inverted alpha
                }
                return alpha;
            }

            void ConvertBgr24ToBgr565 (byte[] source, byte[] dest)
            {
                int src = 0, dst = 0;
                for (int i = 0; i < m_width * m_height; i++)
                {
                    byte b = source[src++];
                    byte g = source[src++];
                    byte r = source[src++];

                    ushort color = (ushort)(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));
                    LittleEndian.Pack (color, dest, dst);
                    dst += 2;
                }
            }

            class BitWriter : IDisposable
            {
                Stream m_stream;
                int m_bits;
                int m_bitCount;

                public BitWriter (Stream stream)
                {
                    m_stream = stream;
                    m_bits = 0;
                    m_bitCount = 0;
                }

                public void WriteBits (int value, int count)
                {
                    while (count > 0)
                    {
                        m_bits >>= 1;
                        if ((value & (1 << (count - 1))) != 0)
                            m_bits |= 0x80;
                        m_bitCount++;
                        count--;

                        if (m_bitCount == 8)
                        {
                            m_stream.WriteByte((byte)m_bits);
                            m_bits = 0;
                            m_bitCount = 0;
                        }
                    }
                }

                public void Flush()
                {
                    if (m_bitCount > 0)
                    {
                        m_bits >>= (8 - m_bitCount);
                        m_stream.WriteByte((byte)m_bits);
                        m_bits = 0;
                        m_bitCount = 0;
                    }
                }

                public void Dispose()
                {
                    Flush();
                }
            }

            class BitCounter
            {
                public int BitCount { get; private set; }

                public void AddBits (int count)
                {
                    BitCount += count;
                }

                public void AddIndexBits (int index, int card)
                {
                    int threshold = (card == 4) ? 14 : 6;
                    int limit = (card == 4) ? 16 : 12;

                    if (index < 2)
                    {
                        BitCount += card + 1;
                    }
                    else
                    {
                        int bitLength = 1;
                        int maxValue = 2;

                        while (index >= maxValue && bitLength < limit)
                        {
                            bitLength++;
                            maxValue = (1 << bitLength);
                        }

                        if (bitLength <= threshold)
                        {
                            BitCount += card + bitLength - 1;
                        }
                        else
                        {
                            BitCount += (bitLength - threshold) + bitLength;
                        }
                    }
                }
            }
        }

        internal class Reader
        {
            IBinaryStream   m_input;
            byte[]          m_output;
            byte[]          m_index;
            byte[]          m_image;
            int             m_width;
            int             m_height;
            int             m_bpp;
            int             m_flags;

            public byte[]        Data { get { return m_image; } }
            public PixelFormat Format { get; private set; }

            public Reader (IBinaryStream file, LimMetaData info)
            {
                m_input = file;
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_bpp = info.BPP;
                m_flags = info.Flags;
                if (32 == m_bpp)
                    Format = PixelFormats.Bgra32;
                else if (16 == m_bpp)
                    Format = PixelFormats.Bgr565;
                else
                    throw new InvalidFormatException();
                m_image = new byte[m_width*m_height*m_bpp/8];
            }

            int         m_remaining;
            int         m_current;
            int         m_bits;

            public void Unpack ()
            {
                m_input.Position = 0x10;
                if (32 == m_bpp)
                {
                    Unpack32bpp();
                }
                else
                {
                    if (0 != (m_flags & 0x10))
                    {
                        if (0 != (m_flags & 0xE0))
                            Unpack16bpp();
                        else
                            m_input.Read (m_image, 0, m_image.Length);
                    }
                    if (0 != (m_flags & 0x100))
                    {
                        if (0 != (m_flags & 0xE00))
                        {
                            m_output = null;
                            UnpackChannel (3);
                        }
                        else
                            m_output = m_input.ReadBytes (m_width * m_height);
                        ApplyAlpha (m_output);
                    }
                }
            }

            void Unpack32bpp ()
            {
                byte mask = 0xFF;
                for (int i = 3; i >= 0; --i)
                {
                    UnpackChannel (3);
                    int src = 0;
                    for (int p = i; p < m_image.Length; p += 4)
                    {
                        m_image[p] = (byte)(m_output[src++] ^ mask);
                    }
                    mask = 0;
                }
            }

            void Unpack16bpp ()
            {
                int image_size = m_input.ReadInt32();
                m_output = m_image;

                m_remaining = m_input.ReadInt32();
                int index_size = m_input.ReadUInt16() * 2;
                if (null == m_index || index_size > m_index.Length)
                    m_index = new byte[index_size];
                m_input.ReadInt16(); // ignored
                if (index_size != m_input.Read (m_index, 0, index_size))
                    throw new InvalidFormatException ("Unexpected end of file");

                int card;
                if (index_size > 8192)
                {
                    m_index_threshold = 14;
                    m_index_length_limit = 16;
                    card = 4;
                }
                else
                {
                    m_index_threshold = 6;
                    m_index_length_limit = 12;
                    card = 3;
                }
                m_current = 0;
                int dst = 0;
                while (dst < m_output.Length)
                {
                    int bits = GetBits (card);
                    if (-1 == bits)
                        break;

                    if (0 != bits)
                    {
                        int index = GetIndex (bits);
                        if (index < 0)
                            break;
                        if (dst + 1 >= m_output.Length)
                            break;

                        m_output[dst++] = m_index[index*2];
                        m_output[dst++] = m_index[index*2+1];
                    }
                    else
                    {
                        int count = GetBits (4);
                        if (-1 == count)
                            break;

                        bits = GetBits (card);
                        if (-1 == bits)
                            break;

                        int index = GetIndex (bits);
                        if (-1 == index)
                            break;
                        count += 2;
                        index *= 2;
                        for (int i = 0; i < count; i++)
                        {
                            if (dst + 1 >= m_output.Length)
                                return;
                            m_output[dst++] = m_index[index];
                            m_output[dst++] = m_index[index+1];
                        }
                    }
                }
            }

            void ApplyAlpha (byte[] alpha)
            {
                var pixels = new byte[m_width*m_height*4];
                int alpha_src = 0;
                int dst = 0;
                for (int i = 0; i < m_image.Length; i += 2)
                {
                    int color = LittleEndian.ToUInt16 (m_image, i);
                    pixels[dst++] = (byte)((color & 0x001F) * 0xFF / 0x1F);
                    pixels[dst++] = (byte)((color & 0x07E0) * 0xFF / 0x7E0);
                    pixels[dst++] = (byte)((color & 0xF800) * 0xFF / 0xF800);
                    pixels[dst++] = (byte)~alpha[alpha_src++];
                }
                m_image = pixels;
                Format = PixelFormats.Bgra32;
            }

            void UnpackChannel (int card)
            {
                m_index_threshold = 6;
                m_index_length_limit = 12;

                int channel_size = m_input.ReadInt32();
                if (null == m_output || m_output.Length < channel_size)
                    m_output = new byte[channel_size];
                m_remaining = m_input.ReadInt32();

                int index_size = m_input.ReadUInt16();
                if (null == m_index || index_size > m_index.Length)
                    m_index = new byte[index_size];
                m_input.ReadInt16(); // ignored
                if (index_size != m_input.Read (m_index, 0, index_size))
                    throw new InvalidFormatException ("Unexpected end of file");

                m_current = 0;
                int dst = 0;
                while (dst < m_output.Length)
                {
                    int bits = GetBits (card);
                    if (-1 == bits)
                        break;

                    if (0 != bits)
                    {
                        int index = GetIndex (bits);
                        if (index < 0)
                            break;
                        if (dst + 1 >= m_output.Length)
                            break;

                        m_output[dst++] = m_index[index];
                    }
                    else
                    {
                        int count = GetBits (4);
                        if (-1 == count)
                            break;

                        bits = GetBits (card);
                        if (-1 == bits)
                            break;

                        int index = GetIndex (bits);
                        if (-1 == index)
                            break;
                        count += 2;
                        for (int i = 0; i < count; i++)
                        {
                            if (dst >= m_output.Length)
                                return;
                            m_output[dst++] = m_index[index];
                        }
                    }
                }
            }

            private int GetBits (int n)
            {
                int v = 0;
                while (n > 0)
                {
                    if (0 == m_current)
                    {
                        if (0 == m_remaining)
                            return 0;
                        m_bits = m_input.ReadByte();
                        --m_remaining;
                        m_current = 8;
                    }
                    v <<= 1;
                    m_bits <<= 1;
                    v |= (m_bits >> 8) & 1;
                    --m_current;
                    --n;
                }
                return v;
            }

            int m_index_threshold;
            int m_index_length_limit;

            private int GetIndex (int bits)
            {
                if (bits <= m_index_threshold)
                {
                    if (0 == bits)
                        return -1;
                    if (1 == bits--)
                        return GetBits (1);
                    return (1 << bits) | GetBits (bits);
                }
                for (int i = m_index_threshold; i < m_index_length_limit; ++i)
                {
                    bits = GetBits (1);
                    if (-1 == bits)
                        return -1;
                    if (0 == bits)
                        return (1 << i) | GetBits (i);
                }
                return -1;
            }
        }
    }
}
