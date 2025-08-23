using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Maika
{
    [Export(typeof(ArchiveFormat))]
    public class MikOpener : Mk2Opener
    {
        public override string         Tag { get { return "DAT/MIK01"; } }
        public override string Description { get { return "MAIKA resource archive"; } }
        public override uint     Signature { get { return 0x304B494D; } } // 'MIK01'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public MikOpener ()
        {
            Signatures = new uint[] { 0x304B494D, 0x30475355 }; // 'MIK01', 'USG01'
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "1\x1A\0"))
                return null;
            int count = file.View.ReadInt16 (8);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (0xA);
            long offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0xC);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                entry.Size = file.View.ReadUInt32 (index_offset+0xC);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                offset += entry.Size;
                index_offset += 0x10;
            }
            return GetArchive (file, dir);
        }
    }
}
