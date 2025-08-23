using GameRes.Compression;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [070323][schoolzone] Cosplay! Kyonyuu Mahjong

namespace GameRes.Formats.Mugi
{
    [Export(typeof(ArchiveFormat))]
    public class BinOpener : ArchiveFormat
    {
        public override string         Tag { get => "BIN/MUGI"; }
        public override string Description { get => "Mugi's resource archive"; }
        public override uint     Signature { get => 0; }
        public override bool  IsHierarchic { get => false; }
        public override bool      CanWrite { get => false; }

        public override ArcFile TryOpen (ArcView file)
        {
            const uint index_size = 0x8000 + 0x4000;
            if (file.MaxOffset <= index_size)
                return null;
            file.View.Reserve (0, index_size);
            uint index_pos = 0x8000;
            uint offset = file.View.ReadUInt32 (index_pos);
            if (offset != index_size)
                return null;
            uint[] offsets = new uint[0x800];
            int count = 0;
            while (offset != file.MaxOffset)
            {
                if (count == offsets.Length)
                    return null;
                offsets[count++] = offset;
                index_pos += 4;
                offset = file.View.ReadUInt32 (index_pos);
                if (offset < offsets[count-1] || offset > file.MaxOffset)
                    return null;
            }
            offsets[count--] = offset;
            uint name_pos = 0;
            uint size_pos = 0xA000;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (name_pos, 0x10);
                var entry = Create<PackedEntry> (name);
                entry.Offset = offsets[i];
                entry.Size = (uint)(offsets[i+1] - offsets[i]);
                entry.UnpackedSize = file.View.ReadUInt32 (size_pos);
                entry.IsPacked = entry.Size != entry.UnpackedSize;
                dir.Add (entry);
                name_pos += 0x10;
                size_pos += 4;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (pent.IsPacked)
                input = new LzssStream (input);
            return input;
        }
    }
}
