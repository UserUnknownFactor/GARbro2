using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Triangle
{
    [Export(typeof(ArchiveFormat))]
    public class BmxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BMX/TRIANGLE"; } }
        public override string Description { get { return "Triangle resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public BmxOpener ()
        {
            Extensions = new string[] { "bmx", "wax", "fx", "gx" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_size = (uint)count * 4 + 8;
            if (index_size > file.View.Reserve (0, index_size))
                return null;
            uint index_offset = 4;
            uint offset = file.View.ReadUInt32 (index_offset);
            if (offset != index_size)
                return null;
            uint last_offset = file.View.ReadUInt32 (index_size - 4);
            if (last_offset != file.MaxOffset)
                return null;
            string default_type = "";
            if (file.Name.HasExtension ("fx"))
                default_type = "audio";
            else if (file.Name.HasExtension ("gx"))
                default_type = "image";
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 4;
                var entry = new PackedEntry {
                    Name = string.Format ("{0}#{1:D4}", base_name, i),
                    Type = default_type,
                    Offset = offset,
                };
                offset = file.View.ReadUInt32 (index_offset);
                entry.Size = (uint)(offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            if (string.IsNullOrEmpty (default_type))
            {
                foreach (var entry in dir)
                {
                    uint signature = file.View.ReadUInt32 (entry.Offset);
                    entry.ChangeType (AutoEntry.DetectFileType (signature));
                }
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent)
                return base.OpenEntry (arc, entry);
            if (!pent.IsPacked)
            {
                if (!arc.File.View.AsciiEqual (entry.Offset, "fACE"))
                    return base.OpenEntry (arc, entry);
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+4) ^ 0x65641538;
            }
            using (var input = arc.File.CreateStream (entry.Offset+8, entry.Size-8))
            {
                var data = new byte[pent.UnpackedSize];
                TriFormat.Unpack (input, data);
                return new BinMemoryStream (data, entry.Name);
            }
        }
    }
}
