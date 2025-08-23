using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [110527][Winters] Kiss x 700 Kiss Tantei

namespace GameRes.Formats.Winters
{
    [Export(typeof(ArchiveFormat))]
    public class CfpOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CFP/CAPYBARA"; } }
        public override string Description { get { return "Winters resource archive"; } }
        public override uint     Signature { get { return 0x59504143; } } // 'CAPYBARA DAT 002'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "CAPYBARA DAT 002"))
                return null;
            uint names_offset = file.View.ReadUInt32 (0x14);
            uint names_length = file.View.ReadUInt32 (0x18);
            uint index_offset = 0x20;
            var dir = new List<Entry>();
            using (var names = file.CreateStream (names_offset, names_length))
            using (var index = new StreamReader (names, Encodings.cp932))
            {
                string name;
                while (index_offset < names_offset && (name = index.ReadLine()) != null)
                {
                    if (name.Length > 0)
                    {
                        var entry = FormatCatalog.Instance.Create<Entry> (name);
                        entry.Offset = file.View.ReadUInt32 (index_offset);
                        entry.Size   = file.View.ReadUInt32 (index_offset+4);
                        if (!entry.CheckPlacement (file.MaxOffset))
                            return null;
                        dir.Add (entry);
                    }
                    index_offset += 0xC;
                }
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
