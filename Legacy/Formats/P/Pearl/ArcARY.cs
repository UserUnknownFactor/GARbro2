using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Pearl
{
    // implementation based on BMX/TRIANGLE
    // exact same layout, but doesn't have compressed entries.

    [Export(typeof(ArchiveFormat))]
    public class AryOpener : ArchiveFormat
    {
        public override string         Tag => "ARY";
        public override string Description => "Pearl Soft resource archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_size = (uint)count * 4 + 8;
            if (index_size > file.View.Reserve (0, index_size))
                return null;
            uint index_offset = 4;
            uint offset = file.View.ReadUInt32 (index_offset);
            if (offset != index_size)
                return null;
            uint last_offset = file.View.ReadUInt32 (index_size - 4);
            if (last_offset != file.MaxOffset)
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 4;
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}", base_name, i),
                    Offset = offset,
                };
                offset = file.View.ReadUInt32 (index_offset);
                entry.Size = (uint)(offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            foreach (var entry in dir)
            {
                uint signature = file.View.ReadUInt32 (entry.Offset);
                entry.ChangeType (AutoEntry.DetectFileType (signature));
            }
            return new ArcFile (file, this, dir);
        }
    }
}
