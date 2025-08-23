using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.SysD
{
    [Export(typeof(ArchiveFormat))]
    public class DpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DPK/SYSD"; } }
        public override string Description { get { return "SYSD engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "PA"))
                return null;
            int count = file.View.ReadInt16 (2);
            if (!IsSaneCount (count))
                return null;
            if (file.MaxOffset != file.View.ReadUInt32 (4))
                return null;

            uint index_offset = 8;
            long data_offset = index_offset + 0x14 * (uint)count;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = data_offset;
                entry.Size   = file.View.ReadUInt32 (index_offset+0x10);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x14;
                data_offset += entry.Size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
