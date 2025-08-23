using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.KAAS
{
    [Export(typeof(ArchiveFormat))]
    public class PbOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PB"; } }
        public override string Description { get { return "KAAS engine audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".pb"))
                return null;
            int count = file.View.ReadInt32 (0);
            if (count <= 0 || count > 0xfff)
                return null;
            var dir = new List<Entry> (count);
            int index_offset = 0x10;
            bool is_voice = VFS.IsPathEqualsToFileName (file.Name, "voice.pb");
            int data_offset = index_offset + 8 * count;
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                Entry entry;
                if (!is_voice)
                    entry = new Entry { Name = i.ToString ("D4"), Type = "audio", Offset = offset };
                else
                    entry = new Entry { Name = string.Format ("{0:D4}.pb", i), Type = "archive", Offset = offset };
                entry.Size = file.View.ReadUInt32 (index_offset + 4);
                if (offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
