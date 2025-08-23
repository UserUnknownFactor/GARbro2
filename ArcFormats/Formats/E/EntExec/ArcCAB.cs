using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.EntExec
{
    [Export(typeof(ArchiveFormat))]
    public class CabOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CAB/PackDat3"; } }
        public override string Description { get { return "Entertainment Executive Engine resource archive"; } }
        public override uint     Signature { get { return 0x6B636150; } } // 'Pack'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "Dat3"))
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0xC;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x100);
                index_offset += 0x100;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0xC;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
