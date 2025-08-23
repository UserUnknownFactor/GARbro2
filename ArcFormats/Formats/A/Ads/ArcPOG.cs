using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Ads
{
    [Export(typeof(ArchiveFormat))]
    public class PogOpener : ArchiveFormat
    {
        public override string         Tag { get { return "060/POG"; } }
        public override string Description { get { return "ads engine audio archive"; } }
        public override uint     Signature { get { return 0x474F50; } } // 'POG'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PogOpener ()
        {
            ContainedFormats = new[] { "OGG", "WAV" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint names_offset = file.View.ReadUInt32 (4);
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            uint pos = 0x10;
            uint next_offset = file.View.ReadUInt32 (pos);
            for (int i = 0; i < count; ++i)
            {
                pos += 4;
                uint offset = next_offset;
                next_offset = file.View.ReadUInt32 (pos);
                var entry = new Entry {
                    Offset = offset,
                    Size = next_offset - offset,
                    Type = "audio",
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            using (var names = file.CreateStream (names_offset))
            {
                names.ReadInt32();
                while (names.PeekByte() != -1)
                {
                    int length = names.ReadInt32();
                    int index = names.ReadInt32();
                    if (index < 0 || index >= count)
                        return null;
                    var name = names.ReadCString (length - 4);
                    dir[index].Name = name;
                }
            }
            return new ArcFile (file, this, dir);
        }
    }
}
