using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [020531][Lust] Fall Down ~Ochita Tenshi no Monogatari~

namespace GameRes.Formats.AlphaSystem
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/ALPHA"; } }
        public override string Description { get { return "Alpha System resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            ContainedFormats = new[] { "SFG", "WAV" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            int first_offset = file.View.ReadInt32 (0x30);
            if (first_offset != count * 0x30 + 4)
                return null;
            int index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x20);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = Create<Entry> (name);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x24);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x2C);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x30;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
