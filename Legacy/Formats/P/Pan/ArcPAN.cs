using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

// [001006][Orange Mina] Judge August

namespace GameRes.Formats.Pan
{
    [Export(typeof(ArchiveFormat))]
    public class PanOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAN"; } }
        public override string Description { get { return "Pan engine resource archive"; } }
        public override uint     Signature { get { return 0x206E6150; } } // 'Pan ver 1.00'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "ver 1.00"))
                return null;
            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;
            uint index_size = (uint)count * 0x2C;
            using (var index = file.CreateStream (file.MaxOffset - index_size, index_size))
            {
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadCString (0x20);
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.UnpackedSize = index.ReadUInt32();
                    entry.Offset       = index.ReadUInt32();
                    entry.Size         = index.ReadUInt32();
                    entry.IsPacked     = true;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (null == pent || !pent.IsPacked)
                return input;
            return new LzssStream (input);
        }
    }
}
