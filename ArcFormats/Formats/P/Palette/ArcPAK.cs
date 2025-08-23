using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Utility;

namespace GameRes.Formats.Palette
{
    [Export(typeof(ArchiveFormat))]
    public class FilePackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/FilePack"; } }
        public override string Description { get { return "Palette resource archive"; } }
        public override uint     Signature { get { return 0x656C6946; } } // 'FilePack'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public FilePackOpener ()
        {
            ContainedFormats = new[] { "PGA", "CHR/Palette", "OGG", "WAV" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "Pack"))
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0xC;
            var name_buf = new byte[0x20];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (index_offset, name_buf, 0, 0x20);
                for (int j = 0; j < 0x20; ++j)
                    name_buf[j] ^= 0xFF;
                var name = Binary.GetCString (name_buf, 0);
                if (string.IsNullOrEmpty (name))
                    return null;
                var entry = Create<Entry> (name);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x20);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x24);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x28;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
