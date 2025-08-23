using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [010929][BOKU] Odeon

namespace GameRes.Formats.Hdl
{
    [Export(typeof(ArchiveFormat))]
    public class HotOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/HOT"; } }
        public override string Description { get { return "HDL engine resource archive"; } }
        public override uint     Signature { get { return 0x544F48; } } // 'HOT'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadUInt32 (4) != 0)
                return null;
            int count = file.View.ReadInt32 (0xC);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (8);
            if (index_offset >= file.MaxOffset || index_offset + count * 4 > file.MaxOffset)
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D5}", base_name, i),
                    Offset = file.View.ReadUInt32 (index_offset) + 0x20,
                };
                if (entry.Offset > index_offset)
                    return null;
                dir.Add (entry);
                index_offset += 4;
            }
            for (int i = 1; i < count; ++i)
            {
                dir[i-1].Size = (uint)(dir[i].Offset - dir[i-1].Offset);
            }
            dir[count-1].Size = (uint)(index_offset - dir[count-1].Offset);
            foreach (var entry in dir)
            {
                uint signature = file.View.ReadUInt32 (entry.Offset);
                if (0x544F48 == signature) // 'HOT'
                {
                    if (0x21 == (file.View.ReadByte (entry.Offset+7) & 0x21))
                        entry.Type = "image";
                }
                else
                    entry.ChangeType (AutoEntry.DetectFileType (signature));
            }
            return new ArcFile (file, this, dir);
        }
    }
}
