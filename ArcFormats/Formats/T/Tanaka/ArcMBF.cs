using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Will
{
    [Export(typeof(ArchiveFormat))]
    public class MbfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MBF"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine image archive"; } }
        public override uint     Signature { get { return 0x3046424D; } } // 'MBF0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public MbfOpener ()
        {
            Signatures = new uint[] { 0x3046424D, 0x3146424D };
            ContainedFormats = new[] { "BC" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            long data_offset = file.View.ReadUInt32 (8);
            uint index_offset = 0x20;
            if (0 != (file.View.ReadByte (0xC) & 1) && count > 1)
            {
                index_offset += file.View.ReadUInt16 (index_offset);
                --count;
            }
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint name_length = file.View.ReadUInt16 (index_offset);
                if (name_length < 3)
                    return null;
                var name = file.View.ReadString (index_offset+2, name_length-2);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                dir.Add (entry);
                index_offset += name_length;
            }
            foreach (var entry in dir)
            {
                if (file.View.AsciiEqual (data_offset, "BC"))
                {
                    entry.Size = file.View.ReadUInt32 (data_offset+2);
                    entry.Type = "image";
                }
                else if (file.View.AsciiEqual (data_offset, "$SEQ"))
                    entry.Size = file.View.ReadUInt32 (data_offset+4);
                else
                    return null;
                entry.Offset = data_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                data_offset += entry.Size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
