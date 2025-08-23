using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Desire
{
    [Export(typeof(ArchiveFormat))]
    public class D000Opener : ArchiveFormat
    {
        public override string         Tag => "000/DESIRE";
        public override string Description => "Desire resource archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasAnyOfExtensions (".000", ".001", ".002", ".003"))
                return null;
            if (!IsAscii (file.View.ReadByte (0)))
                return null;
            uint index_pos = 0;
            var dir = new List<Entry>();
            while (index_pos < file.MaxOffset)
            {
                byte b = file.View.ReadByte (index_pos);
                if (0 == b)
                    break;
                if (!IsAscii (b))
                    return null;
                var name = file.View.ReadString (index_pos, 0xC);
                var entry = Create<Entry> (name);
                entry.Size = file.View.ReadUInt32 (index_pos+0xC);
                if (entry.Size >= file.MaxOffset || 0 == entry.Size)
                    return null;
                dir.Add (entry);
                index_pos += 0x10;
            }
            if (index_pos >= file.MaxOffset || file.View.ReadUInt32 (index_pos+0xC) != 0)
                return null;
            long offset = index_pos + 0x10;
            foreach (var entry in dir)
            {
                entry.Offset = offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                offset += entry.Size;
            }
            if (offset != file.MaxOffset)
                return null;
            return new ArcFile (file, this, dir);
        }

        static internal bool IsAscii (byte b)
        {
            return b >= 0x20 && b < 0x7F;
        }
    }
}
