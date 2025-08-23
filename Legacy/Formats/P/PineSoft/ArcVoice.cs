using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.PineSoft
{
    [Export(typeof(ArchiveFormat))]
    public class CmbAudioOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CMB/VOICE"; } }
        public override string Description { get { return "PineSoft audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public CmbAudioOpener ()
        {
            Extensions = new string[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset <= 0x2C)
                return null;
            int header_size = file.View.ReadInt32 (0);
            int count = file.View.ReadInt32 (0x24);
            if (!IsSaneCount (count) || (count + 1) * 4 + 0x28 != header_size)
                return null;

            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_pos = 0x28;
            uint next_offset = file.View.ReadUInt32 (index_pos);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index_pos += 4;
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D5}", base_name, i),
                    Type = "audio"
                };
                entry.Offset = next_offset;
                next_offset = file.View.ReadUInt32 (index_pos);
                entry.Size = (uint)(next_offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            if (next_offset != file.MaxOffset)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
