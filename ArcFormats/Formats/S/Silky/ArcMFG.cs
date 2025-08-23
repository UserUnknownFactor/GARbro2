using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Silky
{
    [Export(typeof(ArchiveFormat))]
    public class MfgOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MFG"; } }
        public override string Description { get { return "Silky's engine resource archive"; } }
        public override uint     Signature { get { return 0x46504c41; } } // 'ALPF'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public MfgOpener ()
        {
            Extensions = new string[] { "mfg", "mfm", "mfs" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (count <= 0 || count > 0xfffff)
                return null;
            var dir = new List<Entry> (count);
            long index_offset = 8;
            uint next_offset = file.View.ReadUInt32 (index_offset+0x10);
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0x10);
                if (0 == name.Length)
                    return null;
                uint offset = next_offset;
                index_offset += 0x14;
                if (i+1 != count)
                    next_offset = file.View.ReadUInt32 (index_offset+0x10);
                else
                    next_offset = (uint)file.MaxOffset;
                var entry = AutoEntry.Create (file, offset, name);
                entry.Size = next_offset - offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
