using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [000623][Mutation] Seiryaku ~Koukyuu de no Himegoto~

namespace GameRes.Formats.Mutation
{
    [Export(typeof(ArchiveFormat))]
    public class DpfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DPF"; } }
        public override string Description { get { return "Mutation resource archive"; } }
        public override uint     Signature { get { return 0x4C465044; } } // 'DPFL'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt16 (4);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 6;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x10);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x14);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
