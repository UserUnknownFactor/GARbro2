using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Circus
{
    internal class CrmArchive : ArcFile
    {
        readonly IDictionary<uint, Entry> OffsetMap;

        public CrmArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IDictionary<uint, Entry> offset_map)
            : base (arc, impl, dir)
        {
            OffsetMap = offset_map;
        }

        internal IBinaryStream OpenByOffset (uint offset)
        {
            Entry entry;
            if (!OffsetMap.TryGetValue (offset, out entry))
                throw new FileNotFoundException();
            return OpenBinaryEntry (entry);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class CrmOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CRM"; } }
        public override string Description { get { return "Circus image archive"; } }
        public override uint     Signature { get { return 0x42585243; } } // 'CRXB'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;

            var offset_map = new SortedDictionary<uint, Entry>();
            int index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                var name = file.View.ReadString (index_offset+8, 0x18);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                dir.Add (entry);
                offset_map[offset] = entry;
                index_offset += 0x20;
            }
            using (var iterator = offset_map.GetEnumerator())
            {
                if (iterator.MoveNext())
                {
                    for (;;)
                    {
                        var entry = iterator.Current.Value;
                        if (!iterator.MoveNext())
                        {
                            entry.Size = (uint)(file.MaxOffset - entry.Offset);
                            break;
                        }
                        entry.Size = (uint)(iterator.Current.Key - entry.Offset);
                    }
                }
            }
            return new CrmArchive (file, this, dir, offset_map);
        }
    }
}
