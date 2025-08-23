using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.RealLive
{
    [Export(typeof(ArchiveFormat))]
    public class SeenOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SEEN"; } }
        public override string Description { get { return "AVG32 engine scripts archive"; } }
        public override uint     Signature { get { return 0x4C434150; } } // 'PACL'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public SeenOpener ()
        {
            Extensions = new string[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x20;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset        = file.View.ReadUInt32 (index_offset+0x10);
                entry.Size          = file.View.ReadUInt32 (index_offset+0x14);
                entry.UnpackedSize  = file.View.ReadUInt32 (index_offset+0x18);
                entry.IsPacked      = file.View.ReadUInt32 (index_offset+0x1C) != 0;
                if (entry.Size > 0)
                {
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked || input.Signature != 0x4B434150) // 'PACK'
                return input;
            using (input)
            {
                input.Position = 8;
                int unpacked_size = input.ReadInt32();
                var output = new byte[unpacked_size];
                input.Position = 0x10;
                LzDecompress (input, output);
                return new BinMemoryStream (output, entry.Name);
            }
        }

        internal static void LzDecompress (IBinaryStream input, byte[] output)
        {
            int dst = 0;
            int bits = 0;
            int mask = 0;
            while (dst < output.Length)
            {
                mask >>= 1;
                if (0 == mask)
                {
                    bits = input.ReadUInt8();
                    mask = 0x80;
                }
                if (0 != (bits & mask))
                {
                    output[dst++] = input.ReadUInt8();
                }
                else
                {
                    int offset = input.ReadUInt16();
                    int count = (offset & 0xF) + 2;
                    offset >>= 4;
                    Binary.CopyOverlapped (output, dst-offset-1, dst, count);
                    dst += count;
                }
            }
        }
    }
}
