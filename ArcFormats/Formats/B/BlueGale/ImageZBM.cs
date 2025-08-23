using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.BlueGale
{
    internal class ZbmMetaData : ImageMetaData
    {
        public  int UnpackedSize;
        public  int DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class ZbmFormat : ImageFormat
    {
        public override string         Tag { get { return "ZBM/BLUEGALE"; } }
        public override string Description { get { return "BlueGale compressed image format"; } }
        public override uint     Signature { get { return 0x5F706D61; } } // 'amp_'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 4;
            int version = stream.ReadInt16();
            if (version != 1)
                return null;
            int unpacked_size = stream.ReadInt32();
            int data_offset = stream.ReadInt32();
            if (unpacked_size < 0x36 || data_offset < stream.Position)
                return null;
            var header = new byte[0x20];
            stream.Position = data_offset;
            Unpack (stream.AsStream, header);
            Decrypt (header);
            if ('B' != header[0] || 'M' != header[1])
                return null;
            return new ZbmMetaData
            {
                Width = LittleEndian.ToUInt32 (header, 0x12),
                Height = LittleEndian.ToUInt32 (header, 0x16),
                BPP = LittleEndian.ToInt16 (header, 0x1C),
                UnpackedSize = unpacked_size,
                DataOffset = data_offset,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (ZbmMetaData)info;
            var data = new byte[meta.UnpackedSize];
            stream.Position = meta.DataOffset;
            Unpack (stream.AsStream, data);
            Decrypt (data);
            using (var bmp = new BinMemoryStream (data, stream.Name))
                return Bmp.Read (bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ZbmFormat.Write not implemented");
        }

        internal static void Unpack (Stream input, byte[] output, int dst = 0)
        {
            using (var bits = new MsbBitStream (input, true))
            {
                bits.GetNextBit();
                while (dst < output.Length)
                {
                    int count = bits.GetBits (8);
                    if (-1 == count)
                        break;
                    if (count > 0x7F)
                    {
                        int offset = bits.GetBits (10);
                        if (-1 == offset)
                            throw new EndOfStreamException();
                        count = Math.Min (count & 0x7F, output.Length-dst);
                        Binary.CopyOverlapped (output, dst-offset, dst, count);
                        dst += count;
                    }
                    else
                    {
                        if (0 == count)
                            break;
                        for (int i = 0 ; i < count && dst < output.Length; i++)
                        {
                            int v = bits.GetBits (8);
                            if (-1 == v)
                                throw new EndOfStreamException();
                            output[dst++] = (byte)v;
                        }
                    }
                }
            }
        }

        static void Decrypt (byte[] data)
        {
            if (('B'^0xFF) == data[0] && ('M'^0xFF) == data[1])
            {
                int encrypted = Math.Min (100, data.Length);
                for (int i = 0; i < encrypted; ++i)
                    data[i] ^= 0xFF;
            }
        }
    }
}
