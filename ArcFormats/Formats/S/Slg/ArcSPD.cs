using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Slg
{
    [Export(typeof(ArchiveFormat))]
    public class SpdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SPD/SLG"; } }
        public override string Description { get { return "SLG system audio archive"; } }
        public override uint     Signature { get { return 0x504653; } } // 'SFP'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.Name.HasExtension (".SPL"))
                return null;
            var index_name = Path.ChangeExtension (file.Name, ".SPL");
            if (!VFS.FileExists (index_name))
                return null;
            using (var idx = VFS.OpenView (index_name))
            {
                if (!idx.View.AsciiEqual (0, "SFP\0"))
                    return null;
                uint align = idx.View.ReadUInt32 (0xC);
                uint index_offset = 0x20;
                uint names_offset = idx.View.ReadUInt32 (index_offset);
                if (names_offset > idx.MaxOffset || names_offset <= index_offset)
                    return null;
                int count = (int)(names_offset - index_offset) / 0x10;
                if (!IsSaneCount (count))
                    return null;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    uint name_offset = idx.View.ReadUInt32 (index_offset);
                    var name = idx.View.ReadString (name_offset, (uint)(idx.MaxOffset - name_offset));
                    if (string.IsNullOrWhiteSpace (name))
                        return null;
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Size = idx.View.ReadUInt32 (index_offset+4);
                    entry.Offset = (long)idx.View.ReadUInt32 (index_offset+8) * align;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_offset += 0x10;
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
