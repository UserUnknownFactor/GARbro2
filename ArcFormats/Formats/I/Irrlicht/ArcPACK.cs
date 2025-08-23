using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Irrlicht
{
    [Export(typeof(ArchiveFormat))]
    public class PackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PACK/Irrlicht"; } }
        public override string Description { get { return "Irrlicht engine audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PackOpener ()
        {
            Extensions = new string[] { "pack" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".pack"))
                return null;
            long offset = 0;
            var dir = new List<Entry>();
            while (offset < file.MaxOffset)
            {
                if (offset + 0x10A >= file.MaxOffset)
                    return null;
                var name = file.View.ReadString (offset, 0x104);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                offset += 0x105;
                uint size = file.View.ReadUInt32 (offset);
                offset += 5;
                if (offset + size > file.MaxOffset)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                entry.Size = size;
                dir.Add (entry);
                offset += size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
