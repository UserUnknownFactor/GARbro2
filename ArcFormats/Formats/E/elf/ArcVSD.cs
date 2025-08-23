using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Silky
{
    [Export(typeof(ArchiveFormat))]
    public class VsdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "VSD/AI5WIN"; } }
        public override string Description { get { return "AI5WIN engine video file"; } }
        public override uint     Signature { get { return 0x31445356; } } // 'VSD1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public VsdOpener ()
        {
            Extensions = new string[] { "vsd" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint skip = file.View.ReadUInt32 (4);
            if (skip >= file.MaxOffset)
                return null;

            var dir = new List<Entry> (1);
            dir.Add (new Entry {
                Name = Path.GetFileNameWithoutExtension (file.Name)+".mpg",
                Type = "video",
                Offset = 8+skip,
                Size = (uint)(file.MaxOffset-(8+skip)),
            });
            return new ArcFile (file, this, dir);
        }
    }
}
