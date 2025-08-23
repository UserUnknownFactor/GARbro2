using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.RSystem
{
    [Export(typeof(ArchiveFormat))]
    public class RadOpener : ArchiveFormat
    {
        public override string         Tag { get { return "RAD"; } }
        public override string Description { get { return "RSystem engine multi-frame image"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".rad"))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_offset = 4;
            var dir = new List<Entry> (count);
            uint next_offset = file.View.ReadUInt32 (index_offset);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 4;
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D3}", base_name, i),
                    Type = "image",
                    Offset = next_offset,
                };
                next_offset = i+1 < count ? file.View.ReadUInt32 (index_offset) : (uint)file.MaxOffset;
                entry.Size = (uint)(next_offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
