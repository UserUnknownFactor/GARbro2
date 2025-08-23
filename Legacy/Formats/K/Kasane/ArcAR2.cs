using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [030525][Virgin Cream] Yamai Tsuki ~Ryouki Kansen Virus~
// [030530][Mithra] Clione no Kimochi ~Oshiete, Sensei Osanazuma~

namespace GameRes.Formats.Kasane
{
    [Export(typeof(ArchiveFormat))]
    public class Ar2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "AR2/IDX"; } }
        public override string Description { get { return "Kasane script engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".ar2"))
                return null;
            var idx_name = Path.ChangeExtension (file.Name, ".idx");
            using (var idx_view = VFS.OpenView (idx_name))
            using (var idx = idx_view.CreateStream())
            {
                int count = idx.ReadInt32();
                if (!IsSaneCount (count))
                    return null;
                var name_buffer = new byte[0x100];
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    uint size       = idx.ReadUInt32();
                    uint unpacked_size = idx.ReadUInt32();
                    idx.ReadInt32();
                    int name_length = idx.ReadInt32();
                    uint offset     = idx.ReadUInt32();
                    if (name_length > name_buffer.Length)
                        return null;
                    idx.Read (name_buffer, 0, name_length);
                    for (int j = 0; j < name_length; ++j)
                        name_buffer[j] ^= 0x55;
                    var name = Encodings.cp932.GetString (name_buffer, 0, name_length);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = offset;
                    entry.Size   = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            uint name_length = arc.File.View.ReadUInt32 (entry.Offset+0xC) ^ 0x55555555u;
            var input = arc.File.CreateStream (entry.Offset + 0x10 + name_length, entry.Size);
            return new XoredStream (input, 0x55);
        }
    }
}
