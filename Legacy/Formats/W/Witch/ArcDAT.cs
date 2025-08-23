using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Witch
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ImageDataOpener
    {
        public override string         Tag { get { return "DAT/MK"; } }
        public override string Description { get { return "Witch resource archive"; } }
        public override uint     Signature { get { return 0x144B4D; } } // 'MK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 8;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint name_length = file.View.ReadUInt32 (index_offset);
                if (0 == name_length)
                    return null;
                var name = file.View.ReadString (index_offset+4, name_length);
                index_offset += 4 + name_length;
                var entry = new PcdEntry {
                    Name = name,
                    Type = "image",
                    Size   = file.View.ReadUInt32 (index_offset+8),
                    Offset = file.View.ReadUInt32 (index_offset+12),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Info = new ImageMetaData {
                    Width  = file.View.ReadUInt32 (index_offset),
                    Height = file.View.ReadUInt32 (index_offset+4),
                    BPP    = 8,
                };
                dir.Add (entry);
                index_offset += 16;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
