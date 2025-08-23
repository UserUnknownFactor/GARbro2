using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

// [991203][Libido] Ren'Ai Kumikyoku
// [030228][Libido] Libido 7 DVD

namespace GameRes.Formats.Libido
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/LIBIDO"; } }
        public override string Description { get { return "Libido resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;

            const int name_buf_size = 0x14;
            var name_buf = new byte[name_buf_size];
            var is_name_encrypted = new Lazy<bool> (() => -1 != Array.IndexOf<byte> (name_buf, 0xFF, 0, name_buf_size));
            uint index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (index_offset, name_buf, 0, name_buf_size);
                int j;
                if (is_name_encrypted.Value)
                {
                    for (j = 0; j < name_buf_size; ++j)
                    {
                        name_buf[j] ^= 0xFF;
                        if (0 == name_buf[j])
                            break;
                    }
                }
                else
                    j = Array.IndexOf<byte> (name_buf, 0, 0, name_buf_size);
                if (j <= 0 || -1 != Array.IndexOf<byte> (name_buf, 0xFF, 0, j))
                    return null;
                var name = Encodings.cp932.GetString (name_buf, 0, j);
                var entry = Create<PackedEntry> (name);
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+0x14);
                entry.Size         = file.View.ReadUInt32 (index_offset+0x18);
                entry.Offset       = file.View.ReadUInt32 (index_offset+0x1C);
                if (entry.Offset <= index_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = entry.Size != entry.UnpackedSize;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (null == pent || !pent.IsPacked)
                return input;
            return new LzssStream (input);
        }
    }
}
