using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace GameRes.Formats.Adobe
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get => "DAT/AIR"; }
        public override string Description { get => "Adobe AIR resource archive"; }
        public override uint     Signature { get => 0; }
        public override bool  IsHierarchic { get => false; }
        public override bool      CanWrite { get => false; }

        // AMF3 type markers
        private enum AMF3Type : byte
        {
            Undefined    = 0x00,
            Null         = 0x01,
            False        = 0x02,
            True         = 0x03,
            Integer      = 0x04,
            Double       = 0x05,
            String       = 0x06,
            XmlDoc       = 0x07,
            Date         = 0x08,
            Array        = 0x09,
            Object       = 0x0A,
            Xml          = 0x0B,
            ByteArray    = 0x0C,
            VectorInt    = 0x0D,
            VectorUint   = 0x0E,
            VectorDouble = 0x0F,
            VectorObject = 0x10,
            Dictionary   = 0x11
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_pos = Binary.BigEndian (file.View.ReadUInt32 (0));
            if (index_pos >= file.MaxOffset || 0 == index_pos || file.MaxOffset > 0x40000000)
                return null;
            uint index_size = (uint)(file.MaxOffset - index_pos);
            if (index_size > 0x100000) // arbitrary max size for compressed index
                return null;
            using (var input = file.CreateStream (index_pos, index_size))
            using (var unpacked = new DeflateStream (input, CompressionMode.Decompress))
            using (var index = new BinaryStream (unpacked, file.Name))
            {
                if (0x0A != index.ReadUInt8() ||
                    0x0B != index.ReadUInt8() ||
                    0x01 != index.ReadUInt8())
                    return null;
                var name_buffer = new byte[0x80];
                var dir = new List<Entry>();
                while (index.PeekByte() != -1)
                {
                    int length = index.ReadUInt8();
                    if (0 == (length & 1))
                        return null;
                    length >>= 1;
                    if (0 == length)
                        break;
                    index.Read (name_buffer, 0, length);
                    var name = Encoding.UTF8.GetString (name_buffer, 0, length);
                    if (0x09 != index.ReadUInt8() ||
                        0x05 != index.ReadUInt8() ||
                        0x01 != index.ReadUInt8())
                        return null;

                    var offsetValue = ReadAMF3Value (index);
                    if (offsetValue == null || !(offsetValue is IConvertible))
                        return null;
                    uint offset = Convert.ToUInt32 (offsetValue);

                    var sizeValue = ReadAMF3Value (index);
                    if (sizeValue == null || !(sizeValue is IConvertible))
                        return null;
                    uint size = Convert.ToUInt32 (sizeValue);

                    var entry = Create<PackedEntry> (name);
                    entry.Offset = offset;
                    entry.Size   = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0 == entry.Size)
                return Stream.Null;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new DeflateStream (input, CompressionMode.Decompress);
        }

        // Read AMF3 encoded value based on type marker
        internal static object ReadAMF3Value (IBinaryStream input)
        {
            byte markerByte = input.ReadUInt8();
            if (!Enum.IsDefined (typeof(AMF3Type), markerByte))
                throw new NotSupportedException ($"Unknown AMF3 marker: 0x{markerByte:X2}");

            switch ((AMF3Type)markerByte)
            {
            case AMF3Type.Undefined:
            case AMF3Type.Null:    return null;
            case AMF3Type.False:   return false;
            case AMF3Type.True:    return true;
            case AMF3Type.Integer: return ReadAMF3Integer(input);
            case AMF3Type.Double:  return ReadAMF3Double(input);
            case AMF3Type.String:  return ReadAMF3String(input);
            case AMF3Type.Date:    return ReadAMF3Date(input);
            default:
                throw new NotSupportedException ($"Unsupported AMF3 marker: 0x{markerByte:X2}");
            }
        }

        // Read AMF3 variable-length encoded integer (29-bit signed integer)
        internal static int ReadAMF3Integer (IBinaryStream input)
        {
            int result = 0, n = 0;
            int b = input.ReadUInt8();

            while ((b & 0x80) != 0 && n < 3)
            {
                result = (result << 7) | (b & 0x7F);
                b = input.ReadUInt8();
                n++;
            }

            if (n < 3)
            {
                result = (result << 7) | b;
            }
            else
            {
                result = (result << 8) | b;  // Use all 8 bits from the 4th byte
                if ((result & 0x10000000) != 0) // Check for sign extension
                    result |= unchecked((int)0xE0000000);
            }

            return result;
        }

        // Read AMF3 double (64-bit IEEE 754 double)
        internal static double ReadAMF3Double (IBinaryStream input)
        {
            byte[] bytes = new byte[8];
            input.Read (bytes, 0, 8);

            if (BitConverter.IsLittleEndian) // AMF3 uses network byte order
                Array.Reverse (bytes);

            return BitConverter.ToDouble (bytes, 0);
        }

        // Read AMF3 UTF-8 string
        internal static string ReadAMF3String (IBinaryStream input)
        {
            int length = ReadAMF3Integer (input);

            // The low bit is a flag; actual length is shifted
            length = length >> 1;

            if (length == 0)
                return string.Empty;

            byte[] bytes = new byte[length];
            input.Read (bytes, 0, length);
            return Encoding.UTF8.GetString (bytes);
        }

        // Read AMF3 date (milliseconds since epoch as double)
        internal static DateTime ReadAMF3Date (IBinaryStream input)
        {
            double milliseconds = ReadAMF3Double (input);
            return new DateTime (1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddMilliseconds (milliseconds);
        }
    }
}