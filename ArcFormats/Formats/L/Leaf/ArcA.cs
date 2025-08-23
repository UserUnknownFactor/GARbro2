using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Leaf
{
    internal class ALeafEntry : PackedEntry
    {
        public byte Key;
    }

    [Export(typeof(ArchiveFormat))]
    public class AOpener : ArchiveFormat
    {
        public override string         Tag { get { return "A/Leaf"; } }
        public override string Description { get { return "Leaf resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public AOpener ()
        {
            Extensions = new string[] { "a" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0xAF1E != file.View.ReadUInt16 (0))
                return null;
            int count = file.View.ReadUInt16 (2);
            if (!IsSaneCount (count))
                return null;

            long base_offset = 4 + 0x20 * count;
            uint index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x17);
                if (string.IsNullOrEmpty (name))
                    return null;
                var entry = FormatCatalog.Instance.Create<ALeafEntry> (name);
                entry.Key = file.View.ReadByte (index_offset+0x17);
                entry.IsPacked = 0 != entry.Key;
                entry.Size   = file.View.ReadUInt32 (index_offset+0x18);
                entry.Offset = base_offset + file.View.ReadUInt32 (index_offset+0x1C);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as ALeafEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            if (0 == pent.UnpackedSize)
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset);
            Stream input = arc.File.CreateStream (entry.Offset+4, entry.Size-4);
            input = new LzssStream (input);
            if (pent.Key >= 0x7F && pent.Key <= 0x89 && pent.UnpackedSize > 0x20)
            {
                using (input)
                    return Decrypt (input, pent.UnpackedSize, (byte)(pent.Key & 0xF));
            }
            return input;
        }

        Stream Decrypt (Stream input, long length, byte key)
        {
            var data = new byte[length];
            input.Read (data, 0, data.Length);
            uint width = data.ToUInt32 (0);
            uint height = data.ToUInt32 (4);
            uint image_size = width * height;
            int type = data.ToUInt16 (0x10);
            int bits = data.ToUInt16 (0x12);
            if (1 == type && 0x20 == bits && image_size > 0 && (32 + image_size * 4) <= length)
            {
                byte r = 0, g = 0, b = 0;
                int dst = 0x20;
                for (uint i = 0; i < image_size; ++i)
                {
                    byte a = data[dst+3];
                    b += (byte)(data[dst  ] + a - key);
                    g += (byte)(data[dst+1] + a - key);
                    r += (byte)(data[dst+2] + a - key);
                    data[dst++] = b;
                    data[dst++] = g;
                    data[dst++] = r;
                    data[dst++] = 0;
                }
            }
            return new BinMemoryStream (data);
        }
    }
}
