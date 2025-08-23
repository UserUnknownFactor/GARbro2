using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.DigitalWorks
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/HED"; } }
        public override string Description { get { return "Digital Works resource archive"; } }
        public override uint     Signature { get { return 0; } } // 'PPAC-PAC'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "PPAC-PAC") && !file.View.AsciiEqual(0, "FANA_V1.0.0.0"))
                return null;
            var hed_name = Path.ChangeExtension (file.Name, "hed");
            if (!VFS.FileExists (hed_name))
                return null;
            using (var hed = VFS.OpenView (hed_name))
            {
                if (!hed.View.AsciiEqual (0, "PPAC-HED") && !hed.View.AsciiEqual(0, "FANA_V1.0.0.0"))
                    return null;
                uint index_offset = 0x10;
                const uint data_offset = 0x10;
                int count = (int)(hed.MaxOffset - index_offset) / 0x20;
                if (!IsSaneCount (count))
                    return null;
                var dir = new List<Entry> (count);
                while (index_offset+0x20 <= hed.MaxOffset)
                {
                    var name = hed.View.ReadString (index_offset, 0x10);
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.Offset = hed.View.ReadUInt32 (index_offset+0x10) + data_offset;
                    entry.Size   = hed.View.ReadUInt32 (index_offset+0x14);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_offset += 0x20;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent)
                return base.OpenEntry (arc, entry);
            if (!pent.IsPacked)
            {
                if (!arc.File.View.AsciiEqual (entry.Offset, "LZS\0"))
                    return base.OpenEntry (arc, entry);
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+4);
            }
            var input = arc.File.CreateStream (entry.Offset+8, entry.Size-8);
            bool embedded_lzs = (input.Signature & ~0xF0u) == 0x535A4C0F; // 'LZS'
            var lzs = new LzssStream (input);
            if (embedded_lzs)
            {
                var header = new byte[8];
                lzs.Read (header, 0, 8);
                pent.UnpackedSize = header.ToUInt32 (4);
                lzs = new LzssStream (lzs);
            }
            return lzs;
        }
    }
}
