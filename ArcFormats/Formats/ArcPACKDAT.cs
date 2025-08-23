using System.IO;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Utility;

namespace GameRes.Formats.SystemEpsylon
{
    internal class PackDatEntry : PackedEntry
    {
        public uint Flags;
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PACKDAT"; } }
        public override string Description { get { return "SYSTEM-ε resource archive"; } }
        public override uint     Signature { get { return 0x4B434150; } } // "PACK"
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak", "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "DAT."))
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint index_size = 0x30 * (uint)count;
            if (index_size > file.View.Reserve (0x10, index_size))
                return null;
            var dir = new List<Entry> (count);
            long index_offset = 0x10;
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0x20);
                var entry = FormatCatalog.Instance.Create<PackDatEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x20);
                entry.Flags  = file.View.ReadUInt32 (index_offset+0x24);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x28);
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+0x2c);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x30;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pentry = entry as PackDatEntry;
            if (null == pentry || entry.Size < 4
                || !(0 != (pentry.Flags & 0x10000) || entry.Name.HasExtension (".s")))
                return arc.File.CreateStream (entry.Offset, entry.Size);

            var input = arc.File.View.ReadBytes (entry.Offset, pentry.Size);
            if (0 != (pentry.Flags & 0x10000))
            {
                unsafe
                {
                    fixed (byte* buf_raw = input)
                    {
                        uint* encoded = (uint*)buf_raw;
                        uint key = (uint)pentry.Size >> 2;
                        key ^= key << (((int)key & 7) + 8);
                        for (uint i = (uint)entry.Size / 4; i != 0; --i )
                        {
                            *encoded ^= key;
                            int cl = (int)(*encoded++ % 24);
                            key = Binary.RotL (key, cl);
                        }
                    }
                }
            }
            if (entry.Name.HasExtension (".s"))
            {
                for (int i = 0; i < input.Length; ++i)
                    input[i] ^= 0xFF;
            }
            return new BinMemoryStream (input, entry.Name);
        }
    }
}
