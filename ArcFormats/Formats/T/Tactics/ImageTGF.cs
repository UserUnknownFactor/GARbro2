using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Tactics
{
    internal class TgfMetaData : ImageMetaData
    {
        public uint BitmapSize;
        public  int ChunkSize;
    }

    [Export(typeof(ImageFormat))]
    public class TgfFormat : ImageFormat
    {
        public override string         Tag { get { return "TGF"; } }
        public override string Description { get { return "Tactics graphics file"; } }
        public override uint     Signature { get { return 0; } }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("TgfFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            uint length = stream.ReadUInt32();
            int chunk_size = stream.ReadInt32();
            if (length > 0xffffff || chunk_size <= 0 || length < chunk_size)
                return null;
            using (var reader = new Reader (stream, (uint)Math.Max (0x20, chunk_size+2), chunk_size))
            {
                reader.Unpack();
                var bmp = reader.Data;
                if (bmp[0] != 'B' || bmp[1] != 'M')
                    return null;
                return new TgfMetaData
                {
                    Width = LittleEndian.ToUInt32 (bmp, 0x12),
                    Height = LittleEndian.ToUInt32 (bmp, 0x16),
                    BPP = LittleEndian.ToInt16 (bmp, 0x1c),
                    BitmapSize = length,
                    ChunkSize = chunk_size,
                };
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (TgfMetaData)info;
            stream.Position = 8;
            using (var reader = new Reader (stream, meta.BitmapSize, meta.ChunkSize))
            {
                reader.Unpack();
                using (var bmp = new MemoryStream (reader.Data))
                {
                    var decoder = new BmpBitmapDecoder (bmp,
                        BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    BitmapSource frame = decoder.Frames[0];
                    frame.Freeze();
                    return new ImageData (frame, info);
                }
            }
        }

        internal class Reader : IDisposable
        {
            IBinaryStream   m_input;
            byte[]          m_output;
            int             m_chunk_size;

            public byte[] Data { get { return m_output; } }

            public Reader (IBinaryStream file, uint bmp_size, int chunk_size)
            {
                m_chunk_size = chunk_size;
                m_output = new byte[bmp_size];
                m_input = file;
            }

            public void Unpack ()
            {
                int dst = 0;
                while (dst < m_output.Length)
                {
                    int code = m_input.ReadUInt8();
                    switch (code)
                    {
                    case 0:
                        {
                            int count = m_input.ReadUInt8();
                            if (dst + count > m_output.Length)
                                count = m_output.Length - dst;
                            m_input.Read (m_output, dst, count);
                            dst += count;
                            break;
                        }
                    case 1:
                        {
                            int count = m_input.ReadUInt8() * m_chunk_size;
                            if (dst + count > m_output.Length)
                                count = m_output.Length - dst;
                            m_input.Read (m_output, dst, count);
                            dst += count;
                            break;
                        }
                    default:
                        {
                            if (dst + m_chunk_size > m_output.Length)
                                return;
                            m_input.Read (m_output, dst, m_chunk_size);
                            int src = dst;
                            dst += m_chunk_size;
                            for (int i = 1; i < code; ++i)
                            {
                                if (dst + m_chunk_size > m_output.Length)
                                    return;
                                System.Buffer.BlockCopy (m_output, src, m_output, dst, m_chunk_size);
                                dst += m_chunk_size;
                            }
                            break;
                        }
                    }
                }
            }

            #region IDisposable Members
            public void Dispose ()
            {
            }
            #endregion
        }
    }
}
