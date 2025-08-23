using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [020426][Winters] Kiss x 200 To Aru Bunkou no Hanashi

namespace GameRes.Formats.Winters
{
    [Export(typeof(ArchiveFormat))]
    public class IfxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IFX"; } }
        public override string Description { get { return "Winters resource archive"; } }
        public override uint     Signature { get { return 0x65; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".ifx") || file.MaxOffset <= 0x10000)
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry>();
            for (int i = 0, index_offset = 0x20; index_offset < 0x10000; index_offset += 0x10, ++i)
            {
                if (file.View.ReadUInt16 (index_offset) == 0)
                    continue;
                var name = string.Format ("{0}#{1:D5}", base_name, i);
                uint offset = file.View.ReadUInt32 (index_offset+4);
                var entry = AutoEntry.Create (file, offset, name);
                entry.Size = file.View.ReadUInt32 (index_offset+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
