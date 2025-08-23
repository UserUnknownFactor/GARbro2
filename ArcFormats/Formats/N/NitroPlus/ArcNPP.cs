using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.NitroPlus
{
    [Export(typeof(ArchiveFormat))]
    public class NppOpener : ArchiveFormat
    {
        public override string         Tag { get { return "NPP"; } }
        public override string Description { get { return "Nitro+ resource archive"; } }
        public override uint     Signature { get { return 0x5074696E; } } // 'nitP'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 8;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var subdir = file.View.ReadString (index_offset+0x10, 0x40);
                var name = file.View.ReadString (index_offset+0x50, 0x40);
                var entry = Create<PackedEntry> (Path.Combine (subdir, name));
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+8);
                entry.IsPacked = file.View.ReadInt16 (index_offset+12) != 0;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x90;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset, entry.Size, entry.Name);
            return new LzssStream (input);
        }
    }
}
