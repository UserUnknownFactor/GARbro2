using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Bruns
{
    internal class EencMetaData : ImageMetaData
    {
        public uint             Key;
        public ImageMetaData    Info;
        public ImageFormat      Format;
        public bool             Compressed;
    }

    [Export(typeof(ImageFormat))]
    public class EencFormat : ImageFormat
    {
        public override string         Tag { get { return "EENC/IMAGE"; } }
        public override string Description { get { return "Bruns system encrypted image"; } }
        public override uint     Signature { get { return  0x434E4545; } } // 'EENC'
        public override bool      CanWrite { get { return  true; } }

        internal static readonly uint EencKey =  0xDEADBEEF;

        public EencFormat ()
        {
            Extensions = new string[] { "brs", "png", "bmp" };
            Signatures = new uint[] { 0x434E4545, 0x5A4E4545 }; // 'EENC', 'EENZ'
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (8);
            bool compressed = 'Z' == header[3];
            uint size = header.ToUInt32 (4);
            uint key = size ^ EencKey;

            Stream input = new StreamRegion (stream.AsStream, 8, true);
            try
            {
                input = new EencStream (input, key);
                if (compressed)
                {
                    input = new ZLibStream (input, CompressionMode.Decompress);
                    input = new SeekableStream (input);
                }
                using (var bin = new BinaryStream (input, stream.Name, true))
                {
                    var format = FindFormat (bin);
                    if (null == format)
                        return null;
                    return new EencMetaData
                    {
                        Width = format.Item2.Width,
                        Height = format.Item2.Height,
                        BPP = format.Item2.BPP,
                        Key = key,
                        Info = format.Item2,
                        Format = format.Item1,
                        Compressed = compressed,
                    };
                }
            }
            finally
            {
                input.Dispose();
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (EencMetaData)info;
            meta.Info.FileName = info.FileName;
            Stream input = new StreamRegion (stream.AsStream, 8, true);
            try
            {
                input = new EencStream (input, meta.Key);
                if (meta.Compressed)
                    input = new ZLibStream (input, CompressionMode.Decompress);
                using (var bin = new BinaryStream (input, stream.Name, true))
                    return meta.Format.Read (bin, meta.Info);
            }
            finally
            {
                input.Dispose();
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            ImageFormat targetFormat = ImageFormat.Png;
            bool useCompression = true;
            string ext = Path.GetExtension (file is FileStream fs ? fs.Name : "").ToLowerInvariant();
            if (ext == ".png") useCompression = false;

            byte[] originalData;
            using (var imageBuffer = new MemoryStream())
            {
                targetFormat.Write (imageBuffer, image);
                originalData = imageBuffer.ToArray();
            }
            WriteEncrypted (file, originalData, useCompression);
        }

        public static void WriteEncrypted (Stream file, byte[] data, bool compress)
        {
            uint originalSize = (uint)data.Length;

            file.WriteByte ((byte)'E');
            file.WriteByte ((byte)'E');
            file.WriteByte ((byte)'N');
            file.WriteByte (compress ? (byte)'Z' : (byte)'C');

            var sizeBytes = BitConverter.GetBytes (originalSize);
            file.Write (sizeBytes, 0, 4);

            byte[] dataToEncrypt = data;
            if (compress)
            {
                using (var compressedBuffer = new MemoryStream())
                {
                    using (var zlibStream = new ZLibStream (compressedBuffer, CompressionMode.Compress, true))
                    {
                        zlibStream.Write (data, 0, data.Length);
                    }
                    dataToEncrypt = compressedBuffer.ToArray();
                }
            }

            uint key = originalSize ^ EencKey;
            for (int i = 0; i < dataToEncrypt.Length; ++i)
            {
                int pos = i & 3;
                dataToEncrypt[i] ^= (byte)(key >> (pos << 3));
            }

            file.Write (dataToEncrypt, 0, dataToEncrypt.Length);
        }
    }

    internal class EencStream : InputProxyStream
    {
        uint    m_key;

        public EencStream (Stream main, uint key, bool leave_open = false)
            : base (main, leave_open)
        {
            m_key = key;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int pos = (int)Position & 3;
            int read = BaseStream.Read (buffer, offset, count);
            for (int i = 0; i < read; ++i)
            {
                buffer[offset+i] ^= (byte)(m_key >> (pos << 3));
                pos = (pos + 1) & 3;
            }
            return read;
        }

        public override int ReadByte ()
        {
            int pos = (int)Position & 3;
            int b = BaseStream.ReadByte();
            if (-1 != b)
                b ^= (byte)(m_key >> (pos << 3));
            return b;
        }
    }
}