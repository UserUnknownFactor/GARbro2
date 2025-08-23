using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.BlueGale
{
    [Export(typeof(ArchiveFormat))]
    public class SnnOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SNN"; } }
        public override string Description { get { return "BlueGale resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".snn"))
                return null;
            var inx_name = Path.ChangeExtension (file.Name, "Inx");
            if (!VFS.FileExists (inx_name))
                return null;
            using (var inx = VFS.OpenView (inx_name))
            {
                int count = inx.View.ReadInt32 (0);
                if (!IsSaneCount (count))
                    return null;

                int inx_offset = 4;
                if (inx_offset + count * 0x48 > inx.MaxOffset)
                    return null;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = inx.View.ReadString (inx_offset, 0x40);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = inx.View.ReadUInt32 (inx_offset+0x40);
                    entry.Size   = inx.View.ReadUInt32 (inx_offset+0x44);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    inx_offset += 0x48;
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
