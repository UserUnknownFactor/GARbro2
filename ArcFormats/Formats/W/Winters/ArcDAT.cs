using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [091127][Winters] Kiss x 600 Kanrinin-san no Ponytail

namespace GameRes.Formats.Winters
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/CAPYBARA"; } }
        public override string Description { get { return "Winters resource archive"; } }
        public override uint     Signature { get { return 0x59504143; } } // 'CAPYBARA DAT 001'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "CAPYBARA DAT 001"))
                return null;
            uint names_offset = file.View.ReadUInt32 (0x10);
            uint names_length = file.View.ReadUInt32 (0x14);
            uint index_offset = 0x18;
            var dir = new List<Entry>();
            using (var names = file.CreateStream (names_offset, names_length))
            using (var index = new StreamReader (names, Encodings.cp932))
            {
                string name;
                while (index_offset < names_offset && (name = index.ReadLine()) != null)
                {
                    if (":END" == name)
                        break;
                    if (name.Length > 0)
                    {
                        var entry = FormatCatalog.Instance.Create<Entry> (name);
                        entry.Offset = file.View.ReadUInt32 (index_offset);
                        entry.Size   = file.View.ReadUInt32 (index_offset+4);
                        if (!entry.CheckPlacement (file.MaxOffset))
                            return null;
                        dir.Add (entry);
                    }
                    index_offset += 8;
                }
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
