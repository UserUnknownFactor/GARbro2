using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.ShiinaRio
{
    [Export(typeof(ArchiveFormat))]
    public class S25Opener : ArchiveFormat
    {
        public override string         Tag { get { return "S25"; } }
        public override string Description { get { return "ShiinaRio engine multi-image"; } }
        public override uint     Signature { get { return 0x00353253; } } // 'S25'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (count <= 0 || count > 0xfffff)
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            uint index_offset = 8;
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                index_offset += 4;
                if (offset > 0 && offset <= file.MaxOffset)
                {
                    var entry = new Entry
                    {
                        Name = string.Format ("{0}@{1:D4}", base_name, i),
                        Type = "image",
                        Offset = offset,
                    };
                    dir.Add (entry);
                }
            }
            dir.Sort ((a, b) => (int)(a.Offset - b.Offset));
            for (int i = 0; i < dir.Count; ++i)
            {
                long next_offset;
                if (i+1 == dir.Count)
                    next_offset = file.MaxOffset;
                else
                    next_offset = dir[i+1].Offset;
                dir[i].Size = (uint)(next_offset - dir[i].Offset);
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var offset = entry.Offset;
            var info = new S25MetaData
            {
                Width   = arc.File.View.ReadUInt32 (offset),
                Height  = arc.File.View.ReadUInt32 (offset+4),
                OffsetX = arc.File.View.ReadInt32 (offset+8),
                OffsetY = arc.File.View.ReadInt32 (offset+12),
                BPP     = 32,
                FirstOffset = (uint)(offset + 0x14),
                Incremental = 0 != (arc.File.View.ReadUInt32 (offset+0x10) & 0x80000000u),
            };
            var input = arc.File.CreateStream (0, (uint)arc.File.MaxOffset);
            return new S25Format.Reader (input, info);
        }
    }
}
