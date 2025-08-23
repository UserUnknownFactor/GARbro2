using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Ransel
{
    [Export(typeof(ArchiveFormat))]
    public class BcdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BCD"; } }
        public override string Description { get { return "ransel engine resource archive"; } }
        public override uint     Signature { get { return 0x616E6942; } } // 'BinaryCombineData'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "BinaryCombineData"))
                return null;
            var bcl_name = Path.ChangeExtension (file.Name, "bcl");
            using (var bcl = VFS.OpenStream (bcl_name))
            using (var index = new StreamReader (bcl, Encodings.cp932))
            {
                if (index.ReadLine() != "[BinaryCombineData]")
                    return null;
                var filename = index.ReadLine();
                if (!VFS.IsPathEqualsToFileName (file.Name, filename))
                    return null;
                index.ReadLine();
                var dir = new List<Entry>();
                while ((filename = index.ReadLine()) != null)
                {
                    if (!filename.StartsWith ("[") || !filename.EndsWith ("]"))
                        return null;
                    filename = filename.Substring (1, filename.Length-2);
                    var offset = index.ReadLine();
                    var size = index.ReadLine();
                    index.ReadLine();
                    var entry = Create<Entry> (filename);
                    entry.Offset = UInt32.Parse (offset);
                    entry.Size   = UInt32.Parse (size);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
