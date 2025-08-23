using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Will
{
    [Export(typeof(ArchiveFormat))]
    public class WrcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "WVX"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine audio archive"; } }
        public override uint     Signature { get { return 0x30585657; } } // 'WVX0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public WrcOpener ()
        {
            Extensions = new string[] { "wvx", "wrc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint total_size = file.View.ReadUInt32 (4);
            if (total_size != file.MaxOffset)
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            uint index_offset = 0x10;
            uint next_offset = file.View.ReadUInt32 (index_offset+0x1C);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x1C);
                if (0 == name.Length)
                    return null;
                index_offset += 0x20;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = next_offset;
                next_offset = i+1 < count ? file.View.ReadUInt32 (index_offset+0x1C) : (uint)file.MaxOffset;
                entry.Size = (uint)(next_offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (string.IsNullOrEmpty (entry.Type))
                    entry.Type = "audio";
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
