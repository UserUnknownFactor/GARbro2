using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Security.Cryptography;

namespace GameRes.Formats.Slg
{
    [Export(typeof(ArchiveFormat))]
    public class SzsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SZS"; } }
        public override string Description { get { return "SLG system resource archive"; } }
        public override uint     Signature { get { return 0x31535A53; } } // 'SZS1'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadByte (4) - '0';
            if (version < 0 || !file.View.AsciiEqual (5, "0__"))
                return null;
            int count = file.View.ReadInt32 (0xC);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x100);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                index_offset += 0x100;
                name = name.Replace (';', '/');
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadInt64 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new XoredStream (input, 0x90);
        }
    }
}
