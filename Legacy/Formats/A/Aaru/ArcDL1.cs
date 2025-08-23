using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

// [980123][C's Ware] Divi-Dead

namespace GameRes.Formats.Csware
{
    [Export(typeof(ArchiveFormat))]
    public class Dl1Opener : ArchiveFormat
    {
        public override string         Tag { get { return "DL1"; } }
        public override string Description { get { return "C's ware resource archive"; } }
        public override uint     Signature { get { return 0x2E314C44; } } // 'DL1.0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "0\x1A"))
                return null;
            int count = file.View.ReadInt16 (8);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (0xA);
            if (index_offset >= file.MaxOffset)
                return null;
            long data_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0xC);
                var entry = Create<PackedEntry> (name);
                entry.Offset = data_offset;
                entry.Size = file.View.ReadUInt32 (index_offset+0xC);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                data_offset += entry.Size;
                index_offset += 0x10;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            if (!pent.IsPacked)
            {
                if (arc.File.View.AsciiEqual (entry.Offset, "LZ"))
                {
                    pent.IsPacked = true;
                    pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+6);
                }
                if (!pent.IsPacked)
                    return base.OpenEntry (arc, entry);
            }
            var input = arc.File.CreateStream (entry.Offset+10, entry.Size-10);
            return new LzssStream (input);
        }
    }
}
