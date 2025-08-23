using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;

namespace GameRes.Formats.Gsx
{
    [Export(typeof(ArchiveFormat))]
    public class K5Opener : ArchiveFormat
    {
        public override string         Tag => "K5";
        public override string Description => "GSX engine resource archive";
        public override uint     Signature => 0x01354B; // 'K5'
        public override bool  IsHierarchic => true;
        public override bool      CanWrite => false;

        public K5Opener ()
        {
            ContainedFormats = new[] { "K4", "OGG" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (8);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var dir_name = file.View.ReadString (index_offset,      0x80, Encoding.Unicode);
                var name     = file.View.ReadString (index_offset+0x80, 0x40, Encoding.Unicode);
                name = Path.Combine (dir_name, name);
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0xC8);
                entry.Size   = file.View.ReadUInt32 (index_offset+0xCC);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x100;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
