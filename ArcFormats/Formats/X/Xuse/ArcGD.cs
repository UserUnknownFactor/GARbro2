using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Xuse
{
    [Export(typeof(ArchiveFormat))]
    public class GdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GD/Xuse"; } }
        public override string Description { get { return "Xuse resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public GdOpener ()
        {
            Extensions = new string[] { "gd" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            string index_name = Path.ChangeExtension (file.Name, ".dll");
            if (index_name == file.Name || !VFS.FileExists (index_name))
                return null;
            var index_entry = VFS.FindFile (index_name);
            if (index_entry.Size < 12)
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count) || (count & 0xFFFF) == 0x5A4D) // 'MZ'
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            using (var idx = VFS.OpenView (index_entry))
            {
                var dir = new List<Entry> (count);
                uint index_offset = 4;
                int i = 0;
                uint last_offset = 3;
                while (index_offset+8 <= idx.MaxOffset)
                {
                    uint offset = idx.View.ReadUInt32 (index_offset);
                    if (offset <= last_offset)
                        return null;
                    var name = string.Format ("{0}#{1:D5}", base_name, i++);
                    var entry = AutoEntry.Create (file, offset, name);
                    entry.Size = idx.View.ReadUInt32(index_offset+4);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    last_offset = offset;
                    index_offset += 8;
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
