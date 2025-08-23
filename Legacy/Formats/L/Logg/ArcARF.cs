using GameRes.Utility;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [980417][Logg] Tenshi Kourin
// [980828][Logg] Kazeiro no Romance

namespace GameRes.Formats.Logg
{
    [Export(typeof(ArchiveFormat))]
    public class ArfOpener : ArchiveFormat
    {
        public override string         Tag => "ARF";
        public override string Description => "Logg archive file";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => true;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;

            uint index = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index);
                if (offset <= index || offset > file.MaxOffset)
                    return null;
                uint size   = file.View.ReadUInt32 (index+4);
                byte name_len = file.View.ReadByte (index+8);
                var name = file.View.ReadString (index+9, name_len);
                var entry = Create<PackedEntry> (name);
                entry.Offset = offset;
                entry.UnpackedSize = size;
                dir.Add (entry);
                index += name_len + 9u;
                if (index > dir[0].Offset)
                    return null;
            }
            long last_offset = file.MaxOffset;
            for (int i = count-1; i >= 0; --i)
            {
                var entry = dir[i] as PackedEntry;
                entry.Size = (uint)(last_offset - entry.Offset);
                last_offset = entry.Offset;
                if (string.IsNullOrEmpty (entry.Name))
                    dir.RemoveAt (i);
                else
                    entry.IsPacked = entry.Size != entry.UnpackedSize;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            if (!pent.IsPacked)
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (pent.Offset, pent.Size);
            var output = new byte[pent.UnpackedSize];
            Decompress (input, output);
            return new BinMemoryStream (output, pent.Name);
        }

        void Decompress (IBinaryStream input, byte[] output)
        {
            using (var bits = new LsbBitStream (input.AsStream, true))
            {
                int dst = 0;
                while (dst < output.Length)
                {
                    if (bits.GetNextBit() == 0)
                    {
                        output[dst++] = (byte)bits.GetBits (8);
                    }
                    else
                    {
                        int count;
                        if (bits.GetNextBit() == 0)
                            count = 2;
                        else if (bits.GetNextBit() == 0)
                            count = 3;
                        else if (bits.GetNextBit() == 0)
                            count = 4;
                        else if (bits.GetNextBit() == 0)
                            count = 5;
                        else
                        {
                            switch (bits.GetBits (2))
                            {
                            case 0: count = 6; break;
                            case 1: count = bits.GetBits (2) + 7; break;
                            case 2: count = bits.GetBits (4) + 11; break;
                            case 3: count = bits.GetBits (10) + 26; break;
                            default: throw new EndOfStreamException();
                            }
                        }
                        int offset;
                        if (bits.GetNextBit() == 0)
                            offset = bits.GetBits (8);
                        else if (bits.GetNextBit() == 0)
                            offset = bits.GetBits (10) + 0x100;
                        else
                            offset = bits.GetBits (12) + 0x500;
                        Binary.CopyOverlapped (output, dst - offset - 1, dst, count);
                        dst += count;
                    }
                }
            }
        }
    }
}
