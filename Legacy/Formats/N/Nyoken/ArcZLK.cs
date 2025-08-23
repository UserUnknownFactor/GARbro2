using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Nyoken
{
    [Export(typeof(ArchiveFormat))]
    public class ZlkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ZLK"; } }
        public override string Description { get { return "Nyotai Kougaku Kenkyuujo resource archive"; } }
        public override uint     Signature { get { return 0x204B4C5A; } } // 'ZLK '
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadInt32 (4);
            if (version <= 0 || version > 100)
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 12;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                uint size1  = file.View.ReadUInt32 (index_offset+4);
                uint size2  = file.View.ReadUInt32 (index_offset+8);
                byte flags  = file.View.ReadByte (index_offset+12);
                byte name_length = file.View.ReadByte (index_offset+13);
                index_offset += 14;
                var name = file.View.ReadString (index_offset, name_length);
                index_offset += name_length;
                var entry = Create<PackedEntry> (name);
                entry.Offset = offset;
                entry.Size   = size1;
                entry.UnpackedSize = size2;
                entry.IsPacked = flags != 0;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new ZLibStream (input, CompressionMode.Decompress);
        }
    }
}
