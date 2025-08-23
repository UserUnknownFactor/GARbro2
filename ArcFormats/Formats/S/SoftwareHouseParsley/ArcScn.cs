using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Parsley
{
    [Export(typeof(ArchiveFormat))]
    public class ScnDatOpener : ArchiveFormat
    {
        public override string         Tag { get => "DAT/SCN"; }
        public override string Description { get => "Software House Parsley scenario archive"; }
        public override uint     Signature { get => 0; }
        public override bool  IsHierarchic { get => false; }
        public override bool      CanWrite { get => false; }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!VFS.IsPathEqualsToFileName (file.Name, "scn.dat"))
                return null;
            uint base_offset = 0x1400;
            uint index_pos = 0;
            var dir = new List<Entry>();
            while (index_pos < base_offset && file.View.ReadByte (index_pos) != 0)
            {
                var name = file.View.ReadString (index_pos, 0x20);
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_pos+0x20) + base_offset;
                entry.Size   = file.View.ReadUInt32 (index_pos+0x24);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += 0x28;
            }
            if (dir.Count == 0)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
