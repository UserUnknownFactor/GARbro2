using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Formats.Maika;
using GameRes.Utility;

namespace GameRes.Formats.BellDa
{
    [Export(typeof(ArchiveFormat))]
    public sealed class BldOpener : Mk2Opener
    {
        public override string         Tag { get { return "DAT/BLD"; } }
        public override string Description { get { return "BELL-DA resource archive"; } }
        public override uint     Signature { get { return 0x30444C42; } } // 'BLD0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public BldOpener ()
        {
            Signatures = new[] { this.Signature };
            Settings = null;
            Scheme = null;
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var version_str = file.View.ReadString (4, 4).TrimEnd ('\x1A');
            if (version_str != "0" && version_str != "1" && version_str != "12" && version_str != "3")
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
                entry.Size   = file.View.ReadUInt32 (index_offset+0xC);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                index_offset += 0x10;
                data_offset += entry.Size;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
