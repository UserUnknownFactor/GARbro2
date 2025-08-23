using GameRes.Compression;
using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.YuRis
{
    internal class YcgMetaData : ImageMetaData
    {
        public int  CompressionMethod;
        public int  CompressedSize1;
        public int  CompressedSize2;
        public int  UnpackedSize1;
        public int  UnpackedSize2;
    }

    [Export(typeof(ImageFormat))]
    public class YcgFormat : ImageFormat
    {
        public override string         Tag { get { return "YCG"; } }
        public override string Description { get { return "YU-RIS compressed image format"; } }
        public override uint     Signature { get { return 0x474359; } } // 'YCG'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x38);
            return new YcgMetaData
            {
                Width   = header.ToUInt32 (4),
                Height  = header.ToUInt32 (8),
                BPP     = header.ToInt32 (12),
                CompressionMethod   = header.ToInt32 (0x10),
                UnpackedSize1       = header.ToInt32 (0x20),
                CompressedSize1     = header.ToInt32 (0x24),
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
            throw new System.NotImplementedException ("YcgFormat.Write not implemented");
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
                throw new NotImplementedException ("YSSnp compression not implemented");
            else
                throw new InvalidFormatException ("Unknown YCG compression method");
        }

        void UnpackZlib ()
        {
            m_input.Position = 0x38;
            using (var z = new ZLibStream (m_input, CompressionMode.Decompress, true))
                if (m_info.UnpackedSize1 != z.Read (m_output, 0, m_info.UnpackedSize1))
                    throw new EndOfStreamException();
            m_input.Position = 0x38 + m_info.CompressedSize1;
            using (var z = new ZLibStream (m_input, CompressionMode.Decompress, true))
                if (m_info.UnpackedSize2 != z.Read (m_output, m_info.UnpackedSize1, m_info.UnpackedSize2))
                    throw new EndOfStreamException();
        }
    }
}
