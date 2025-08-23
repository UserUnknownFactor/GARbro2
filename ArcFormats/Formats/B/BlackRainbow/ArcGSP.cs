using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.BlackRainbow
{
    [Export(typeof(ArchiveFormat))]
    public class GspOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GSP"; } }
        public override string Description { get { return "GSP resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_size = 0x40u * (uint)count;
            if (index_size > file.View.Reserve (4, index_size))
                return null;
            var dir = new List<Entry> (count);
            long index_offset = 4;
            long data_offset = index_offset + index_size;
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset+8, 0x38);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x40;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
