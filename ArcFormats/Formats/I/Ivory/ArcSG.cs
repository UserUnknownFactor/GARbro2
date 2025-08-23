using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Ivory
{
    [Export(typeof(ArchiveFormat))]
    public class SgOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SG/cOBJ"; } }
        public override string Description { get { return "Ivory multi-frame image"; } }
        public override uint     Signature { get { return 0x58475366; } } // 'fSGX'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            long offset = 8;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry>();
            while (offset < file.MaxOffset && file.View.AsciiEqual (offset, "cOBJ"))
            {
                uint obj_size = file.View.ReadUInt32 (offset+4);
                if (0 == obj_size)
                    break;
                if (file.View.AsciiEqual (offset+0x10, "fSG "))
                {
                    var entry = new Entry {
                        Name = string.Format ("{0}#{1}", base_name, dir.Count),
                        Type = "image",
                        Offset = offset+0x10,
                        Size = file.View.ReadUInt32 (offset+0x14),
                    };
                    dir.Add (entry);
                }
                offset += obj_size;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
