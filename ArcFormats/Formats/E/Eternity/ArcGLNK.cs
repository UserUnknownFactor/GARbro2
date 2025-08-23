using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Miris
{
    [Export(typeof(ArchiveFormat))]
    public class GlnkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GLNK/MIRIS"; } }
        public override string Description { get { return "Studio Miris resource archive"; } }
        public override uint     Signature { get { return 0x4B4E4C47; } } // 'GLNK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public GlnkOpener ()
        {
            Extensions = new string[] { "dat", "glk", "mlk", "slk", "gl", "ml", "sl", "ets", "etg", "etm" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadUInt16 (4);
            int count = file.View.ReadInt32 (6);
            if (!IsSaneCount (count))
                return null;
            long index_offset = file.View.ReadUInt32 (0xA);
            int index_length = file.View.ReadInt32 (0xE);
            if (index_length > file.View.Reserve (index_offset, (uint)index_length))
                return null;
            int entry_size = version >= 0x6E ? 12 : 8;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (index_length <= 0)
                    return null;
                int name_length = file.View.ReadByte (index_offset++);
                string name = file.View.ReadString (index_offset, (uint)name_length);
                index_offset += name_length;
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += entry_size;
                index_length -= 1+name_length + entry_size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
