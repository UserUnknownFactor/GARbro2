using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.WestGate
{
    [Export(typeof(ArchiveFormat))]
    public class UsfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "USF"; } }
        public override string Description { get { return "West Gate resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public UsfOpener ()
        {
            Extensions = new string[] { "alh", "usf", "udc", "uwb", "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint first_offset = file.View.ReadUInt32 (0xC);
            if (first_offset >= file.MaxOffset || 0 != (first_offset & 0xF))
                return null;
            int count = (int)(first_offset / 0x10);
            if (!IsSaneCount (count))
                return null;

            var dir = UcaTool.ReadIndex (file, 0, count, "");
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
