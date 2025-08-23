using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Gpk2
{
    [Export(typeof(ArchiveFormat))]
    public class GpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GPK2"; } }
        public override string Description { get { return "GPK2 resource archive"; } }
        public override uint     Signature { get { return 0x324B5047; } } // 'GPK2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public GpkOpener ()
        {
            Extensions = new string[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_offset = file.View.ReadUInt32 (4);
            if (index_offset >= file.MaxOffset)
                return null;
            int count = file.View.ReadInt32 (index_offset);
            if (!IsSaneCount (count))
                return null;
            index_offset += 4;
            uint index_size = (uint)(count * 0x88);
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset+8, 0x80);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x88;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
