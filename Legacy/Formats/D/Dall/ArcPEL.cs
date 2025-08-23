using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Dall
{
    [Export(typeof(ArchiveFormat))]
    public class PelOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PEL"; } }
        public override string Description { get { return "Dall resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension ("PEL"))
                return null;
            int count = file.View.ReadInt16 (0);
            if (!IsSaneCount (count))
                return null;
            using (var input = file.CreateStream())
            {
                input.Position = 2;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = input.ReadCString (0x14);
                    uint size = input.ReadUInt32();
                    var entry = Create<Entry> (name);
                    entry.Offset = input.Position;
                    entry.Size = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    input.Seek (size, SeekOrigin.Current);
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
