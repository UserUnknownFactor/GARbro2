using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Bishop
{
    [Export(typeof(ArchiveFormat))]
    public class BscOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BSC"; } }
        public override string Description { get { return "Bishop composite image archive"; } }
        public override uint     Signature { get { return 0x2D535342; } } // 'BSS-'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public BscOpener ()
        {
            Extensions = new string[] { "bsc", "bsg" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "BSS-Composition\0"))
                return null;
            int count = file.View.ReadByte (0x11);
            if (0 == count)
                return null;

            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            long current_offset = 0x20;
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name    = string.Format ("{0}#{1:D3}.bsg", base_name, i),
                    Type    = "image",
                    Offset  = current_offset,
                    Size    = 0x40 + file.View.ReadUInt32 (current_offset+0x36),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                current_offset += entry.Size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
