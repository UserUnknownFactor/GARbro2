using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Valkyria
{
    [Export(typeof(ArchiveFormat))]
    public class Am2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "AM2"; } }
        public override string Description { get { return "Valkyria multi-frame image"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".am2"))
                return null;
            uint base_offset = file.View.ReadUInt32 (0) + 12;
            int count = file.View.ReadInt32 (4);
            if (base_offset >= file.MaxOffset || !IsSaneCount (count))
                return null;
            uint index_offset = 12;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}.MG2", base_name, i),
                    Type = "image",
                    Offset = file.View.ReadUInt32 (index_offset) + base_offset,
                    Size   = file.View.ReadUInt32 (index_offset+4),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0xC;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
