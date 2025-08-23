using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Giga
{
    internal class TpfEntry : PackedEntry
    {
        public uint InterimSize;
        public byte Compression;
    }

    [Export(typeof(ArchiveFormat))]
    public class TpfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "TPF"; } }
        public override string Description { get { return "Giga resource archive"; } }
        public override uint     Signature { get { return 0x20465054; } } // 'TPF FILE'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "FILE"))
                return null;
            int count = file.View.ReadInt32 (0xC);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x20);
                byte compression = file.View.ReadByte (index_offset+0x23);
                var entry = FormatCatalog.Instance.Create<TpfEntry> (name);
                entry.Compression = compression;
                entry.IsPacked = compression != 0;
                entry.Offset = file.View.ReadUInt32 (index_offset+0x24);
                entry.InterimSize = file.View.ReadUInt32 (index_offset+0x28);
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+0x2C);
                index_offset += 0x30;
                if (entry.UnpackedSize != 0)
                {
                    entry.Size = (uint)(file.View.ReadUInt32 (index_offset+0x24) - entry.Offset);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as TpfEntry;
            if (null == pent || !pent.IsPacked || pent.Compression > 2)
                return input;
            if (2 == pent.Compression)
                input = new HuffmanStream (input);
            return new LzssStream (input);
        }
    }
}
