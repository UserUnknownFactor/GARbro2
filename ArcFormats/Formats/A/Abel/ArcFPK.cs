using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Abel
{
    [Export(typeof(ArchiveFormat))]
    public class FpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/FPK"; } }
        public override string Description { get { return "Abel resource archive"; } }
        public override uint     Signature { get { return 0x4B5046; } } // 'FPK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public FpkOpener ()
        {
            ContainedFormats = new[] { "CBF", "WAV", "DAT/GENERIC" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint names_size = file.View.ReadUInt32 (8);
            if (names_size >= file.MaxOffset)
                return null;
            uint index_offset = 12;
            using (var names = file.CreateStream (index_offset + count*8, names_size))
            {
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = names.ReadCString();
                    if (string.IsNullOrEmpty (name))
                        return null;
                    var entry = Create<Entry> (name);
                    entry.Offset = file.View.ReadUInt32 (index_offset);
                    entry.Size   = file.View.ReadUInt32 (index_offset+4);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_offset += 8;
                }
                return new ArcFile (file, this, dir);
            }
        }
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "ALP")]
    [ExportMetadata("Target", "DAT/GENERIC")]
    public class AlpFormat : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "JCP")]
    [ExportMetadata("Target", "WAV")]
    public class JcpFormat : ResourceAlias { }
}
