using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Ail
{
    [Export(typeof(ArchiveFormat))]
    public class Lnk2Opener : DatOpener
    {
        public override string         Tag { get { return "DAT/LNK2"; } }
        public override string Description { get { return "Ail resource archive"; } }
        public override uint     Signature { get { return 0x324B4E4C; } } // 'LNK2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Lnk2Opener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4)*2;
            if (!IsSaneCount (count))
                return null;
            var dir = ReadIndex (file, 8, count);
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
