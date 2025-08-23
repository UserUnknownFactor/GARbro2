using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [000708][Witch] Milkyway

namespace GameRes.Formats.Witch
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/WITCH"; } }
        public override string Description { get { return "Witch resource archive"; } }
        public override uint     Signature { get { return 0x20435241; } } // 'ARC '
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadInt32 (4) != 0)
                return null;
            using (var index = file.CreateStream())
            {
                long index_pos = 0x10;
                var id = new byte[8];
                var dir = new List<Entry>();
                for (;;)
                {
                    index.Position = index_pos;
                    if (8 != index.Read (id, 0, 8))
                        return null;
                    if (!id.AsciiEqual ("DIR \0\0\0\0"))
                        break;
                    index.ReadInt32();
                    int name_length = index.ReadInt32();
                    if (name_length <= 0)
                        return null;
                    index.Seek (0x10, SeekOrigin.Current);
                    uint size   = index.ReadUInt32();
                    uint offset = index.ReadUInt32();
                    var name = index.ReadCString (name_length);
                    index_pos += 0x28 + name_length;

                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = offset;
                    entry.Size = size;
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
}
