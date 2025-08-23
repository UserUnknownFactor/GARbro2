using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Triangle
{
    [Export(typeof(ArchiveFormat))]
    public class IafOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IAF/MULTI"; } }
        public override string Description { get { return "route2 engine multi-frame image"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public IafOpener ()
        {
            Extensions = new string[] { "iaf" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".iaf")
                || file.MaxOffset < 0x20)
                return null;
            uint size = file.View.ReadUInt32 (1);
            if (size >= file.MaxOffset || size+0x19 >= file.MaxOffset)
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            long current_offset = 0;
            var dir = new List<Entry>();
            while (current_offset < file.MaxOffset)
            {
                uint packed_size = file.View.ReadUInt32 (current_offset+1);
                if (0 == packed_size || current_offset+packed_size+0x19 > file.MaxOffset)
                    return null;
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D3}.IAF", base_name, dir.Count),
                    Type = "image",
                    Offset = current_offset,
                    Size = packed_size + 0x19,
                };
                dir.Add (entry);
                current_offset += entry.Size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
