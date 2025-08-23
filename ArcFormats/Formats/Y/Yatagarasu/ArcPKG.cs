using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Yatagarasu
{
    [Export(typeof(ArchiveFormat))]
    public class PkgOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PKG"; } }
        public override string Description { get { return "Yatagarasu resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".pkg"))
                return null;
            uint key = file.View.ReadUInt32 (0x84); // last bytes of the first entry name
            if (key != file.View.ReadUInt32 (0x10C)) // keys of the first two entries supposed to be the same
                return null;
            int count = (int)(file.View.ReadUInt32 (4) ^ key);
            if (!IsSaneCount (count))
                return null;
            var key_bytes = new byte[4];
            LittleEndian.Pack (key, key_bytes, 0);
            using (var input = file.CreateStream())
            using (var dec = new ByteStringEncryptedStream (input, key_bytes))
            using (var index = new BinaryStream (dec, file.Name))
            {
                index.Position = 8;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadCString (0x80);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Size = index.ReadUInt32();
                    entry.Offset = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new PkgArchive (file, this, dir, key_bytes);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pkg_arc = (PkgArchive)arc;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new ByteStringEncryptedStream (input, pkg_arc.Key);
        }
    }

    internal class PkgArchive : ArcFile
    {
        public readonly byte[] Key;

        public PkgArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }
}
