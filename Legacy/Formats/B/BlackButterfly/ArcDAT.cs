using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.BlackButterfly
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/PITA"; } }
        public override string Description { get { return "Black Butterfly resource archive"; } }
        public override uint     Signature { get { return 0x41544950; } } // 'PITA'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            uint index_pos = 0x10;
            uint next_offset = file.View.ReadUInt32 (index_pos);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index_pos += 4;
                var entry = new PackedEntry {
                    Name = string.Format ("{0:D5}.bmp", i),
                    Type = "image",
                    Offset = next_offset,
                };
                next_offset = file.View.ReadUInt32 (index_pos);
                entry.Size = (uint)(next_offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            if (!pent.IsPacked)
            {
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (pent.Offset);
            }
            using (var input = arc.File.CreateStream (pent.Offset+4, pent.Size-4))
            {
                var data = new byte[pent.UnpackedSize];
                Unpack (input, data);
                return new BinMemoryStream (data, entry.Name);
            }
        }

        void Unpack (IBinaryStream input, byte[] output)
        {
            int dst = 0;
            while (input.PeekByte() != -1)
            {
                byte ctl = input.ReadUInt8();
                if (0x7F == ctl)
                {
                    if (input.PeekByte() == 0xFF)
                        break;
                }
                int count;
                if (ctl <= 0x7F)
                {
                    count = (ctl >> 2) + 2;
                    int offset = (ctl & 3) << 8 | input.ReadUInt8();
                    offset = (offset ^ 0x3FF) + 1;
                    Binary.CopyOverlapped (output, dst - offset, dst, count);
                    dst += count;
                }
                else if (ctl > 0xFE)
                {
                    count = input.ReadUInt8() + 32;
                    while (count --> 0)
                        output[dst++] = 0;
                }
                else if (ctl > 0xDF)
                {
                    count = (ctl & 0x1F) + 1;
                    while (count --> 0)
                        output[dst++] = 0;
                }
                else if (ctl > 0xBF)
                {
                    count = (ctl & 0x1F) + 2;
                    byte fill = input.ReadUInt8();
                    while (count --> 0)
                        output[dst++] = fill;
                }
                else if (ctl > 0x9F)
                {
                    count = (ctl & 0x1F) + 1;
                    while (count --> 0)
                    {
                        output[dst++] = 0;
                        output[dst++] = input.ReadUInt8();
                    }
                }
                else
                {
                    count = (ctl & 0x1F) + 1;
                    input.Read (output, dst, count);
                    dst += count;
                }
            }
        }
    }
}
