using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Hexenhaus
{
    [Export(typeof(ArchiveFormat))]
    public class BinOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/ODIO"; } }
        public override string Description { get { return "Hexenhaus audio archive"; } }
        public override uint     Signature { get { return 0x4F49444F; } } // 'ODIO'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public BinOpener ()
        {
            Extensions = new string[] { "bin" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0 != file.View.ReadUInt32 (4) || 0xCCAE01FF != file.View.ReadUInt32 (0xA))
                return null;
            uint first_offset = file.View.ReadUInt32 (0x12);
            int count = (int)(first_offset - 0x12) / 6;
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);

            uint next_offset = first_offset;
            uint index_offset = 0x12;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}.ogg", base_name, i),
                    Type = "audio",
                    Offset = next_offset,
                };
                index_offset += 6;
                next_offset = i+1 == count ? (uint)file.MaxOffset : file.View.ReadUInt32 (index_offset);
                entry.Size = (uint)(next_offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Size < 0x2C || !arc.File.View.AsciiEqual (entry.Offset, "ONCE"))
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset+0x2C, entry.Size-0x2C);
            return new Ror4EncryptedStream (input);
        }
    }
}
