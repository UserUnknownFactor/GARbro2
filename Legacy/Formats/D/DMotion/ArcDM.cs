using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.DMotion
{
    internal class ExtEntry : Entry
    {
        public int Count;
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag => "256/DMOTION";
        public override string Description => "D-Motion engine resource archive";
        public override uint     Signature => 0x4B434150; // 'PACK'
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "FILE100DATA"))
                return null;
            if (!file.View.AsciiEqual (0x10, @".\\\"))
                return null;
            int ext_count = file.View.ReadUInt16 (0x16);
            long index_pos = file.View.ReadUInt32 (0x18);
            int total_count = 0;
            var ext_dir = new List<ExtEntry> (ext_count);
            for (int i = 0; i < ext_count; ++i)
            {
                var ext = new ExtEntry {
                    Name   = file.View.ReadString (index_pos, 4),
                    Count  = file.View.ReadUInt16 (index_pos+6),
                    Offset = file.View.ReadUInt32 (index_pos+8),
                    Size   = file.View.ReadUInt32 (index_pos+12),
                };
                ext_dir.Add (ext);
                total_count += ext.Count;
                index_pos += 0x10;
            }
            if (!IsSaneCount (total_count))
                return null;

            var dir = new List<Entry> (total_count);
            foreach (var ext in ext_dir)
            {
                index_pos = ext.Offset;
                for (int i = 0; i < ext.Count; ++i)
                {
                    var name = file.View.ReadString (index_pos, 8).TrimEnd() + ext.Name;
                    var entry = Create<Entry> (name);
                    entry.Offset = file.View.ReadUInt32 (index_pos+8);
                    entry.Size   = file.View.ReadUInt32 (index_pos+12);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_pos += 0x10;
                }
            }
            return new ArcFile (file, this, dir);
        }
    }
}
