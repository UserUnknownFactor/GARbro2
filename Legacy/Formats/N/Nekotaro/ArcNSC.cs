using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [991231][Jam] Kakuyuugou Shoujo Ripple-chan
// [000331][Jam] Zetsumetsu King
// [000630][STONE HEADS] Sei Cosplay Gakuen ~Game Bunkou~

namespace GameRes.Formats.Nekotaro
{
    [Export(typeof(ArchiveFormat))]
    public class NscOpener : ArchiveFormat
    {
        public override string         Tag { get { return "NSC"; } }
        public override string Description { get { return "Nekotaro Game System resource archive"; } }
        public override uint     Signature { get { return 0x4643534E; } } // 'NSCF'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry>();
            uint prev_offset = file.View.ReadUInt32 (4);
            for (uint index_offset = 8; index_offset < file.MaxOffset; index_offset += 4)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                if (offset <= prev_offset || offset > file.MaxOffset)
                    return null;
                uint size = offset - prev_offset;
                var name = string.Format ("{0}#{1:D4}", base_name, dir.Count);
                Entry entry;
                if (size > 4)
                    entry = AutoEntry.Create (file, prev_offset, name);
                else
                    entry = new Entry { Name = name, Offset = prev_offset };
                entry.Size = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                if (file.MaxOffset == offset)
                    break;
                prev_offset = offset;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
