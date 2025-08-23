using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Ankh
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : GrpOpener
    {
        public override string         Tag { get { return "DAT/ANKH"; } }
        public override string Description { get { return "Ankh resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count) || !file.Name.HasExtension (".dat"))
                return null;
            uint first_offset = file.View.ReadUInt32 (0x14);
            if (first_offset != 4 + count * 0x14)
                return null;
            uint index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0xC);
                if (string.IsNullOrEmpty (name))
                    return null;
                var entry = new PackedEntry {
                    Name   = name,
                    Size   = file.View.ReadUInt32 (index_offset+0xC),
                    Offset = file.View.ReadUInt32 (index_offset+0x10),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x14;
            }
            DetectFileTypes (file, dir);
            return new ArcFile (file, this, dir);
        }
    }
}
