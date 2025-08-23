using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Triangle
{
    [Export(typeof(ArchiveFormat))]
    public class SudOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SUD"; } }
        public override string Description { get { return "Triangle audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "OggS"))
                return null;

            uint current_offset = 0;
            uint first_size = file.View.ReadUInt32 (current_offset);
            if (first_size >= file.MaxOffset)
                return null;

            var dir = new List<Entry>();
            int n = 0;
            while (current_offset < file.MaxOffset)
            {
                uint size = file.View.ReadUInt32 (current_offset);
                if (current_offset + 4 + (long)size > file.MaxOffset)
                    return null;
                if (file.View.AsciiEqual (current_offset+4, "OggS"))
                {
                    var entry = new Entry {
                        Name    = string.Format ("{0:D5}.ogg", n++),
                        Type    = "audio",
                        Offset  = current_offset + 4,
                        Size    = size,
                    };
                    dir.Add (entry);
                }
                current_offset += 4+size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
