using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [001124][emu] Luna Season ~150 Bun no 1 no Koibito~

namespace GameRes.Formats.Aos
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/PACK"; } }
        public override string Description { get { return "AOS engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat"))
                return null;
            var idx_name = VFS.ChangeFileName (file.Name, "index.idx");
            if (!VFS.FileExists (idx_name))
                return null;
            using (var index = VFS.OpenBinaryStream (idx_name))
            {
                var dir = new List<Entry>();
                while (index.PeekByte() != -1)
                {
                    var name = index.ReadCString (0x34);
                    if (string.IsNullOrEmpty (name))
                        break;
                    var entry = Create<Entry> (name);
                    entry.Offset = index.ReadUInt32();
                    entry.Size   = index.ReadUInt32();
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
