using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;

namespace GameRes.Formats.Xuse
{
    [Export(typeof(ArchiveFormat))]
    public class BinOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/Xuse"; } }
        public override string Description { get { return "Xuse audio archive"; } }
        public override uint     Signature { get { return 1; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public BinOpener ()
        {
            Extensions = new string[] { "bin", "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var first_offset = file.View.ReadUInt32 (8);
            if (first_offset <= 0x14 || first_offset >= file.MaxOffset || first_offset > int.MaxValue)
                return null;
            int index_size = (int)(first_offset - 4);
            var count = index_size / 0x10;
            if (count * 0x10 != index_size)
                return null;

            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_offset = 4;
            uint last_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var offset = file.View.ReadUInt32 (index_offset+4);
                if (0 == offset)
                    break;
                if (offset <= last_offset)
                    return null;
                string name = string.Format ("{0}#{1:D4}", base_name, i);
                var entry = new AutoEntry (name, () => {
                    uint signature = file.View.ReadUInt32 (offset);
                    if (0 == signature) return null;
                    return FormatCatalog.Instance.LookupSignature (signature).FirstOrDefault();
                });
                entry.Offset = offset;
                entry.Size = file.View.ReadUInt32 (index_offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                last_offset = offset;
                index_offset += 0x10;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
