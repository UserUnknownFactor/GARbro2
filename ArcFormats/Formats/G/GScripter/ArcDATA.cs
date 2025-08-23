using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.GScripter
{
    [Export(typeof(ArchiveFormat))]
    public class DataOpener : ArchiveFormat
    {
        public override string         Tag { get => "DAT/GScripter"; }
        public override string Description { get => "GScripter engine resource archive"; }
        public override uint     Signature { get => 0; }
        public override bool  IsHierarchic { get => false; }
        public override bool      CanWrite { get => false; }

        public DataOpener ()
        {
            Extensions = new[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var info_name = file.Name + ".info";
            if (!VFS.FileExists (info_name))
                return null;
            using (var index = VFS.OpenView (info_name))
            {
                int count = (int)index.MaxOffset / 0x28;
                if (!IsSaneCount (count) || count * 0x28 != index.MaxOffset)
                    return null;
                var arc_name = Path.GetFileName (file.Name);
                bool is_cg    = arc_name.StartsWith ("CG");
                bool is_sound = !is_cg && arc_name.StartsWith ("SOUND");
                uint index_pos = 0;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.View.ReadString (index_pos, 0x20);
                    var entry = new Entry {
                        Name = name,
                        Type = is_cg ? "image" : is_sound ? "audio" : "",
                        Offset = index.View.ReadUInt32 (index_pos+0x20),
                        Size   = index.View.ReadUInt32 (index_pos+0x24),
                    };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_pos += 0x28;
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
