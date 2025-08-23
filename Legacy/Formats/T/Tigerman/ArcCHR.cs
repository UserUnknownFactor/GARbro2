using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Tigerman
{
    [Export(typeof(ArchiveFormat))]
    public class ChrOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CHR/TIGERMAN"; } }
        public override string Description { get { return "Tigerman Project compound image"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public ChrOpener ()
        {
            Extensions = new string[] { "chr", "cls", "ev" };
            Signatures = new uint[] { 0x01B1, 0 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint base_offset = file.View.ReadUInt32 (0);
            if (base_offset >= file.MaxOffset || !file.View.AsciiEqual (base_offset, "ZT"))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry>();
            Func<int, uint, uint, Entry> create_entry = (i, offset, size) => new Entry {
                Name = string.Format ("{0}#{1}.ZIT", base_name, i),
                Type = "image",
                Offset = offset,
                Size = size,
            };
            dir.Add (create_entry (0, base_offset, file.View.ReadUInt32 (4)));
            uint index_offset = 12;
            while (index_offset + 0x24 <= base_offset)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                if (offset != 0)
                {
                    uint size   = file.View.ReadUInt32 (index_offset+4);
                    var entry = create_entry (dir.Count, offset, size);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                index_offset += 0x24;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
