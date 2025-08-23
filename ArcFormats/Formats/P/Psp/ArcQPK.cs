using GameRes.Compression;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Psp
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag => "QPK";
        public override string Description => "PSP resource archive";
        public override uint     Signature => 0x4B5051; // 'QPK'
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            var index_name = Path.ChangeExtension (file.Name, "QPI");
            List<Entry> dir;
            using (var index = VFS.OpenView (index_name))
            {
                if (!index.View.AsciiEqual (0, "QPI\0"))
                    return null;
                int count = index.View.ReadInt32 (4);
                if (!IsSaneCount (count))
                    return null;

                var base_name = Path.GetFileNameWithoutExtension (file.Name);
                string ext = "";
                string type = "";
                if ("TGA" == base_name)
                {
                    ext = ".tga";
                    type = "image";
                }
                dir = new List<Entry> (count);
                uint index_pos = 0x1C;
                for (int i = 0; i < count; ++i)
                {
                    uint offset = index.View.ReadUInt32 (index_pos);
                    uint size   = index.View.ReadUInt32 (index_pos+4);
                    if (offset > file.MaxOffset)
                        return null;
                    index_pos += 8;
                    if ((size & 0x80000000) != 0 || size == 0)
                        continue;
                    var entry = new PackedEntry {
                        Name = string.Format ("{0}#{1:D5}{2}", base_name, i, ext),
                        Type = type,
                        Offset = offset,
                        UnpackedSize = size & 0x3FFFFFFF,
                        IsPacked = (size & 0x40000000) != 0,
                    };
                    dir.Add (entry);
                }
                long last_offset = file.MaxOffset;
                for (int i = dir.Count - 1; i >= 0; --i)
                {
                    dir[i].Size = (uint)(last_offset - dir[i].Offset);
                    last_offset = dir[i].Offset;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            if (!pent.IsPacked || !arc.File.View.AsciiEqual (pent.Offset, "CZL\0"))
                return base.OpenEntry (arc, pent);
            uint size = arc.File.View.ReadUInt32 (pent.Offset+4);
            var input = arc.File.CreateStream (pent.Offset+12, size);
            return new ZLibStream (input, CompressionMode.Decompress);
        }
    }
}
