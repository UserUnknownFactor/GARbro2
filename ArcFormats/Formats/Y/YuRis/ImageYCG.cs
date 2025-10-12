using System.Windows.Media.Imaging;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.YuRis
{
    internal class YcgMetaData : ImageMetaData
    {
        public int  CompressionMethod;
        //public int  ChunkCount;      // ?
        //public int  Reserved;        // 0
        //public int  SplitRowMinus1;  // Last row of first chunk (height/2 - 1), can be -1
        public int  CompressedSize1;
        public int  CompressedSize2;
        public int  UnpackedSize1;
        public int  UnpackedSize2;
        //public int  SplitRow;        // First row of second chunk (height/2)
        //public int  LastRow;         // Last row index (height - 1)
    }

    [Export(typeof(ImageFormat))]
    public class YcgFormat : ImageFormat
    {
        public override string         Tag { get { return "YCG"; } }
        public override string Description { get { return "YU-RIS compressed image format"; } }
        public override uint     Signature { get { return  0x474359; } } // 'YCG'
        public override bool      CanWrite { get { return  true; } }

        public YcgFormat ()
        {
            Extensions = new[] { "ycg", "png" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x38);
            return new YcgMetaData
            {
                Width               = header.ToUInt32 ( 4 ),
                Height              = header.ToUInt32 ( 8 ),
                BPP                 = header.ToInt32 ( 12 ),
                CompressionMethod   = header.ToInt32 (0x10),
                //ChunkCount        = header.ToInt32 (0x14),
                //Reserved          = header.ToInt32 (0x18),
                //SplitRowMinus1    = header.ToInt32 (0x1C),
                UnpackedSize1       = header.ToInt32 (0x20),
                CompressedSize1     = header.ToInt32 (0x24),
                //SplitRow          = header.ToInt32 (0x28),
                //LastRow           = header.ToInt32 (0x2C),
                UnpackedSize2       = header.ToInt32 (0x30),
                CompressedSize2     = header.ToInt32 (0x34),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = new YcgReader (stream.AsStream, (YcgMetaData)info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            var bitmap = image.Bitmap;
            if (bitmap.Format != PixelFormats.Bgra32)
                bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);

            int stride = (int)image.Width * 4;
            var pixels = new byte[stride * (int)image.Height];
            bitmap.CopyPixels (pixels, stride, 0);

            int splitRow = (int)image.Height / 2;
            int splitRowMinus1 = splitRow - 1; // can be -1 for 1-pixel height
            int lastRow = (int)image.Height - 1;

            using (var writer = new BinaryWriter (file, System.Text.Encoding.ASCII, true))
            {
                writer.Write ((uint)0x474359); // 'YCG'
                writer.Write ((uint)image.Width);
                writer.Write ((uint)image.Height);
                writer.Write ((int)32); // BPP
                writer.Write ((int)1);  // CompressionMethod: zlib
                writer.Write ((int)2);  // ChunkCount?
                writer.Write ((int)0);  // Reserved
                writer.Write (splitRowMinus1);

                int rowsInFirstChunk = splitRow;
                int rowsInSecondChunk = (int)image.Height - splitRow;
                int unpackedSize1 = rowsInFirstChunk * stride;
                int unpackedSize2 = rowsInSecondChunk * stride;

                byte[] compressed1;
                using (var ms = new MemoryStream())
                {
                    using (var z = new ZLibStream (ms, CompressionMode.Compress, CompressionLevel.Level9))
                    {
                        if (unpackedSize1 > 0)
                            z.Write (pixels, 0, unpackedSize1);
                    }
                    compressed1 = ms.ToArray();
                }

                byte[] compressed2;
                using (var ms = new MemoryStream())
                {
                    using (var z = new ZLibStream (ms, CompressionMode.Compress, CompressionLevel.Level9))
                    {
                        z.Write (pixels, unpackedSize1, unpackedSize2);
                    }
                    compressed2 = ms.ToArray();
                }

                writer.Write (unpackedSize1);
                writer.Write ((int)compressed1.Length);
                writer.Write (splitRow);
                writer.Write (lastRow);
                writer.Write (unpackedSize2);
                writer.Write ((int)compressed2.Length);

                writer.Write (compressed1);
                writer.Write (compressed2);
            }
        }
    }

    internal sealed class YcgReader
    {
        Stream          m_input;
        YcgMetaData     m_info;
        byte[]          m_output;

        public PixelFormat Format { get; private set; }
        public byte[]        Data { get { return m_output; } }

        public YcgReader (Stream input, YcgMetaData info)
        {
            m_input = input;
            m_info = info;
            int stride = (int)m_info.Width * 4;
            m_output = new byte[stride * (int)m_info.Height];
            Format = PixelFormats.Bgra32;
        }

        public void Unpack ()
        {
            if (1 == m_info.CompressionMethod)
                UnpackZlib();
            else if (2 == m_info.CompressionMethod)
                UnpackSnappy();
            else
                throw new InvalidFormatException ("Unknown YCG compression method");
        }

        void UnpackZlib ()
        {
            m_input.Position = 0x38;

            // First chunk might be empty (for images with height < 2)
            if (m_info.UnpackedSize1 > 0)
            {
                using (var z = new ZLibStream (m_input, CompressionMode.Decompress, true))
                    if (m_info.UnpackedSize1 != z.Read (m_output, 0, m_info.UnpackedSize1))
                        throw new EndOfStreamException();
            }
            else
                m_input.Position += m_info.CompressedSize1;

            // Second chunk always has data
            m_input.Position = 0x38 + m_info.CompressedSize1;
            using (var z = new ZLibStream (m_input, CompressionMode.Decompress, true))
                if (m_info.UnpackedSize2 != z.Read (m_output, m_info.UnpackedSize1, m_info.UnpackedSize2))
                    throw new EndOfStreamException();
        }

        void UnpackSnappy ()
        {
            m_input.Position = 0x38;
            if (m_info.UnpackedSize1 > 0)
            {
                var compressed1 = new byte[m_info.CompressedSize1];
                if (m_info.CompressedSize1 != m_input.Read (compressed1, 0, m_info.CompressedSize1))
                    throw new EndOfStreamException();
                var decompressed1 = SnappyNative.Uncompress (compressed1);
                if (decompressed1.Length != m_info.UnpackedSize1)
                    throw new InvalidFormatException ("Unexpected decompressed size");
                Buffer.BlockCopy (decompressed1, 0, m_output, 0, m_info.UnpackedSize1);
            }
            else
                m_input.Position += m_info.CompressedSize1;

            var compressed2 = new byte[m_info.CompressedSize2];
            if (m_info.CompressedSize2 != m_input.Read (compressed2, 0, m_info.CompressedSize2))
                throw new EndOfStreamException();
            var decompressed2 = SnappyNative.Uncompress (compressed2);
            if (decompressed2.Length != m_info.UnpackedSize2)
                throw new InvalidFormatException ("Unexpected decompressed size");
            Buffer.BlockCopy (decompressed2, 0, m_output, m_info.UnpackedSize1, m_info.UnpackedSize2);
        }
    }
}
