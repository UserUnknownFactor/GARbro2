using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.BlackRainbow
{
    [Export(typeof(ArchiveFormat))]
    public class CcfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CCF"; } }
        public override string Description { get { return "BlackRainbow audio archive"; } }
        public override uint     Signature { get { return 0x22664343; } } // 'CCf"'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public CcfOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_offset = 8;
            long base_offset = index_offset + count * 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                index_offset += 4;
                var name = string.Format ("{0}#{1:D4}", base_name, i);
                var entry = AutoEntry.Create (file, base_offset+offset, name);
                if (entry.Offset >= file.MaxOffset)
                    return null;
                dir.Add (entry);
            }
            for (int i = 1; i < dir.Count; ++i)
            {
                dir[i-1].Size = (uint)(dir[i].Offset - dir[i-1].Offset);
            }
            var last_entry = dir[dir.Count-1];
            last_entry.Size = (uint)(file.MaxOffset - last_entry.Offset);
            return new ArcFile (file, this, dir);
        }
    }
}
