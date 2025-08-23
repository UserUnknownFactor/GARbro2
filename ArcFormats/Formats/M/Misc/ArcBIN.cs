using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Misc
{
    [Export(typeof(ArchiveFormat))]
    public class BinOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/?"; } }
        public override string Description { get { return "Uncategorized resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint first_offset = file.View.ReadUInt32 (4);
            if (4 + count * 8 != first_offset)
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            bool is_msg = base_name == "msg";
            uint index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                uint size   = file.View.ReadUInt32 (index_offset+4);
                var entry = new PackedEntry {
                    Name = string.Format ("{0}#{1:D5}", base_name, i),
                    Offset = offset,
                    Size = size,
                };
                if (!is_msg && !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            foreach (PackedEntry entry in dir)
            {
                if (is_msg)
                {
                    uint size = file.View.ReadUInt32 (entry.Offset);
                    entry.Offset += 4;
                    entry.UnpackedSize = entry.Size;
                    entry.Size = size;
                    entry.IsPacked = true;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    continue;
                }
                uint pos = file.View.ReadUInt32 (entry.Offset+4);
                if (pos > entry.Size)
                    continue;
                uint unpacked_size = file.View.ReadUInt32 (entry.Offset+8);
                uint flags = unpacked_size >> 30;
                unpacked_size &= 0x3FFFFFFF;
                entry.IsPacked = 0 == flags;
                uint signature = 0;
                if (entry.IsPacked)
                {
                    entry.UnpackedSize = unpacked_size;
                    entry.Size = file.View.ReadUInt32 (entry.Offset+pos);
                    entry.Offset += pos + 4;
                    if ((file.View.ReadByte (entry.Offset) & 0xF) == 0xF)
                        signature = file.View.ReadUInt32 (entry.Offset+1);
                }
                else
                {
                    entry.Size = unpacked_size;
                    entry.Offset += pos;
                    signature = file.View.ReadUInt32 (entry.Offset);
                }
                var res = AutoEntry.DetectFileType (signature);
                if (res != null)
                    entry.ChangeType (res);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            return new LzssStream (input);
        }
    }
}
