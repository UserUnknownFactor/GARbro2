using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.AliceSoft
{
    [Export(typeof(ArchiveFormat))]
    public class AarOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AAR"; } }
        public override string Description { get { return "AliceSoft System engine resource archive"; } }
        public override uint     Signature { get { return 0x00524141; } } // 'AAR'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public AarOpener ()
        {
            Extensions = new string[] { "red" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;

            using (var index = file.CreateStream())
            {
                index.Position = 0xC;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    uint offset = index.ReadUInt32();
                    uint size   = index.ReadUInt32();
                    bool is_packed = index.ReadInt32() != 1;
                    string name = index.ReadCString();
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.Offset = offset;
                    entry.Size = size;
                    entry.IsPacked = is_packed;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!arc.File.View.AsciiEqual (entry.Offset, "ZLB\0"))
                return base.OpenEntry (arc, entry);
            var pent = entry as PackedEntry;
            if (null != pent && 0 == pent.UnpackedSize)
            {
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (8);
            }
            uint packed_size = arc.File.View.ReadUInt32 (0xC);
            var input = arc.File.CreateStream (entry.Offset+0x10, packed_size);
            return new ZLibStream (input, CompressionMode.Decompress);
        }
    }
}
