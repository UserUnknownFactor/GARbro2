using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Utility;

namespace GameRes.Formats.Abel
{
    [Export(typeof(ArchiveFormat))]
    public class FilepakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/ABEL"; } }
        public override string Description { get { return "Abel resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadUInt32 (0) != file.MaxOffset)
                return null;
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint index_size = file.View.ReadUInt32 (8);
            int index_pos = file.View.ReadInt32 (0x14);
            if (index_size >= file.MaxOffset || index_pos >= file.MaxOffset)
                return null;
            var index = file.View.ReadBytes (0, index_size);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_pos = index.ToInt32 (index_pos);
                var name = Binary.GetCString (index, name_pos, index.Length - name_pos);
                name = name.TrimStart ('\\', '/');
                if (string.IsNullOrEmpty (name))
                    return null;
                var entry = Create<Entry> (name);
                entry.Offset = index.ToUInt32 (index_pos+4);
                entry.Size   = index.ToUInt32 (index_pos+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += 12;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
