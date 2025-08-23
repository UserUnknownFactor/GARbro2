using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Pochette
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/IDX"; } }
        public override string Description { get { return "Pochette resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PacOpener ()
        {
            ContainedFormats = new[] { "GDT", "WAV" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".pac"))
                return null;
            var idx_name = Path.ChangeExtension (file.Name, ".idx");
            if (!VFS.FileExists (idx_name))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            string type = "";
            if (base_name.StartsWith ("GDT", StringComparison.OrdinalIgnoreCase))
                type = "image";
            else if (base_name.StartsWith ("WAV", StringComparison.OrdinalIgnoreCase))
                type = "audio";
            List<Entry> dir = null;
            using (var idx = VFS.OpenView (idx_name))
            {
                if ((idx.MaxOffset & 0xF) != 0)
                    return null;
                int count = (int)idx.MaxOffset / 0x10;
                if (!IsSaneCount (count))
                    return null;
                uint idx_pos = 0;
                dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    uint name_length = idx.View.ReadByte (idx_pos);
                    var name = idx.View.ReadString (idx_pos+1, name_length);
                    if (string.IsNullOrWhiteSpace (name))
                        return null;
                    var entry = new Entry { Name = name, Type = type };
                    entry.Offset = idx.View.ReadUInt32 (idx_pos+8);
                    entry.Size   = idx.View.ReadUInt32 (idx_pos+12);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    idx_pos += 0x10;
                }
            }
            return new ArcFile (file, this, dir);
        }
    }
}
