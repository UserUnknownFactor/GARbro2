using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Mmfass
{
    [Export(typeof(ArchiveFormat))]
    public class SdaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SDA/MMFASS"; } }
        public override string Description { get { return "MMFass engine resource archive"; } }
        public override uint     Signature { get { return 0x4153; } } // 'SA'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public SdaOpener ()
        {
            Signatures = new uint[] { 0x30004153, 0x4153, 0 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "SA"))
                return null;
            uint data_offset = file.View.ReadUInt32 (4);
            if (data_offset <= 8 || data_offset >= file.MaxOffset)
                return null;
            int count = (int)(data_offset - 8) / 0x1C;
            if (!IsSaneCount (count))
                return null;
            bool is_graphic = Path.GetFileNameWithoutExtension (file.Name).Equals ("g", StringComparison.OrdinalIgnoreCase);
            uint index_offset = 8;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x14).Trim();
                if (string.IsNullOrEmpty (name))
                    return null;
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x14) + data_offset;
                entry.Size   = file.View.ReadUInt32 (index_offset+0x18);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (is_graphic)
                    entry.Type = "image";
                dir.Add (entry);
                index_offset += 0x1C;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
