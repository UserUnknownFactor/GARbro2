using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Herb
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/HERB"; } }
        public override string Description { get { return "Herb Soft resource archive"; } }
        public override uint     Signature { get { return 0x2E; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            ContainedFormats = new[] { "GRP/HERB", "WAV", "TXT" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            long base_offset = 0x20000;
            if (file.MaxOffset <= base_offset || !file.View.AsciiEqual (0x40, "..\0"))
                return null;
            long index_pos = 0x80;
            var dir = new List<Entry>();
            while (index_pos < base_offset && file.View.ReadByte (index_pos) != 0)
            {
                var name = file.View.ReadString (index_pos, 0x30);
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_pos + 0x30) + base_offset;
                entry.Size   = file.View.ReadUInt32 (index_pos + 0x38);
                if (entry.Size != 0)
                {
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                index_pos += 0x40;
            }
            if (dir.Count == 0)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
