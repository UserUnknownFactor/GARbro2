using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.MyHarvest
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag => "DAT/UNA";
        public override string Description => "MyHarvest resource archive";
        public override uint     Signature => 0x414E55; // 'UNA'
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "001\0"))
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint index = 0x20;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index, 0x20);
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index+0x20);
                entry.Size   = file.View.ReadUInt32 (index+0x24);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index += 0x30;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
