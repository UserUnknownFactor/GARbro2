using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.TinkerBell
{
    [Export(typeof(ArchiveFormat))]
    public class P8Opener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/P8"; } }
        public override string Description { get { return "TinkerBell resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".pak"))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                if (!string.IsNullOrEmpty (name))
                {
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = file.View.ReadUInt32 (index_offset+0x18);
                    entry.Size   = file.View.ReadUInt32 (index_offset+0x10);
                    if (entry.Offset <= index_offset || !entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                index_offset += 0x1C;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
