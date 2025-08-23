using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Weapon
{
//    [Export(typeof(ArchiveFormat))]
    public class VoiceOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/W/VOICE"; } }
        public override string Description { get { return "Weapon audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_size = (uint)count * 4 + 8;
            if (index_size > file.View.Reserve (0, index_size))
                return null;
            if (file.View.ReadUInt32 (index_size-4) != file.MaxOffset)
                return null;

            uint index_offset = 4;
            uint offset = file.View.ReadUInt32 (index_offset);
            if (offset < index_size)
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 4;
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}.wav", base_name, i),
                    Type = "audio",
                    Offset = offset,
                };
                offset = file.View.ReadUInt32 (index_offset);
                entry.Size = (uint)(offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
