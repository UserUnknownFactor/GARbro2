using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

// [000407][Shape Shifter] Tsubasa no Hatameki
// [040326][Glastonbury] Assassin Lesson

namespace GameRes.Formats.ShapeShifter
{
#if DEBUG
    [Export(typeof(ArchiveFormat))]
#endif
    public class BndOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BND"; } }
        public override string Description { get { return "Shape Shifter resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;

            uint first_offset = file.View.ReadUInt32 (4);
            if (first_offset != 4 +(uint)count * 12)
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name).ToUpperInvariant();
            string default_type = base_name == "SCR" ? "script" :
                                  base_name == "VOICE" || base_name == "SE" ? "audio" :
                                  base_name == "PICT" ? "image" : "";
            uint index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                var entry = new PackedEntry {
                    Name = string.Format ("{0}#{1:D4}", base_name, i),
                    Type = default_type,
                    Offset = file.View.ReadUInt32 (index_offset),
                    UnpackedSize = file.View.ReadUInt32 (index_offset+4),
                    Size = file.View.ReadUInt32 (index_offset+8),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = entry.UnpackedSize != entry.Size;
                dir.Add (entry);
                index_offset += 12;
            }
            if (string.IsNullOrEmpty (default_type))
                DetectFileTypes (file, dir);
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size, entry.Name);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            return new LzssStream (input);
        }

        void DetectFileTypes (ArcView file, List<Entry> dir)
        {
            foreach (PackedEntry entry in dir)
            {
                var offset = entry.Offset;
                var signature = file.View.ReadUInt32 (offset);
                if (entry.IsPacked  && (0x4D42   == (signature & 0xFFFF)) || // 'BM'
                    !entry.IsPacked && (0x4D4207 == (signature & 0xFFFF07)))
                {
                    entry.Type = "image";
                    entry.Name = Path.ChangeExtension (entry.Name, "bmp");
                }
            }
        }
    }
}
