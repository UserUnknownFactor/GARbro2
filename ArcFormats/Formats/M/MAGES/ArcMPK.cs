using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.MAGES
{
    [Export(typeof(ArchiveFormat))]
    public class MpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MPK/MAGES"; } }
        public override string Description { get { return "MAGES engine resource archive"; } }
        public override uint     Signature { get { return 0x4B504D; } } // 'MPK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint version_temp = file.View.ReadUInt32 (4);
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            var dir = new List<Entry> (count);
            uint index_offset = 0;
            if (version_temp == 65536)
            {
                index_offset = 0x44;
                for (int i = 0; i < count; ++i)
                {
                    var name = file.View.ReadString (index_offset+0x1c, 0xE0);
                    var entry = Create<PackedEntry> (name);
                    entry.Offset = file.View.ReadInt32 (index_offset);
                    entry.Size = file.View.ReadUInt32 (index_offset+4);
                    entry.UnpackedSize = file.View.ReadUInt32 (index_offset+8);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_offset += 0x100;
                }
            }
            else
            {
                index_offset = 0x48;
                for (int i = 0; i < count; ++i)
                {
				    var name = file.View.ReadString (index_offset+0x18, 0xE0);
                    var entry = Create<PackedEntry> (name);
                    entry.Offset = file.View.ReadInt64 (index_offset);
                    entry.Size = file.View.ReadUInt32 (index_offset+8);
                    entry.UnpackedSize = file.View.ReadUInt32 (index_offset+0x10);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_offset += 0x100;
                }
            }
            return new ArcFile (file, this, dir);
        }
    }
}
