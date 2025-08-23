using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Purple
{
    [Export(typeof(ArchiveFormat))]
    public class Cpz1Opener : ArchiveFormat
    {
        public override string         Tag { get { return "CPZ1"; } }
        public override string Description { get { return "CVNS engine resource archive"; } }
        public override uint     Signature { get { return 0x315A5043; } } // 'CPZ1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Cpz1Opener ()
        {
            Extensions = new string[] { "cpz" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint index_size = file.View.ReadUInt32 (8);
            var index = file.View.ReadBytes (0x10, index_size);
            DecryptData (index, DefaultKey);
            long base_offset = 0x10 + index_size;
            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int entry_size = LittleEndian.ToInt32 (index, index_offset);
                if (entry_size <= 0 || entry_size > index.Length - index_offset)
                    return null;
                var name = Binary.GetCString (index, index_offset+0x18);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size = LittleEndian.ToUInt32 (index, index_offset+4);
                entry.Offset = LittleEndian.ToUInt32 (index, index_offset+8) + base_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += entry_size;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            DecryptData (data, DefaultKey);
            if (Binary.AsciiEqual (data, "PSS0"))
                data = CpzOpener.UnpackLzss (data);
            return new BinMemoryStream (data, entry.Name);
        }

        void DecryptData (byte[] data, byte[] key)
        {
            if (key.Length < 0x40)
                throw new System.ArgumentException ("Invalid CPZ1 key");
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)((data[i] ^ key[i & 0x3F]) - 0x6C);
            }
        }

        static readonly byte[] DefaultKey = {
            0x92, 0xCD, 0x97, 0x90, 0x8C, 0xD7, 0x8C, 0xD5, 0x8B, 0x4B, 0x93, 0xFA, 0x9A, 0xD7, 0x8C, 0xBF,
            0x8C, 0xC9, 0x8C, 0xEB, 0x8D, 0x69, 0x8D, 0x8B, 0x8C, 0xD2, 0x8C, 0xD6, 0x8B, 0x6D, 0x8C, 0xE3,
            0x8C, 0xFB, 0x8C, 0xD0, 0x8C, 0xC8, 0x8C, 0xF0, 0x8B, 0xFE, 0x8C, 0xAA, 0x8C, 0xF4, 0x8B, 0x4B,
            0x9C, 0x58, 0x8C, 0xD3, 0x96, 0xC8, 0x8C, 0xCB, 0x8C, 0xCE, 0x8C, 0xF3, 0x8C, 0xD6, 0x8B, 0x52,
        };
    }
}
