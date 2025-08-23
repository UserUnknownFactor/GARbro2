using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;

namespace GameRes.Formats.Kaguya
{
    internal class ApsMetaData : ImageMetaData
    {
        public bool IsPacked;
        public uint PackedSize;
        public uint UnpackedSize;
        public uint DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class Aps3Format : ApFormat
    {
        public override string         Tag { get { return "APS3"; } }
        public override string Description { get { return "KaGuYa tiled image format"; } }
        public override uint     Signature { get { return 0x53504104; } } // '\x04APS'

        public Aps3Format ()
        {
            Extensions = new string[] { "aps", "parts", "ap3" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 4;
            if (stream.ReadByte() != '3')
                return null;
            var rect = new Rectangle (0, 0, 0, 0);
            int count = stream.ReadInt32();
            for (int i = 0; i < count; ++i)
            {
                stream.ReadInt32();
                int name_length = stream.ReadByte();
                stream.Seek (name_length, SeekOrigin.Current);
                int x = stream.ReadInt32();
                int y = stream.ReadInt32();
                int w = stream.ReadInt32() - x;
                int h = stream.ReadInt32() - y;
                if (name_length > 0)
                {
                    var part_rect = new Rectangle (x, y, w, h);
                    rect = Rectangle.Union (rect, part_rect);
                }
                stream.Seek (12, SeekOrigin.Current);
            }
            uint data_size = stream.ReadUInt32();
            if (data_size > stream.Length-stream.Position)
                return null;
            return ReadCompressionMetaData (stream, rect);
        }

        internal ApsMetaData ReadCompressionMetaData (IBinaryStream reader, Rectangle rect)
        {
            int compression = reader.ReadInt16();
            var info = new ApsMetaData
            {
                Width = (uint)rect.Width,
                Height = (uint)rect.Height,
                BPP = 32,
                IsPacked = 0 != compression,
            };
            if (0 == compression)
            {
                info.UnpackedSize = reader.ReadUInt32();
            }
            else if (1 == compression)
            {
                info.PackedSize = reader.ReadUInt32();
                info.UnpackedSize = reader.ReadUInt32();
            }
            else
                return null;
            info.DataOffset = (uint)reader.Position;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (ApsMetaData)info;
            stream.Position = meta.DataOffset;
            byte[] image_data;
            if (meta.IsPacked)
            {
                using (var reader = new LzReader (stream.AsStream, meta.PackedSize, meta.UnpackedSize))
                {
                    reader.Unpack();
                    image_data = reader.Data;
                }
            }
            else
            {
                image_data = stream.ReadBytes ((int)meta.UnpackedSize);
            }
            using (var unpacked = BinaryStream.FromArray (image_data, stream.Name))
            {
                var ap_info = base.ReadMetaData (unpacked);
                if (null == ap_info)
                    throw new InvalidFormatException();
                return base.Read (unpacked, ap_info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Aps3Format.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class ApsFormat : Aps3Format
    {
        public override string         Tag { get { return "APS"; } }
        public override string Description { get { return "KaGuYa tiled image format"; } }
        public override uint     Signature { get { return 0; } }

        public ApsFormat ()
        {
            Extensions = new string[] { "aps", "parts" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            int name_count = stream.ReadInt16();
            if (name_count <= 0 || name_count > 1000)
                return null;
            for (int i = 0; i < name_count; ++i)
            {
                int name_length = stream.ReadInt32();
                if (name_length <= 0 || name_length > 260)
                    return null;
                stream.Seek (name_length, SeekOrigin.Current);
            }
            int tile_count = stream.ReadInt16();
            if (tile_count <= 0 || tile_count > 1000)
                return null;
            var rect = new Rectangle (0, 0, 0, 0);
            for (int i = 0; i < tile_count; ++i)
            {
                int name_length = stream.ReadInt32();
                if (name_length <= 0 || name_length > 260)
                    return null;
                stream.Seek (name_length+0xC, SeekOrigin.Current);
                int x = stream.ReadInt32();
                int y = stream.ReadInt32();
                int w = stream.ReadInt32() - x;
                int h = stream.ReadInt32() - y;
                var part_rect = new Rectangle (x, y, w, h);
                rect = Rectangle.Union (rect, part_rect);
                stream.Seek (0x28, SeekOrigin.Current);
            }
            return ReadCompressionMetaData (stream, rect);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ApsFormat.Write not implemented");
        }
    }
}
