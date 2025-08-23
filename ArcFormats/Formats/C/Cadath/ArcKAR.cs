using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Cadath
{
    [Export(typeof(ArchiveFormat))]
    public class KarOpener : ArchiveFormat
    {
        public override string         Tag { get { return "KAR"; } }
        public override string Description { get { return "Cadath resource archive"; } }
        public override uint     Signature { get { return 0x52414B; } } // 'KAR'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public KarOpener ()
        {
            Extensions = new string[] { "bin" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0xC;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x20);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x20);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x24);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x28;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (entry.Name.HasExtension (".ns6"))
            {
                byte key = (byte)(entry.Size / 7);
                return new XoredStream (input, key);
            }
            else if (entry.Name.HasExtension (".ns5"))
            {
                byte key = (byte)(entry.Size / 13);
                return new XoredStream (input, key);
            }
            return input;
        }
    }
}
