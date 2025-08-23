using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Kogado
{
    [Export(typeof(ArchiveFormat))]
    public class LpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LPK/KOGADO"; } }
        public override string Description { get { return "Kogado resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".lpk") || file.MaxOffset < 0x2800)
                return null;
            uint index_offset = 0;
            var dir = new List<Entry> ();
            for (int i = 0; i < 0x200; ++i)
            {
                if (file.View.ReadByte (index_offset) == 0)
                    break;
                var name = file.View.ReadString (index_offset, 0x10);
                if (!IsValidEntryName (name))
                    return null;
                index_offset += 0x10;
                var entry = Create<Entry> (name);
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            index_offset = 0x2000;
            uint base_offset = 0x2800;
            uint offset = file.View.ReadUInt32 (index_offset);
            foreach (var entry in dir)
            {
                index_offset += 4;
                uint next_offset = file.View.ReadUInt32 (index_offset);
                uint size = next_offset - offset;
                entry.Offset = offset + base_offset;
                entry.Size = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                offset = next_offset;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
