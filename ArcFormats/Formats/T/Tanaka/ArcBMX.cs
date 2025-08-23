using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Will
{
    [Export(typeof(ArchiveFormat))]
    public class BmxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BMX"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public BmxOpener ()
        {
            ContainedFormats = new[] { "BC" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint total_size = file.View.ReadUInt32 (0);
            if (total_size != file.MaxOffset)
                return null;
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            uint index_offset = 0x10;
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x1C);
                if (0 == name.Length)
                    break;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x1C);
                if (string.IsNullOrEmpty (entry.Type))
                    entry.Type = "image";
                dir.Add (entry);
                index_offset += 0x20;
            }
            if (0 == dir.Count)
                return null;
            long last_offset = file.MaxOffset;
            for (int i = dir.Count - 1; i >= 0; --i)
            {
                var entry = dir[i];
                entry.Size = (uint)(last_offset - entry.Offset);
                last_offset = entry.Offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
