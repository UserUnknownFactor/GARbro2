using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.BlackRainbow
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/BR"; } }
        public override string Description { get { return "BlackRainbow resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat", "pak" };
            Signatures = new uint[] { 2u, 4u, 5u, 6u };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint base_offset = file.View.ReadUInt32 (0x0c);
            uint index_offset = 0x10;
            uint index_size = 4u * (uint)count;
            if (base_offset >= file.MaxOffset || base_offset < (index_offset+index_size))
                return null;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            var index = new List<uint> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                if (offset != 0xffffffff)
                    index.Add (base_offset + offset);
                index_offset += 4;
            }
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            index.Sort();
            var dir = new List<Entry> (index.Count);
            for (int i = 0; i < index.Count; ++i)
            {
                long offset = index[i];
                string name = file.View.ReadString (offset, 0x24);
                if (0 == name.Length)
                {
                    name = string.Format ("{0:D2}_{1}#{0:D2}", i, base_name);
                    if (file.View.AsciiEqual (offset + 0x24, "_BMD"))
                        name += ".bmd";
                }
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset + 0x24;
                entry.Size   = (uint)((i + 1 < index.Count ? index[i+1]  : file.MaxOffset) - entry.Offset);
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
