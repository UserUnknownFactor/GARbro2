using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.DarkNiteSystem
{
    [Export(typeof(ArchiveFormat))]
    public class DnsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DNS"; } }
        public override string Description { get { return "DarkNiteSystem resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith ("data.dns", StringComparison.InvariantCultureIgnoreCase))
                return null;
            if (file.View.ReadUInt32 (8) != 0x8000) // first file offset
                return null;

            var dir = new List<Entry>();
            uint index_offset = 0;
            while (index_offset < 0x8000)
            {
                var name = file.View.ReadString (index_offset, 8);
                if (0 == name.Length)
                    break;
                var entry = new Entry {
                    Name   = Path.ChangeExtension (name, "S"),
                    Type   = "script",
                    Offset = file.View.ReadUInt32 (index_offset+8),
                    Size   = file.View.ReadUInt32 (index_offset+12),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] = (byte)-data[i];
            }
            return new BinMemoryStream (data, entry.Name);
        }
    }
}
