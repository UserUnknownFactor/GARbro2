using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.MicroVision
{
    [Export(typeof(ArchiveFormat))]
    public class GsdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GSD"; } }
        public override string Description { get { return "MicroVision audio resource archive"; } }
        public override uint     Signature { get { return 0x00445347; } } // 'GSD'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0x1C);
            if (!IsSaneCount (count))
                return null;
            long base_offset = file.View.ReadUInt32 (8);
            uint index_offset = 0x34;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0:D5}.wav", i),
                    Type = "audio",
                    Offset = base_offset + file.View.ReadUInt32 (index_offset),
                    Size = file.View.ReadUInt32 (index_offset+4)
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
