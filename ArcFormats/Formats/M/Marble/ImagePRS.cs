using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Marble
{
    internal class PrsMetaData : ImageMetaData
    {
        public byte Flag;
        public uint PackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class PrsFormat : ImageFormat
    {
        public override string         Tag { get { return "PRS"; } }
        public override string Description { get { return "Marble engine image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x10);
            if (header[0] != 'Y' || header[1] != 'B')
                return null;
            int bpp = header[3];
            if (bpp != 3 && bpp != 4)
                return null;

            return new PrsMetaData
            {
                Width      = header.ToUInt16 (12),
                Height     = header.ToUInt16 (14),
                BPP        = 8 * bpp,
                Flag       = header[2],
                PackedSize = header.ToUInt32 (4),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new Reader (stream, (PrsMetaData)info))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Data, reader.Stride);
            }
        }

        internal class Reader : IDisposable
        {
            IBinaryStream   m_input;
            byte[]          m_output;
            uint            m_size;
            byte            m_flag;
            int             m_depth;

            public byte[]        Data { get { return m_output; } }
            public PixelFormat Format { get; private set; }
            public int         Stride { get; private set; }

            public Reader (IBinaryStream file, PrsMetaData info)
            {
                m_input = file;
                m_size = info.PackedSize;
                m_flag = info.Flag;
                m_depth = info.BPP / 8;
                if (3 == m_depth)
                    Format = PixelFormats.Bgr24;
                else
                    Format = PixelFormats.Bgra32;
                Stride = (int)info.Width * m_depth;
                m_output = new byte[Stride * (int)info.Height];
            }

            static readonly int[] LengthTable = InitLengthTable();

            private static int[] InitLengthTable ()
            {
                var length_table = new int[256];
                for (int i = 0; i < 0xfe; ++i)
                    length_table[i] = i + 3;
                length_table[0xfe] = 0x400;
                length_table[0xff] = 0x1000;
                return length_table;
            }

            public void Unpack ()
            {
                m_input.Position = 0x10;
                int dst = 0;
                int remaining = (int)m_size;
                int bit = 0;
                int ctl = 0;
                while (remaining > 0 && dst < m_output.Length)
                {
                    bit >>= 1;
                    if (0 == bit)
                    {
                        ctl = m_input.ReadUInt8();
                        --remaining;
                        bit = 0x80;
                    }
                    if (remaining <= 0)
                        break;
                    if (0 == (ctl & bit))
                    {
                        m_output[dst++] = m_input.ReadUInt8();
                        --remaining;
                        continue;
                    }
                    int b = m_input.ReadUInt8();
                    --remaining;
                    int length = 0;
                    int shift = 0;

                    if (0 != (b & 0x80))
                    {
                        if (remaining <= 0)
                            break;
                        shift = m_input.ReadUInt8();
                        --remaining;
                        shift |= (b & 0x3f) << 8;
                        if (0 != (b & 0x40))
                        {
                            if (remaining <= 0)
                                break;
                            int offset = m_input.ReadUInt8();
                            --remaining;
                            length = LengthTable[offset];
                        }
                        else
                        {
                            length = (shift & 0xf) + 3;
                            shift >>= 4;
                        }
                    }
                    else
                    {
                        length = b >> 2;
                        b &= 3;
                        if (3 == b)
                        {
                            length += 9;
                            //length = Math.Min(m_output.Length - dst, length);
                            int read = m_input.Read (m_output, dst, length);
                            if (read < length)
                                break;
                            remaining -= length;
                            dst += length;
                            continue;
                        }
                        shift = length;
                        length = b + 2;
                    }
                    ++shift;
                    if (dst < shift)
                        throw new InvalidFormatException ("Invalid offset value");
                    length = Math.Min (length, m_output.Length - dst);
                    Binary.CopyOverlapped (m_output, dst-shift, dst, length);
                    dst += length;
                }
                if ((m_flag & 0x80) != 0)
                {
                    for (int i = m_depth; i < m_output.Length; ++i)
                        m_output[i] += m_output[i-m_depth];
                }
                if (4 == m_depth && IsDummyAlphaChannel())
                    Format = PixelFormats.Bgr32;
            }

            bool IsDummyAlphaChannel ()
            {
                byte alpha = m_output[3];
                if (0xFF == alpha)
                    return false;
                for (int i = 7; i < m_output.Length; i += 4)
                    if (m_output[i] != alpha)
                        return false;
                return true;
            }

            #region IDisposable Members
            bool disposed = false;

            public void Dispose ()
            {
                Dispose (true);
                GC.SuppressFinalize (this);
            }

            protected virtual void Dispose (bool disposing)
            {
                if (!disposed)
                {
                    if (disposing)
                        m_input.Dispose();
                    disposed = true;
                }
            }
            #endregion
        }

        public override void Write(Stream file, ImageData image)
        {
            using (var writer = new PrsWriter(file, image))
            {
                writer.Pack();
            }
        }

        internal class PrsWriter : IDisposable
        {
            Stream m_output;
            ImageData m_image;
            byte[] m_input;
            int m_stride;
            int m_depth;
            byte m_flag;
            List<byte> m_packed;

            public PrsWriter(Stream output, ImageData image)
            {
                m_output = output;
                m_image = image;
                m_depth = image.BPP / 8;
                if (m_depth != 3 && m_depth != 4)
                    throw new InvalidFormatException("PRS format supports only 24 and 32 bit images");

                m_stride = (int)image.Width * m_depth;
                m_input = new byte[m_stride * (int)image.Height];
                m_packed = new List<byte>();
                m_flag = 0x80; // Enable delta filter by default

                // Convert image data to byte array
                var bitmap = image.Bitmap;
                if (bitmap.Format != PixelFormats.Bgr24 && bitmap.Format != PixelFormats.Bgra32)
                {
                    bitmap = new FormatConvertedBitmap(bitmap,
                        m_depth == 3 ? PixelFormats.Bgr24 : PixelFormats.Bgra32, null, 0);
                }
                bitmap.CopyPixels(m_input, m_stride, 0);
            }

            public void Pack()
            {
                if ((m_flag & 0x80) != 0)
                    ApplyDeltaFilter();

                CompressData();
                WriteHeader();
                m_output.Write(m_packed.ToArray(), 0, m_packed.Count);
            }

            private void ApplyDeltaFilter()
            {
                for (int i = m_input.Length - 1; i >= m_depth; i--)
                {
                    m_input[i] = (byte)(m_input[i] - m_input[i - m_depth]);
                }
            }

            private void WriteHeader()
            {
                var header = new byte[0x10];
                header[0] = (byte)'Y';
                header[1] = (byte)'B';
                header[2] = m_flag;
                header[3] = (byte)m_depth;

                LittleEndian.Pack((uint)m_packed.Count, header, 4);

                LittleEndian.Pack((ushort)m_image.Width, header, 12);
                LittleEndian.Pack((ushort)m_image.Height, header, 14);

                m_output.Write(header, 0, header.Length);
            }

            private void CompressData()
            {
                int src = 0;
                var controlBits = new List<bool>();
                var tempOutput = new List<byte>();

                while (src < m_input.Length)
                {
                    int matchLength = 0;
                    int matchOffset = 0;

                    if (src > 0)
                        FindLongestMatch(src, out matchLength, out matchOffset);

                    if (matchLength >= 2)
                    {
                        controlBits.Add(true); // 1 = reference
                        EncodeReference(tempOutput, matchLength, matchOffset);
                        src += matchLength;
                    }
                    else
                    {
                        controlBits.Add(false); // 0 = literal
                        tempOutput.Add(m_input[src]);
                        src++;
                    }

                    if (controlBits.Count == 8)
                        FlushControlBits(controlBits, tempOutput);
                }

                if (controlBits.Count > 0)
                    FlushControlBits(controlBits, tempOutput);
            }

            private void FindLongestMatch(int position, out int bestLength, out int bestOffset)
            {
                bestLength = 0;
                bestOffset = 0;

                int maxOffset = Math.Min(position, 0x3FFF); // Maximum offset supported by format
                int maxLength = Math.Min(m_input.Length - position, 0x1000); // Maximum length

                for (int offset = 1; offset <= maxOffset; offset++)
                {
                    int length = 0;
                    while (length < maxLength &&
                           position + length < m_input.Length &&
                           m_input[position - offset + length] == m_input[position + length])
                    {
                        length++;
                    }

                    if (length > bestLength)
                    {
                        bestLength = length;
                        bestOffset = offset;

                        if (bestLength >= 0x1000)
                            break;
                    }
                }
            }

            private void EncodeReference(List<byte> output, int length, int offset)
            {
                offset--; // Adjust offset (decoder adds 1)

                if (length >= 2 && length <= 5 && offset < 256)
                {
                    // Short reference format
                    byte b = (byte)((length - 2) << 2);
                    output.Add(b);
                }
                else if (length >= 3 && length <= 18 && offset < 4096)
                {
                    // Medium reference format
                    byte b = 0x80;
                    b |= (byte)((offset >> 8) & 0x3F);
                    output.Add(b);
                    output.Add((byte)(offset & 0xFF));
                }
                else
                {
                    // Long reference format
                    byte b = 0xC0;
                    b |= (byte)((offset >> 8) & 0x3F);
                    output.Add(b);
                    output.Add((byte)(offset & 0xFF));

                    // Encode length
                    if (length <= 0x102)
                    {
                        output.Add((byte)(length - 3));
                    }
                    else if (length <= 0x400)
                    {
                        output.Add(0xFE);
                    }
                    else
                    {
                        output.Add(0xFF);
                    }
                }
            }

            private void FlushControlBits(List<bool> controlBits, List<byte> tempOutput)
            {
                byte controlByte = 0;
                for (int i = 0; i < controlBits.Count; i++)
                {
                    if (controlBits[i])
                        controlByte |= (byte)(0x80 >> i);
                }

                m_packed.Add(controlByte);
                m_packed.AddRange(tempOutput);

                controlBits.Clear();
                tempOutput.Clear();
            }

            #region IDisposable Members
            bool disposed = false;

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!disposed)
                {
                    if (disposing)
                    {
                        // Stream disposal is handled by the caller
                    }
                    disposed = true;
                }
            }
        }
    } 
}
#endregion