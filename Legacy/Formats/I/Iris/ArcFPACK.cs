using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [000728][iris] 聖白薔薇学園プチ☆MAHJONG

namespace GameRes.Formats.Iris
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/FPACK"; } }
        public override string Description { get { return "Iris resource archive"; } }
        public override uint     Signature { get { return 0x43415046; } } // 'FPACK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "K\0"))
                return null;
            if (file.View.ReadUInt32 (8) != file.MaxOffset)
                return null;
            int count = file.View.ReadInt16 (6);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                if (string.IsNullOrEmpty (name))
                    return null;
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
