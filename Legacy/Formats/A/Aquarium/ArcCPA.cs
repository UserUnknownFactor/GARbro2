using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [000324][Fuuro] Over the Rainbow

namespace GameRes.Formats.Aquarium
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CPA"; } }
        public override string Description { get { return "Aquarium resource archive"; } }
        public override uint     Signature { get { return 0x00415043; } } // 'CPA'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (12);
            if (!IsSaneCount (count))
                return null;
            uint data_offset = file.View.ReadUInt32 (8);
            if (data_offset >= file.MaxOffset)
                return null;
            uint index_offset = 0x20;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (file.View.ReadByte (index_offset) != 0)
                {
                    var name = file.View.ReadString (index_offset, 0x10);
                    var entry = Create<Entry> (name);
                    entry.Offset = file.View.ReadUInt32 (index_offset+0x10) + data_offset;
                    entry.Size   = file.View.ReadUInt32 (index_offset+0x14);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
