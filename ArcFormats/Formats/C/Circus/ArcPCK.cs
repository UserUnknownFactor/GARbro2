using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Circus
{
    [Export(typeof(ArchiveFormat))]
    public class PckOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PCK/CIRCUS"; } }
        public override string Description { get { return "Circus resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PckOpener ()
        {
            Extensions = new string[] { "pck", "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            int index_size = count * 0x48 + 4;
            uint first_offset = file.View.ReadUInt32 (4);
            if (first_offset < index_size || first_offset >= file.MaxOffset)
                return null;
            int index_offset = 4 + count * 8;
            file.View.Reserve (index_offset, (uint)count * 0x40);
            if (first_offset != file.View.ReadUInt32 (index_offset+0x38))
                return null;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x38);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x38);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x3C);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x40;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
