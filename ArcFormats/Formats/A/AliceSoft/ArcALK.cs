using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.AliceSoft
{
    [Export(typeof(ArchiveFormat))]
    public class AlkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ALK"; } }
        public override string Description { get { return "AliceSoft System 4 resource archive"; } }
        public override uint     Signature { get { return 0x304B4C41; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 8;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var offset = file.View.ReadUInt32 (index_offset);
                var size   = file.View.ReadUInt32 (index_offset+4);
                if (0 != size)
                {
                    var name = string.Format ("{0}#{1:D4}", base_name, i);
                    var entry = AutoEntry.Create (file, offset, name);
                    entry.Size = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
