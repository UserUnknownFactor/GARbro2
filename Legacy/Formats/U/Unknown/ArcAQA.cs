using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

// [040917][Unknown] Abandoner

namespace GameRes.Formats.Unknown
{
    [Export(typeof(ArchiveFormat))]
    public class AqaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AQA"; } }
        public override string Description { get { return "'Unknown' resource archive"; } }
        public override uint     Signature { get { return 0x20415141; } } // 'AQA '
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (12);
            if (!IsSaneCount (count))
                return null;
            ushort key = (ushort)(((101 * file.View.ReadUInt32 (8) + 777) & 0xFFFF) + 1);
            uint index_size = 0x90 * (uint)count;
            var index = file.View.ReadBytes (0x18, 0x90 * (uint)count);
            for (int i = 0; i < index.Length; i += 2)
            {
                index[i  ] ^= (byte)key;
                index[i+1] ^= (byte)(key >> 8);
            }
            uint data_offset = index_size + 0x18;
            int offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = Binary.GetCString (index, offset, 0x80);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = index.ToUInt32 (offset+0x88) + data_offset;
                entry.Size   = index.ToUInt32 (offset+0x80);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                offset += 0x90;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
