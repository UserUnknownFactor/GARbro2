using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Ivory
{
    [Export(typeof(ArchiveFormat))]
    public class PxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PX/IVORY"; } }
        public override string Description { get { return "Ivory audio archive"; } }
        public override uint     Signature { get { return 0x20585066; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset != file.View.ReadUInt32 (4))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            long offset = 8;
            var dir = new List<Entry>();
            while (offset < file.MaxOffset)
            {
                if (!file.View.AsciiEqual (offset, "cTRK"))
                    break;
                uint size = file.View.ReadUInt32 (offset+4);
                if (0 == size)
                    return null;
                int num = file.View.ReadInt32 (offset+8);
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}.trk", base_name, num),
                    Type = "audio",
                    Offset = offset,
                    Size = size
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                offset += size;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
