using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Utility;

namespace GameRes.Formats.Witch
{
    [Export(typeof(ArchiveFormat))]
    public class SoundDataOpener : ArchiveFormat
    {
        public override string         Tag { get { return "VBD/SOUND"; } }
        public override string Description { get { return "Witch audio archive"; } }
        public override uint     Signature { get { return 0x4E554F53; } } // 'SOUNDDATE'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "SOUNDDATE "))
                return null;
            int count = file.View.ReadInt32 (0xA);
            if (!IsSaneCount (count))
                return null;
            long index_offset = 0xE;
            var dir = new List<Entry> (count);
            var name_buffer = new byte[0x100];
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                if (offset <= index_offset || offset > file.MaxOffset)
                    return null;
                int name_length = file.View.ReadInt32 (index_offset+4);
                if (name_length <= 0)
                    return null;
                if (name_length > name_buffer.Length)
                    name_buffer = new byte[name_length];
                var name = file.View.ReadString (index_offset+8, (uint)name_length);
                var entry = new Entry {
                    Name = name,
                    Type = "audio",
                    Offset = offset,
                };
                dir.Add (entry);
                index_offset += 8 + name_length;
            }
            ImageDataOpener.SetAdjacentEntriesSize (dir, file.MaxOffset);
            return new ArcFile (file, this, dir);
        }
    }
}
