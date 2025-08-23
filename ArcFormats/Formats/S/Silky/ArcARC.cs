using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Silky
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/SILKY'S"; } }
        public override string Description { get { return "Silky's resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            long index_offset = 4;
            const int name_length = 0x20;
            uint index_size = (uint)(count * (name_length + 8));
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            var seen_offsets = new HashSet<uint>();
            var dir = new List<Entry>();
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, (uint)name_length);
                if (0 == name.Length)
                    return null;
                index_offset += name_length;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                if (entry.Offset < index_size+4 || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (!seen_offsets.Add ((uint)entry.Offset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
