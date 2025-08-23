using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Utility;

// [020726][Liddell] Garasu no Yakata ~Kimi ga Inai Yoru~

namespace GameRes.Formats.Liddell
{
    [Export(typeof(ArchiveFormat))]
    public class FlkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "FLK"; } }
        public override string Description { get { return "Liddell resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".flk"))
                return null;
            uint index_pos = 0;
            var buffer = file.View.ReadBytes (index_pos, 0x10);
            int base_offset = buffer[3] << 8;
            int next_offset = ((buffer[1] << 8 | buffer[0]) << 4) + base_offset;
            var dir = new List<Entry>();
            while (buffer[4] != 0)
            {
                uint tail_size = buffer[2];
                var name = Binary.GetCString (buffer, 4, 12);
                var entry = Create<Entry> (name);
                entry.Offset = next_offset;
                index_pos += 0x10;
                if (file.View.Read (index_pos, buffer, 0, 0x10) != 0x10)
                    return null;
                next_offset = ((buffer[3] << 16 | buffer[1] << 8 | buffer[0]) << 4) + base_offset;
                entry.Size = (uint)(next_offset - entry.Offset);
                if (tail_size != 0)
                    entry.Size += tail_size - 0x10;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
