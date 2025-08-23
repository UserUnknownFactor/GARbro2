using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.PlanTech
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/PLANTECH"; } }
        public override string Description { get { return "PLANTECH engine bitmap package"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadInt32 (0) != 0)
                return null;
            if (!file.View.AsciiEqual (8, "BM"))
                return null;
            if (file.View.ReadUInt32 (4) != file.View.ReadUInt32 (10))
                return null;

            var dir = new List<Entry> (1);
            var entry = new Entry {
                Name = Path.GetFileNameWithoutExtension (file.Name) + ".BMP",
                Type = "image",
                Offset = 8,
                Size = (uint)(file.MaxOffset - 8),
            };
            dir.Add (entry);
            return new ArcFile (file, this, dir);
        }
    }
}
