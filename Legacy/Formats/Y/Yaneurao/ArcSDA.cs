using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

// [040716][Squadra D] Auction

namespace GameRes.Formats.Yaneurao
{
    [Export(typeof(ArchiveFormat))]
    public class SdaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SDA/yane"; } }
        public override string Description { get { return "YaneSDK2 resource archive"; } }
        public override uint     Signature { get { return 0x41445153; } } // 'SQDARC'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "SQDARC"))
                return null;
            int dir_count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (dir_count))
                return null;
            var dir = new List<Entry>();
            uint index_offset = 0x14;
            for (int i = 0; i < dir_count; ++i)
            {
                uint dir_offset = file.View.ReadUInt32 (index_offset);
                int file_count = file.View.ReadInt32 (index_offset+4);
                var dir_name = file.View.ReadString (index_offset+8, 10);
                var ext = file.View.ReadString (index_offset+0x12, 4);
                for (int j = 0; j < file_count; ++j)
                {
                    var name = file.View.ReadString (dir_offset, 0x28);
                    name = Path.Combine (dir_name, name);
                    name = Path.ChangeExtension (name, ext);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = file.View.ReadUInt32 (dir_offset+0x28);
                    entry.Size   = file.View.ReadUInt32 (dir_offset+0x2C);
                    dir_offset += 0x30;
                    dir.Add (entry);
                }
                index_offset += 0x18;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            return new LzssStream (input);
        }
    }
}
