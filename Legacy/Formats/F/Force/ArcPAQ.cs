using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Force
{
    [Export(typeof(ArchiveFormat))]
    public class PaqOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAQ/FORCE"; } }
        public override string Description { get { return "Force resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".paq"))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_offset = 8;
            long data_offset = 4 + (uint)count * 8;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = string.Format ("{0}#{1:D4}", base_name, i);
                var entry = AutoEntry.Create (file, data_offset, name);
                entry.Size = file.View.ReadUInt32 (index_offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
                data_offset += entry.Size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
