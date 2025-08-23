using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.HyperWorks
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/ACE"; } }
        public override string Description { get { return "HyperWorks resource archive"; } }
        public override uint     Signature { get { return 0x4B434150; } } // 'PACK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int index_size = file.View.ReadInt32 (4);
            if (index_size >= file.MaxOffset - 4)
                return null;
            int count = index_size / 0x18;
            if (!IsSaneCount (count))
                return null;

            uint pos = 8;
            file.View.Reserve (pos, (uint)index_size);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint name_length = file.View.ReadByte (pos+8);
                if (name_length > 15)
                    return null;
                var name = file.View.ReadString (pos+9, name_length);
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (pos);
                entry.Size   = file.View.ReadUInt32 (pos+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                pos += 0x18;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
