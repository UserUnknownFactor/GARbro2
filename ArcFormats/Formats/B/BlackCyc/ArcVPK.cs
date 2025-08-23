using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.BlackCyc
{
    [Export(typeof(ArchiveFormat))]
    public class VpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "VPK"; } }
        public override string Description { get { return "Black Cyc engine audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".vpk"))
                return null;
            var vtb_name = Path.ChangeExtension (file.Name, "vtb");
            if (!VFS.FileExists (vtb_name))
                return null;
            var vtb_entry = VFS.FindFile (vtb_name);
            int count = (int)(vtb_entry.Size / 0x0C) - 1;
            if (!IsSaneCount (count))
                return null;

            using (var vtb = VFS.OpenView (vtb_entry))
            {
                vtb.View.Reserve (0, (uint)vtb.MaxOffset);
                uint index_offset = 0;
                uint next_offset = vtb.View.ReadUInt32 (8);
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    string name = vtb.View.ReadString (index_offset, 8) + ".vaw";
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = next_offset;
                    index_offset += 0xC;
                    next_offset = vtb.View.ReadUInt32 (index_offset+8);
                    entry.Size = next_offset - (uint)entry.Offset;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
