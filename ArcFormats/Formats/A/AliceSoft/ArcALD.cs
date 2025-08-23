using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.AliceSoft
{
    [Export(typeof(ArchiveFormat))]
    public class AldOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ALD"; } }
        public override string Description { get { return "AliceSoft System engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            long index_offset = file.MaxOffset - 0x10;
            if (index_offset <= 0)
                return null;
            uint version = file.View.ReadUInt32 (index_offset);
            if (0x014C4E != version && 0x012020 != version
                || 0x10 != file.View.ReadUInt32 (index_offset+4))
                return null;
            int count = file.View.ReadUInt16 (index_offset+9);
            if (0 == count)
                return null;
            uint index_length = (file.View.ReadUInt32 (0) & 0xffffff) << 8;
            if (index_length > file.View.Reserve (0, index_length))
                return null;
            var dir = new List<Entry> (count);
            index_offset = 3;
            for (int i = 0; i < count; ++i)
            {
                uint offset = (file.View.ReadUInt32 (index_offset) & 0xffffff) << 8;
                if (0 == offset)
                    break;
                if (offset >= file.MaxOffset)
                    return null;
                dir.Add (new Entry { Offset = offset });
                index_offset += 3;
            }
            foreach (var entry in dir)
            {
                var offset = entry.Offset;
                uint header_size = file.View.ReadUInt32 (offset);
                if (header_size <= 0x10)
                    return null;
                entry.Size = file.View.ReadUInt32 (offset+4);
                entry.Name = file.View.ReadString (offset+0x10, header_size-0x10);
                entry.Offset = offset + header_size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
