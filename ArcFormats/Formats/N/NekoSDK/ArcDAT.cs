using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.NekoSDK
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/NekoSDK"; } }
        public override string Description { get { return "NekoSDK engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset < 0x8C)
                return null;
            uint first_offset = file.View.ReadUInt32 (0x88) ^ 0xCACACAu;
            if (first_offset <= 0 || first_offset >= file.MaxOffset)
                return null;
            int count = (int)first_offset / 0x8C;
            if (first_offset != count * 0x8C)
                return null;
            if (!IsSaneCount (--count))
                return null;

            uint index_offset = 0;
            var dir = new List<Entry> (count);
            var name_buffer = new byte[0x80];
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (index_offset, name_buffer, 0, 0x80);
                if (0 == name_buffer[0])
                    return null;
                var name = Binary.GetCString (name_buffer, 0, name_buffer.Length);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.UnpackedSize  = file.View.ReadUInt32 (index_offset+0x80) ^ 0xCACACAu;
                entry.Size          = file.View.ReadUInt32 (index_offset+0x84) ^ 0xCACACAu;
                entry.Offset        = file.View.ReadUInt32 (index_offset+0x88) ^ 0xCACACAu;
                if (entry.Offset < first_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = entry.UnpackedSize != 0;
                if (!entry.IsPacked)
                    entry.UnpackedSize = entry.Size;
                index_offset += 0x8C;
                dir.Add(entry);
            }
            if (0 == dir.Count)
                return null;
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
