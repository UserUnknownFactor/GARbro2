using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Leaf
{
    [Export(typeof(ArchiveFormat))]
    public class PxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PX/LEAF"; } }
        public override string Description { get { return "Leaf multi-frame image"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadUInt16 (0x10) != 0x80 || !file.View.AsciiEqual (0x14, "Leaf"))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_offset = 0x20;
            uint base_offset = (uint)count * 4 + index_offset;
            var dir = new List<Entry> (count);
            long next_offset = base_offset + file.View.ReadUInt32 (index_offset);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 4;
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}", base_name, i),
                    Type = "image",
                    Offset = next_offset,
                };
                if (i+1 < count)
                    next_offset = base_offset + file.View.ReadUInt32 (index_offset);
                else
                    next_offset = file.MaxOffset;
                entry.Size = (uint)(next_offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
