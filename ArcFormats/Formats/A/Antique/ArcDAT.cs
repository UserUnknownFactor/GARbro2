using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Antique
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/ACHV"; } }
        public override string Description { get { return "An*tique resource archive"; } }
        public override uint     Signature { get { return 0x56484341; } } // 'ACHV'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0xC);
            if (!IsSaneCount (count))
                return null;
            uint names_length = file.View.ReadUInt32 (0x10);
            uint index_offset = 0x14;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Offset = file.View.ReadUInt32 (index_offset),
                    Size   = file.View.ReadUInt32 (index_offset+4),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10;
            }
            using (var names = file.CreateStream (index_offset, names_length))
            {
                foreach (var entry in dir)
                {
                    var name = names.ReadCString();
                    if (string.IsNullOrEmpty (name))
                        return null;
                    entry.Name = name;
                    entry.Type = FormatCatalog.Instance.GetTypeFromName (name);
                }
            }
            return new ArcFile (file, this, dir);
        }
    }
}
