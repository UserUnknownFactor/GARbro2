using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [010309][Tetratech] Kyouiku Jisshuu 2 ~Joshikousei Maniacs~
// [031128][Kogado Studio] 指極星

namespace GameRes.Formats.Tetratech
{
    [Export(typeof(ArchiveFormat))]
    public class BndOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BND/IDX"; } }
        public override string Description { get { return "Tetratech resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public BndOpener ()
        {
            ContainedFormats = new[] { "BMP", "WAV", "SCR" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".BND"))
                return null;
            var idx_name = Path.ChangeExtension (file.Name, "idx");
            if (!VFS.FileExists (idx_name))
                return null;
            using (var idx = VFS.OpenBinaryStream (idx_name))
            {
                int count = (int)idx.Length / 0x18;
                if (!IsSaneCount (count) || count * 0x18 != idx.Length)
                    return null;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = idx.ReadCString (0x10);
                    var entry = Create<Entry> (name);
                    entry.Size   = idx.ReadUInt32();
                    entry.Offset = idx.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "SCB")]
    [ExportMetadata("Target", "SCR")]
    public class ScbFormat : ResourceAlias { }
}
