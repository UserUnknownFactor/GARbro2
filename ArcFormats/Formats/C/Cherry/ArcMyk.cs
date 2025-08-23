using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Cherry
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/CHERRY"; } }
        public override string Description { get { return "Cherry Soft resource archive"; } }
        public override uint     Signature { get { return 0x304B594D; } } // 'MYK0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
            Signatures = new uint[] { 0x304B594D, 0x30454843 }; // 'MYK0', 'CHE0'
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0x1A30 != file.View.ReadUInt16 (4))
                return null;
            int count = file.View.ReadUInt16 (8);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (0xA);
            if (index_offset >= file.MaxOffset)
                return null;
            uint index_size = (uint)count * 0x10u;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            long base_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0xC);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = base_offset;
                entry.Size = file.View.ReadUInt32 (index_offset+0xC);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                base_offset += entry.Size;
                index_offset += 0x10;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
