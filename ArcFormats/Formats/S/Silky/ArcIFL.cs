using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Silky
{
    [Export(typeof(ArchiveFormat))]
    public class IflOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IFL"; } }
        public override string Description { get { return "Silky's engine resource archive"; } }
        public override uint     Signature { get { return 0x534c4649; } } // 'IFLS'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint data_offset = file.View.ReadUInt32 (4);
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count) || data_offset <= 12 || data_offset >= file.MaxOffset)
                return null;
            var dir = new List<Entry> (count);
            long index_offset = 12;
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0x10);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x10);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x14);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            if (!pent.IsPacked)
            {
                if (entry.Size <= 12
                    || entry.Name.HasExtension (".grd") // let GrdFormat unpack images
                    || !arc.File.View.AsciiEqual (entry.Offset, "CMP_"))
                    return arc.File.CreateStream (entry.Offset, entry.Size);
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+4);
            }
            using (var input = arc.File.CreateStream (entry.Offset+12, entry.Size-12))
            using (var lzss = new LzssReader (input, (int)input.Length, (int)pent.UnpackedSize))
            {
                lzss.FrameFill = 0x20;
                lzss.Unpack();
                return new BinMemoryStream (lzss.Data, entry.Name);
            }
        }
    }
}
