using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Unknown
{
    [Export(typeof(ArchiveFormat))]
    public class PakFormat : ArchiveFormat
    {
        public override string         Tag { get { return "PAK"; } }
        public override string Description { get { return "Sample PAK Archive"; } }
        public override uint     Signature { get { return  0x4B4150; } } // "PAK"
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  false; } }

        public PakFormat()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "PAK"))
                return null;

            uint version = file.View.ReadUInt32 (3);
            if (version != 1)
                return null;

            uint count = file.View.ReadUInt32 (8);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 12;
            var dir = new List<Entry>((int)count);

            for (uint i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 256);
                var entry = new Entry
                {
                    Name = name,
                    Offset = file.View.ReadUInt32 (index_offset + 256),
                    Size = file.View.ReadUInt32 (index_offset + 260)
                };

                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;

                entry.Type = FormatCatalog.Instance.GetTypeFromName (name);
                dir.Add (entry);
                index_offset += 264;
            }

            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            // Check if entry is compressed
            var pent = entry as PackedEntry;
            if (pent != null && pent.IsPacked)
            {
                var input = arc.File.CreateStream (entry.Offset, entry.Size);
                return new ZLibStream (input, CompressionMode.Decompress);
            }

            return base.OpenEntry (arc, entry);
        }
    }
}
