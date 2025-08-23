using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [000428][Apple Pie] Seishun Ouka

namespace GameRes.Formats.Yaneurao
{
    [Export(typeof(ImageFormat))]
    public class GtoFormat : ImageFormat
    {
        public override string         Tag { get { return "GTO"; } }
        public override string Description { get { return "Yaneurao obfuscated bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (2);
            if (!header.AsciiEqual ("NY"))
                return null;
            file.Position = 0;
            using (var input = OpenGtoStream (file))
                return Bmp.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = OpenGtoStream (file))
                return Bmp.Read (input, info);
        }

        internal IBinaryStream OpenGtoStream (IBinaryStream file)
        {
            var input = new SubFilterStream (file.AsStream, 0xC, true);
            return new BinaryStream (input, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GtoFormat.Write not implemented");
        }
    }

    public class SubFilterStream : ProxyStream
    {
        private byte        m_key;

        public override bool CanWrite { get { return false; } }

        public SubFilterStream (Stream stream, byte key, bool leave_open = false)
            : base (stream, leave_open)
        {
            m_key = key;
        }

        #region System.IO.Stream methods
        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = BaseStream.Read (buffer, offset, count);
            for (int i = 0; i < read; ++i)
            {
                buffer[offset+i] -= m_key;
            }
            return read;
        }

        public override int ReadByte ()
        {
            int b = BaseStream.ReadByte();
            if (-1 != b)
            {
                b = (b - m_key) & 0xFF;
            }
            return b;
        }
        #endregion
    }
}
