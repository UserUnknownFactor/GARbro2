using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Elf
{
    [Export(typeof(ArchiveFormat))]
    public class VolOpener : ArchiveFormat
    {
        public override string         Tag { get { return "VOL/ELF"; } }
        public override string Description { get { return "Ancient elf resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public VolOpener ()
        {
            Extensions = new string[] { "vol" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".vol"))
                return null;
            uint first_offset = file.View.ReadUInt32 (0);
            if (first_offset < 0x10 || 0 != (first_offset & 0xF) || first_offset >= file.MaxOffset)
                return null;
            int count = (int)(first_offset /4);
            if (!IsSaneCount (count))
                return null;

            var offset_table = new List<uint> (count);
            offset_table.Add (first_offset);
            uint index_offset = 4;
            for (int i = 1; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                if (offset < offset_table[i-1] || offset > file.MaxOffset)
                    return null;
                offset_table.Add (offset);
                if (offset == file.MaxOffset)
                    break;
                index_offset += 4;
            }
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (offset_table.Count-1);
            for (int i = 0; i < offset_table.Count-1; ++i)
            {
                uint size = offset_table[i+1] - offset_table[i];
                if (0 == size)
                    continue;
                var name = string.Format ("{0}#{1:D4}", base_name, i);
                var entry = AutoEntry.Create (file, offset_table[i], name);
                entry.Size = size;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
