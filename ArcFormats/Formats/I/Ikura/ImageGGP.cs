using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Ikura
{
    internal class GgpMetaData : ImageMetaData
    {
        public byte[]   Key = new byte[8];
        public uint     Offset;
        public uint     Length;
    }

    [Export(typeof(ImageFormat))]
    public class GgpFormat : PngFormat
    {
        public override string         Tag { get { return "GGP"; } }
        public override string Description { get { return "Digital Romance System encrypted image format"; } }
        public override uint     Signature { get { return 0x46504747u; } } // 'GGPF'
        public override bool      CanWrite { get { return false; } }

        public GgpFormat ()
        {
            Extensions = new string[] { "ggp", "gg" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x24);
            if (!header.AsciiEqual ("GGPFAIKE"))
                return null;
            var info = new GgpMetaData
            {
                Offset = header.ToUInt32 (0x14),
                Length = header.ToUInt32 (0x18),
            };
            for (int i = 0; i < 8; ++i)
                info.Key[i] = (byte)(header[i] ^ header[i+0xC]);
            stream.Position = info.Offset;
            using (var enc = new EncryptedStream (stream.AsStream, info.Key, true))
            using (var png = new BinaryStream (enc, stream.Name))
            {
                var png_info = base.ReadMetaData (png);
                info.Width   = png_info.Width;
                info.Height  = png_info.Height;
                info.BPP     = png_info.BPP;
                info.OffsetX = png_info.OffsetX;
                info.OffsetY = png_info.OffsetY;
                return info;
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (GgpMetaData)info;
            using (var input = new StreamRegion (file.AsStream, meta.Offset, meta.Length, true))
            using (var enc = new EncryptedStream (input, meta.Key))
            using (var png = new BinaryStream (enc, file.Name))
                return base.Read (png, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GgpFormat.Write not implemented");
        }
    }

    internal class EncryptedStream : InputProxyStream
    {
        private byte[]  m_key;
        private long    m_position;

        public EncryptedStream (Stream main, byte[] key, bool leave_open = false)
            : base (main, leave_open)
        {
            if (null == key)
                throw new ArgumentNullException ("key");
            if (key.Length < 8)
                throw new ArgumentException ("key");
            m_key = key;
            m_position = 0;
        }

        public override bool CanSeek  { get { return false; } }
        public override long Length   { get { throw new NotSupportedException(); } }
        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = BaseStream.Read (buffer, offset, count);
            if (read > 0)
            {
                for (int i = 0; i < read; ++i)
                {
                    buffer[offset+i] ^= m_key[m_position++ & 7];
                }
            }
            return read;
        }

        public override int ReadByte ()
        {
            int b = BaseStream.ReadByte();
            if (-1 != b)
            {
                b ^= m_key[m_position++ & 7];
            }
            return b;
        }
    }
}
