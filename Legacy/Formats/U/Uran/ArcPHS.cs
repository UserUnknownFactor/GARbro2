using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Uran
{
    [Export(typeof(ArchiveFormat))]
    public class PhsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PHS"; } }
        public override string Description { get { return "KuonAdv engine resource archive"; } }
        public override uint     Signature { get { return 0x5348504B; } } // 'KPHS'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0xC);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x50;
            long data_offset = index_offset + count * 0x14;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0xC);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0xC) + data_offset;
                entry.Size   = file.View.ReadUInt32 (index_offset+0x10);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x14;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
