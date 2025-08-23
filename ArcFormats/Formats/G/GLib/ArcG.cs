using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.GLib
{
    internal class GmlArchive : ArcFile
    {
        public readonly byte[] Key;

        public GmlArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    internal class GmlEntry : Entry
    {
        public byte[] Header;
    }

    [Export(typeof(ArchiveFormat))]
    public class GOpener : ArchiveFormat
    {
        public override string         Tag { get { return "G/GML"; } }
        public override string Description { get { return "GLib engine resource archive"; } }
        public override uint     Signature { get { return 0x5F4C4D47; } } // 'GML_'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public GOpener ()
        {
            Extensions = new string[] { "g", "xp" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "ARC\0"))
                return null;
            uint data_offset   = file.View.ReadUInt32 (8);
            uint unpacked_size = file.View.ReadUInt32 (0xC);
            uint packed_size   = file.View.ReadUInt32 (0x10);
            byte[] unpacked = new byte[unpacked_size];
            using (var packed = file.CreateStream (0x14, packed_size))
            using (var input = new XoredStream (packed, 0xFF))
            {
                LzssUnpack (input, unpacked);
            }
            using (var index = new BinMemoryStream (unpacked))
            {
                var key = index.ReadBytes (256);
                int count = index.ReadInt32();
                if (!IsSaneCount (count))
                    return null;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    int name_length = index.ReadInt32();
                    var name = index.ReadCString (name_length);
                    var entry = FormatCatalog.Instance.Create<GmlEntry> (name);
                    entry.Offset = index.ReadUInt32() + data_offset;
                    entry.Size   = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    entry.Header = index.ReadBytes (4);
                }
                return new GmlArchive (file, this, dir, key);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var garc = arc as GmlArchive;
            var gent = entry as GmlEntry;
            if (null == garc || null == gent)
                return base.OpenEntry (arc, entry);
            var data = garc.File.View.ReadBytes (entry.Offset, entry.Size);
            for (int i = gent.Header.Length; i < data.Length; ++i)
                data[i] = garc.Key[data[i]];
            Buffer.BlockCopy (gent.Header, 0, data, 0, gent.Header.Length);
            return new BinMemoryStream (data, entry.Name);
        }

        internal static void LzssUnpack (Stream input, byte[] output)
        {
            const int frame_mask = 0xFFF;
            byte[] frame = new byte[0x1000];
            int frame_pos = 0xFEE;
            int dst = 0;
            int ctl = 2;
            while (dst < output.Length)
            {
                ctl >>= 1;
                if (1 == ctl)
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        break;
                    ctl |= 0x100;
                }
                if (0 != (ctl & 1))
                {
                    int b = input.ReadByte();
                    if (-1 == b)
                        break;
                    output[dst++] = frame[frame_pos++ & frame_mask] = (byte)b;
                }
                else
                {
                    int lo = input.ReadByte();
                    if (-1 == lo)
                        break;
                    int hi = input.ReadByte();
                    if (-1 == hi)
                        break;
                    int offset = (hi & 0xf0) << 4 | lo;
                    int count = Math.Min ((~hi & 0xF) + 3, output.Length-dst);
                    while (count --> 0)
                    {
                        byte v = frame[offset++ & frame_mask];
                        frame[frame_pos++ & frame_mask] = v;
                        output[dst++] = v;
                    }
                }
            }
        }
    }
}
