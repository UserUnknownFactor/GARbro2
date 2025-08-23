using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Triangle
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/TRIANGLE"; } }
        public override string Description { get { return "Triangle resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count-1))
                return null;
            uint next_offset = file.View.ReadUInt32 (4);
            if (4 + count * 0x11 != next_offset)
                return null;
            uint index_offset = 8;
            var dir = new List<Entry> (--count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0xD);
                if (0 == name.Length)
                    return null;
                uint offset = next_offset;
                next_offset = file.View.ReadUInt32 (index_offset+0xD);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                entry.Size = (uint)(next_offset - offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x11;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
