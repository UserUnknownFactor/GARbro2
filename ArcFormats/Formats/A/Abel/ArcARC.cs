using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Abel
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/ADVENGINE"; } }
        public override string Description { get { return "ADVEngine resource archive"; } }
        public override uint     Signature { get { return 0x00637261; } } // 'arc'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "" };
        }

        const int IndexEntrySize = 0x26;

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint base_offset = file.View.ReadUInt32 (0xC);
            if (base_offset <= 0x18 || base_offset >= file.MaxOffset)
                return null;
            uint packed_size = file.View.ReadUInt32 (0x10);
            int index_size = file.View.ReadInt32 (0x14);
            if (packed_size > file.MaxOffset || index_size / IndexEntrySize != count)
                return null;
            var name_buffer = new byte[30];
            using (var packed = file.CreateStream (0x18, packed_size))
            using (var lzss = new LzssStream (packed))
            using (var index = new ArcView.Reader (lzss))
            {
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    if (name_buffer.Length != index.Read (name_buffer, 0, name_buffer.Length))
                        return null;
                    var name = Binary.GetCString (name_buffer, 0, name_buffer.Length);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    if (name.HasExtension (".acd"))
                        entry.Type = "script";
                    entry.Offset = index.ReadUInt32() + base_offset;
                    entry.Size   = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Size > 12 && entry.Name.HasExtension (".cmp")
                && arc.File.View.AsciiEqual (entry.Offset, "CMP\0"))
                return OpenCmpEntry (arc, entry);
            if (entry.Size > 8 && entry.Name.HasExtension (".acd")
                && arc.File.View.AsciiEqual (entry.Offset, "ACD\0"))
                return OpenAcdEntry (arc, entry);
            return base.OpenEntry (arc, entry);
        }

        Stream OpenAcdEntry (ArcFile arc, Entry entry)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            for (int i = 8; i < data.Length; ++i)
            {
                data[i] = (byte)(0xFF - data[i]);
            }
            return new BinMemoryStream (data, entry.Name);
        }

        Stream OpenCmpEntry (ArcFile arc, Entry entry)
        {
            uint offset = arc.File.View.ReadUInt32 (entry.Offset+8);
            if (offset >= entry.Size)
                return base.OpenEntry (arc, entry);
            long cmp_offset = entry.Offset + offset;
            if (arc.File.View.ReadByte (cmp_offset) == 0)
            {
                uint packed_size = arc.File.View.ReadUInt32 (cmp_offset+5);
                if (packed_size == entry.Size - (offset+0x11))
                {
                    var input = arc.File.CreateStream (cmp_offset+0x11, packed_size);
                    return new LzssStream (input);
                }
            }
            return arc.File.CreateStream (entry.Offset+offset, entry.Size-offset);
        }
    }
}
