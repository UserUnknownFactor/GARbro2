using System.ComponentModel.Composition;

namespace GameRes.Formats.Rpm
{
    [Export(typeof(ArchiveFormat))]
    public class ZenosOpener : ArcOpener
    {
        public override string         Tag { get { return "ARC/ZENOS"; } }
        public override string Description { get { return "Zenos resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count) || 4 + count * 0x1C >= file.MaxOffset)
                return null;

            var index_reader = new ArcIndexReader (file, count, true);
//            var scheme = new EncryptionScheme ("haku", 0x10);
            var scheme = index_reader.GuessScheme (4, new int[] { 0x10 });
            if (null == scheme)
                return null;
            var dir = index_reader.ReadIndex (4, scheme);
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
