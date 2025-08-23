using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [980529][Brownie] Dawn Slave

namespace GameRes.Formats.Brownie
{
    [Export(typeof(ArchiveFormat))]
    public class NafOpener : ArchiveFormat
    {
        public override string         Tag { get { return "NAF"; } }
        public override string Description { get { return "Brownie/NenGollo resource archive"; } }
        public override uint     Signature { get { return 0x52422E31; } } // '1.BROWNIE/NenGollo'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "1.BROWNIE"))
                return null;
            int count = file.View.ReadInt32 (0x30);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = file.View.ReadUInt32 (0x34);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                var ext = file.View.ReadString (index_offset+0x10, 4);
                name = Path.ChangeExtension (name, ext);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x14);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x18);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
