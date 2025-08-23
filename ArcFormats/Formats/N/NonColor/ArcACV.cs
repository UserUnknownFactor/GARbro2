using System.ComponentModel.Composition;

namespace GameRes.Formats.NonColor
{
    [Export(typeof(ArchiveFormat))]
    public class AcvOpener : DatOpener
    {
        public override string         Tag { get { return "ACV"; } }
        public override string Description { get { return "Mirai resource archive"; } }
        public override uint     Signature { get { return 0x31564341; } } // 'ACV1'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public AcvOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint key = 0x8B6A4E5F;
            int count = file.View.ReadInt32 (4) ^ (int)key;
            if (!IsSaneCount (count))
                return null;

            var scheme = QueryScheme (file.Name);
            if (null == scheme)
                return null;

            using (var index = new NcIndexReader (file, count, key) { IndexPosition = 8 })
                return index.Read (this, scheme);
        }
    }
}
