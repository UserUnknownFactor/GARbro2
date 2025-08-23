using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.FrontWing
{
    internal class FgEntry : Entry
    {
        public ImageMetaData    Info;
    }

    [Export(typeof(ArchiveFormat))]
    public class FgOpener : ArchiveFormat
    {
        public override string         Tag { get { return "FG"; } }
        public override string Description { get { return "FrontWing multi-layer image"; } }
        public override uint     Signature { get { return 0x49475746; } } // 'FWGI'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var dir = new List<Entry>();
            uint index_offset = 4;
            while (index_offset < file.MaxOffset && file.View.ReadInt32 (index_offset) == 1)
            {
                var info = new ImageMetaData {
                    OffsetX = file.View.ReadInt32 (index_offset+8),
                    OffsetY = file.View.ReadInt32 (index_offset+0xC),
                    Width   = file.View.ReadUInt32 (index_offset+0x18),
                    Height  = file.View.ReadUInt32 (index_offset+0x1C),
                    BPP     = 32,
                };
                var name = file.View.ReadString (index_offset+0x20, 0x104);
                var entry = Create<FgEntry> (Path.GetFileName (name));
                entry.Info = info;
                entry.Offset = file.View.ReadUInt32 (index_offset+0x124) + 4;
                entry.Size   = file.View.ReadUInt32 (index_offset+0x128);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x1AC;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var fgent = (FgEntry)entry;
            var input = arc.OpenBinaryEntry (entry);
            var bmp_info = ImageFormat.Bmp.ReadMetaData (input);
            input.Position = 0;
            if (null == bmp_info)
                return ImageFormatDecoder.Create (input);
            bmp_info.OffsetX = fgent.Info.OffsetX;
            bmp_info.OffsetY = fgent.Info.OffsetY;
            return new ImageFormatDecoder (input, ImageFormat.Bmp, bmp_info);
        }
    }
}
