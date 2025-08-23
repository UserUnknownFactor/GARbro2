using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Ffa
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "FFA/ARC"; } }
        public override string Description { get { return "FFA System resource archive"; } }
        public override uint     Signature { get { return 0x5954324d; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
            Signatures = new uint[] { 0x5954324d, 0x5f54324d };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            #pragma warning disable 219
            string type;
            if (file.View.AsciiEqual (0, "M2TYPE_WAV"))
                type = "wave";
            else if (file.View.AsciiEqual (0, "M2T_BMP"))
                type = "bmp_";
            else if (file.View.AsciiEqual (0, "M2T_WORD"))
                type = "word";
            else
                return null;
            #pragma warning restore 219

            uint index_size = file.View.ReadUInt32 (file.MaxOffset-12);
            long index_offset = file.MaxOffset-0x14-index_size;
            int count = file.View.ReadInt32 (file.MaxOffset-8);
            if (index_offset <= 0 || count <= 0 || count > 0xfffff)
                return null;
            file.View.Reserve (index_offset, index_size);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x10);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x14);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
