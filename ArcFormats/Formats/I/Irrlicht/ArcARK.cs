using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Security.Cryptography;

namespace GameRes.Formats.Irrlicht
{
    [Export(typeof(ArchiveFormat))]
    public class ArkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARK"; } }
        public override string Description { get { return "Irrlicht engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;

            int index_offset = 4;
            int first_offset = file.View.ReadInt32 (index_offset+0x104);
            if (first_offset != (index_offset + count * 0x10C))
                return null;
            var name_buffer = new byte[0x104];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (index_offset, name_buffer, 0, 0x104);
                int l;
                for (l = 0; l < name_buffer.Length && 0xFF != name_buffer[l]; ++l)
                    name_buffer[l] ^= 0xFF;
                if (0 == l)
                    return null;
                var name = Encodings.cp932.GetString (name_buffer, 0, l);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x104);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x108);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10C;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new InputCryptoStream (input, new NotTransform());
        }
    }
}
