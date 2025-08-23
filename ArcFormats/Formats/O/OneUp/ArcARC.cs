using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;

namespace GameRes.Formats.OneUp
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/ONE-UP"; } }
        public override string Description { get { return "One-up resource archive"; } }
        public override uint     Signature { get { return 0x43524100; } } // '\x00ARC'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            long data_offset = file.View.ReadUInt32 (4);
            if (data_offset >= file.MaxOffset)
                return null;

            var name_buf = new byte[0x100];
            var dir = new List<Entry> (count);
            uint index_offset = 0xC;
            for (int i = 0; i < count; ++i)
            {
                uint name_length = file.View.ReadUInt32 (index_offset);
                index_offset += 4;
                if (name_length > 0x400)
                    return null;
                if (name_length > (uint)name_buf.Length)
                    name_buf = new byte[name_length];
                file.View.Read (index_offset, name_buf, 0, name_length);
                var name = Encoding.Unicode.GetString (name_buf, 0, (int)name_length);
                index_offset += name_length;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = data_offset;
                entry.Size = file.View.ReadUInt32 (index_offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                data_offset += entry.Size;
                index_offset += 4;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
