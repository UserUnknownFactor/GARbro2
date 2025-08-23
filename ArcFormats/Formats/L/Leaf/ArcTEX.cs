using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Leaf
{
    [Export(typeof(ArchiveFormat))]
    public class TexOpener : ArchiveFormat
    {
        public override string         Tag { get { return "TEX/LEAF"; } }
        public override string Description { get { return "Leaf textures archive"; } }
        public override uint     Signature { get { return 0x20584554; } } // 'TEX '
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public TexOpener ()
        {
            Extensions = new string[] { "tex" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "PACK0.02"))
                return null;
            int count = file.View.ReadInt32 (12);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x20;
            long base_offset = index_offset + 0x28 * count;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x20);
                index_offset += 0x20;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset) + base_offset;
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
