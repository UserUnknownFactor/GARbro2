using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Carriere
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/CARRIERE"; } }
        public override string Description { get { return "Carriere resource archive"; } }
        public override uint     Signature { get { return 0x8F949B87; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadUInt32 (4) != 0x869E919A)
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 0xC;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x104);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset        = file.View.ReadUInt32 (index_offset+0x104);
                entry.UnpackedSize  = file.View.ReadUInt32 (index_offset+0x108);
                entry.Size          = file.View.ReadUInt32 (index_offset+0x10C);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = entry.UnpackedSize != entry.Size;
                dir.Add (entry);
                index_offset += 0x110;
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

    [Export(typeof(ArchiveFormat))]
    public class ScenarioArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/~ARCHIVE"; } }
        public override string Description { get { return "Carriere scripts archive"; } }
        public override uint     Signature { get { return 0xB7BCADBE; } } // ~'ARCHIVE'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadUInt32 (4) != 0xFFBAA9B6)
                return null;
            uint index_length = file.View.ReadUInt32 (8);
            using (var index_s = OpenDataStream (file, 8))
            using (var index = BinaryStream.FromStream (index_s, file.Name))
            {
                int count = index.ReadInt32();
                if (!IsSaneCount (count))
                    return null;
                uint data_offset = 8 + index_length;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadCString (0x104);
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.Offset = index.ReadUInt32() + data_offset;
                    entry.Size   = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = OpenDataStream (arc.File, entry.Offset);
            var pent = entry as PackedEntry;
            if (pent != null && !pent.IsPacked)
            {
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+8);
            }
            return input;
        }

        internal Stream OpenDataStream (ArcView file, long offset)
        {
            uint packed_size = file.View.ReadUInt32 (offset);
            int flags        = file.View.ReadInt32 (offset+4);
            int unpacked_size = file.View.ReadInt32 (offset+8);
            Stream input = file.CreateStream (offset+12, packed_size-12);
            if ((flags & 1) != 0)
            {
                input = new XoredStream (input, 0xFF);
            }
            if ((flags & 2) != 0)
            {
                input = new LzssStream (input);
            }
            return input;
        }
    }
}
