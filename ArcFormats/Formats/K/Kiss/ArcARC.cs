using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Kiss
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/KISS"; } }
        public override string Description { get { return "Kiss resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".arc"))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            using (var input = file.CreateStream())
            {
                long prev_offset = 4;
                input.Position = 4;
                for (int i = 0; i < count; ++i)
                {
                    var name = input.ReadCString();
                    if (string.IsNullOrWhiteSpace (name))
                        return null;
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = input.ReadInt64();
                    if (entry.Offset < prev_offset || entry.Offset > file.MaxOffset)
                        return null;
                    dir.Add (entry);
                    prev_offset = entry.Offset;
                }
                for (int i = 0; i < count; ++i)
                {
                    long next = i+1 < count ? dir[i+1].Offset : file.MaxOffset;
                    dir[i].Size = (uint)(next - dir[i].Offset);
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
