using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;

namespace GameRes.Formats.Sceplay
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/SCEPLAY"; } }
        public override string Description { get { return "Sceplay engine resource archive"; } }
        public override uint     Signature { get { return 0x006B6170; } } // 'pak'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            using (var index = file.CreateStream())
            {
                index.Position = 8;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadCString (0x18);
                    if (string.IsNullOrEmpty (name))
                        return null;
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    dir.Add (entry);
                }
                for (int i = 0; i < count; ++i)
                    dir[i].Size = index.ReadUInt32();
                for (int i = 0; i < count; ++i)
                    dir[i].Offset = index.ReadUInt32();
                dir = dir.Where (e => e.Offset != uint.MaxValue).ToList();
                if (dir.Any (e => !e.CheckPlacement (file.MaxOffset)))
                    return null;
                return new ArcFile (file, this, dir);
            }
        }
    }
}
