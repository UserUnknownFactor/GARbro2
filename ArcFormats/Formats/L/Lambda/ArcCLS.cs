using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Lambda
{
    [Export(typeof(ArchiveFormat))]
    public class ClsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/CLS"; } }
        public override string Description { get { return "Lambda engine resource archive"; } }
        public override uint     Signature { get { return 0x5F534C43; } } // 'CLS_'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "FILELINK"))
                return null;
            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (0x18);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = file.View.ReadString (index_offset, 0x28),
                    Offset = file.View.ReadUInt32 (index_offset+0x2C),
                    Size = file.View.ReadUInt32 (index_offset+0x30),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x40;
            }
            DetectFileTypes (file, dir);
            return new ArcFile (file, this, dir);
        }

        void DetectFileTypes (ArcView file, IEnumerable<Entry> dir)
        {
            foreach (var entry in dir)
            {
                uint signature = file.View.ReadUInt32 (entry.Offset);
                if (0x5F534C43 == signature) // 'CLS_'
                {
                    if (file.View.AsciiEqual (entry.Offset+4, "TEXFILE"))
                        entry.Type = "image";
                }
                else
                {
                    var res = AutoEntry.DetectFileType (signature);
                    if (res != null)
                        entry.ChangeType (res);
                }
            }
        }
    }
}
