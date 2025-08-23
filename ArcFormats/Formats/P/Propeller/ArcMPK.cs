using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Propeller
{
    [Export(typeof(ArchiveFormat))]
    public class MpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MPK"; } }
        public override string Description { get { return "Propeller resources archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_offset = file.View.ReadUInt32 (0);
            int count = file.View.ReadInt32 (4);
            if (index_offset < 8 || index_offset >= file.MaxOffset || !IsSaneCount (count))
                return null;
            uint index_size = (uint)count * 0x28u;
            if (index_size > file.MaxOffset - index_offset)
                return null;
            var index = file.View.ReadBytes (index_offset, index_size);
            // last byte of the first filename presumably is zero
            byte key = index[0x1F];
            for (int i = 0; i < index.Length; ++i)
                index[i] ^= key;

            int current = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_offset = '\\' == index[current] ? 1 : 0;
                var name = Binary.GetCString (index, current+name_offset, 0x20-name_offset);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = LittleEndian.ToUInt32 (index, current+0x20);
                entry.Size   = LittleEndian.ToUInt32 (index, current+0x24);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                current += 0x28;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (!entry.Name.HasExtension (".msc")
                || 0x88 != input.PeekByte())
                return input;
            return new XoredStream (input, 0x88);
        }
    }
}
