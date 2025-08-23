using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;

namespace GameRes.Formats.AdvSys
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/ADVSYS3"; } }
        public override string Description { get { return "AdvSys3 engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat")
                || !Path.GetFileName (file.Name).StartsWith ("arc", StringComparison.InvariantCultureIgnoreCase))
                return null;
            long current_offset = 0;
            var dir = new List<Entry>();
            while (current_offset < file.MaxOffset)
            {
                uint size = file.View.ReadUInt32 (current_offset);
                if (0 == size)
                    break;
                uint name_length = file.View.ReadUInt16 (current_offset+8);
                if (0 == name_length || name_length > 0x100)
                    return null;
                var name = file.View.ReadString (current_offset+10, name_length);
                if (0 == name.Length)
                    return null;
                current_offset += 10 + name_length;
                if (current_offset + size > file.MaxOffset)
                    return null;
                var entry = new Entry {
                    Name = name,
                    Offset = current_offset,
                    Size = size,
                };
                uint signature = file.View.ReadUInt32 (current_offset);
                if (file.View.AsciiEqual (current_offset+4, "GWD"))
                {
                    entry.Type = "image";
                    entry.Name = Path.ChangeExtension (entry.Name, "gwd");
                }
                else
                {
                    var res = AutoEntry.DetectFileType (signature);
                    entry.ChangeType (res);
                }
                dir.Add (entry);
                current_offset += size;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
