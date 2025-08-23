using System.Collections.Generic;

namespace GameRes.Formats
{
    public class WrapSingleFileArchive : ArcFile
    {
        internal static readonly ArchiveFormat Format = new SingleFileArchiveFormat();

        public WrapSingleFileArchive (ArcView file, Entry entry)
            : base (file, Format, new List<Entry> { entry })
        {
        }

        public WrapSingleFileArchive (ArcView file, string entry_name)
            : base (file, Format, new List<Entry> { CreateEntry (file, entry_name) })
        {
        }

        private static Entry CreateEntry (ArcView file, string name)
        {
            var entry = FormatCatalog.Instance.Create<Entry> (name);
            entry.Offset = 0;
            entry.Size = (uint)file.MaxOffset;
            return entry;
        }

        /// this format is not registered in catalog and only accessible via WrapSingleFileArchive.Format singleton.
        private class SingleFileArchiveFormat : ArchiveFormat
        {
            public override string         Tag => "DAT/BOGUS";
            public override string Description => "Not an archive";
            public override uint     Signature => 0;
            public override bool  IsHierarchic => false;
            public override bool      CanWrite => false;

            public override ArcFile TryOpen (ArcView file)
            {
                return new WrapSingleFileArchive (file, System.IO.Path.GetFileName (file.Name));
            }
        }
    }
}
