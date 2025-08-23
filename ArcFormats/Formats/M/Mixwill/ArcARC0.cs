using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Mixwill
{
    [Export(typeof(ArchiveFormat))]
    public class Arc0Opener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC0"; } }
        public override string Description { get { return "Mixwill soft resource archive"; } }
        public override uint     Signature { get { return 0x30435241; } } // 'ARC0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Arc0Opener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0x10004);
            if (!IsSaneCount (count))
                return null;
            var name_buf = new byte[0x100];
            uint index_offset = 0x10008;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (0x100 != file.View.Read (index_offset, name_buf, 0, 0x100))
                    return null;
                int n;
                for (n = 0; n < 0x100; ++n)
                {
                    name_buf[n] ^= (byte)n;
                    if (0 == name_buf[n])
                        break;
                }
                if (0 == n)
                    return null;
                index_offset += 0x100;
                var name = Encodings.cp932.GetString (name_buf, 0, n);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size   = file.View.ReadUInt32 (index_offset);
                entry.Offset = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            long encrypted_size = entry.Size;
            if (!entry.Name.HasExtension (".txt")
                && encrypted_size > 0x100)
                encrypted_size = 0x100;
            var prefix = arc.File.View.ReadBytes (entry.Offset, encrypted_size);
            for (int i = 0; i < prefix.Length; ++i)
                prefix[i] ^= (byte)i;
            if (entry.Size == encrypted_size)
                return new BinMemoryStream (prefix, entry.Name);
            var rest = arc.File.CreateStream (entry.Offset+encrypted_size, entry.Size-encrypted_size);
            return new PrefixStream (prefix, rest);
        }
    }
}
