using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.DigitalWorks
{
    [Export(typeof(ArchiveFormat))]
    public class PacSingleOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/LZS-TIM2"; } }
        public override string Description { get { return "LZS-TIM2 Image archive"; } }
        public override uint     Signature { get { return 0x535A4C; } } // 'LZS'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        /**
         Target games:
        Cafe Little Wish SLPM-65294
        F Fanatic SLPM-65296
         */

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual(9, "TIM2"))
                return null;
            var dir = new List<Entry> (1);
            var entry = FormatCatalog.Instance.Create<PackedEntry> (file.Name);
            entry.Offset = 0L;
            entry.Size   = (uint)file.MaxOffset;
            if (!entry.CheckPlacement (file.MaxOffset))
                return null;
            dir.Add (entry);
            
            return new ArcFile (file, this, dir);    
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent)
                return base.OpenEntry (arc, entry);
            if (!pent.IsPacked)
            {
                if (!arc.File.View.AsciiEqual (entry.Offset, "LZS\0"))
                    return base.OpenEntry (arc, entry);
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+4);
            }
            var input = arc.File.CreateStream (entry.Offset+8, entry.Size-8);
            bool embedded_lzs = (input.Signature & ~0xF0u) == 0x535A4C0F; // 'LZS'
            var lzs = new LzssStream (input);
            if (embedded_lzs)
            {
                var header = new byte[8];
                lzs.Read (header, 0, 8);
                pent.UnpackedSize = header.ToUInt32 (4);
                lzs = new LzssStream (lzs);
            }
            return lzs;
        }
    }
}
