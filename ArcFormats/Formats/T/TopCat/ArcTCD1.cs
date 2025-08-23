using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.TopCat
{
    [Export(typeof(ArchiveFormat))]
    public class Tcd1Opener : TcdOpener
    {
        public override string         Tag { get { return "TCD1"; } }
        public override string Description { get { return "TopCat data archive"; } }
        public override uint     Signature { get { return 0x31444354; } } // 'TCD1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Tcd1Opener ()
        {
            Extensions = new string[] { "tcd" };
            Signatures = new uint[] { 0x31444354 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = file.View.ReadUInt32 (8);
            uint names_offset = file.View.ReadUInt32 (12);

            uint pos = index_offset;
            var offsets = new uint[count+1];
            for (int i = 0; i < count; ++i)
            {
                offsets[i] = file.View.ReadUInt32 (pos) - (index_offset << ((i & 7) + 8));
                pos += 4;
            }
            offsets[count] = file.View.ReadUInt32 (pos);
            var names = file.View.ReadBytes (names_offset, (uint)(file.MaxOffset - names_offset));
            var dir = new List<Entry> (count);
            int name_start = 0;
            int entry_num = 0;
            for (int i = 0; i < names.Length; ++i)
            {
                if (names[i] != 0)
                {
                    names[i] -= 0x57;
                }
                else
                {
                    var name = Encodings.cp932.GetString (names, name_start, i - name_start);
                    name_start = i+1;
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = offsets[entry_num];
                    entry.Size = offsets[entry_num+1] - offsets[entry_num];
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    ++entry_num;
                }
            }
            return new ArcFile (file, this, dir);
        }
    }
}
