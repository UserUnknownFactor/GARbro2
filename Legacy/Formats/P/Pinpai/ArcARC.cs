using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

// [020726][Pinpai] Court no Naka no Tenshi-tachi 2 ~Softball Hen~

namespace GameRes.Formats.Pinpai
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/arcx"; } }
        public override string Description { get { return "Pinpai resource archive"; } }
        public override uint     Signature { get { return 0x78637261; } } // 'arcx'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            var arc_name = Path.GetFileNameWithoutExtension (file.Name).ToLowerInvariant();
            bool is_compressed = arc_name != "wav";
            uint index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Size   = file.View.ReadUInt32 (index_offset+0x10);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x14);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (name.HasExtension (".b"))
                    entry.Type = "image";
                entry.IsPacked = is_compressed;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            if (0 == pent.UnpackedSize)
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset);
            var input = arc.File.CreateStream (entry.Offset+4, entry.Size-4);
            return new LzssStream (input);
        }
    }
}
