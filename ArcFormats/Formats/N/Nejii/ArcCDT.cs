using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Nejii
{
    [Export(typeof(ArchiveFormat))]
    public class CdtOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CDT"; } }
        public override string Description { get { return "NEJII engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public CdtOpener ()
        {
            Extensions = new string[] { "cdt", "pdt", "vdt", "ovd" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset <= 12 || !file.View.AsciiEqual (file.MaxOffset-12, "RK1\0"))
                return null;
            int count = file.View.ReadInt32 (file.MaxOffset-8);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (file.MaxOffset-4);
            if (index_offset >= file.MaxOffset)
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                index_offset += 0x10;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Size          = file.View.ReadUInt32 (index_offset);
                entry.UnpackedSize  = file.View.ReadUInt32 (index_offset+4);
                entry.IsPacked      = file.View.ReadInt32 (index_offset+8) != 0;
                entry.Offset        = file.View.ReadUInt32 (index_offset+12);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                index_offset += 0x10;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (null != pent && pent.IsPacked)
                input = new LzssStream (input);
            return input;
        }
    }
}
