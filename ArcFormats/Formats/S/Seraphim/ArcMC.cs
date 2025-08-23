using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Seraphim
{
    [Export(typeof(ArchiveFormat))]
    public class McOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MC/SERAPH"; } }
        public override string Description { get { return "Seraphim engine animation resource"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadInt32 (0) != 0 || !file.View.AsciiEqual (4, "MC"))
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            if (file.View.ReadUInt32 (0x10) + 0x14 != file.MaxOffset)
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            long offset = 0x14;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}.cb", base_name, i),
                    Type = "image",
                    Offset = offset+4,
                    Size = file.View.ReadUInt32 (offset),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                offset += 4 + entry.Size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
