using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Emic
{
    internal class EmicArchive : ArcFile
    {
        public readonly byte[] Key;

        public EmicArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/EMIC"; } }
        public override string Description { get { return "Emic engine resource archive"; } }
        public override uint     Signature { get { return 0x4B434150; } } // 'PACK'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PacOpener ()
        {
            Extensions = new string[] { "pac" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            byte[] key;
            int count = file.View.ReadInt32 (0x28);
            uint is_encrypted = file.View.ReadUInt32 (4);
            if (IsSaneCount (count) && is_encrypted <= 1)
            {
                key = file.View.ReadBytes (8, 0x20);
                for (int i = 0; i < key.Length; ++i)
                    key[i] ^= 0xAA;
            }
            else
            {
                count = file.View.ReadInt32 (4);
                is_encrypted = file.View.ReadUInt32 (8);
                if (!IsSaneCount (count) || is_encrypted > 1)
                    return null;
                key = file.View.ReadBytes (0xC, 0x20);
                for (int i = 0; i < key.Length; ++i)
                    key[i] ^= 0xAB;
            }
            Stream input = file.CreateStream();
            if (1 == is_encrypted)
                input = new ByteStringEncryptedStream (input, key);
            using (var reader = new BinaryReader (input))
            {
                input.Position = 0x2C;
                var index_buf = new byte[0x108];
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    int name_len = reader.ReadInt32();
                    if (name_len <= 0 || name_len > index_buf.Length) // file name is too long
                        return null;
                    if (name_len != reader.Read (index_buf, 0, name_len))
                        return null;
                    string name = Binary.GetCString (index_buf, 0, name_len);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Size   = reader.ReadUInt32();
                    entry.Offset = reader.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                if (1 == is_encrypted)
                    return new EmicArchive (file, this, dir, key);
                else
                    return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var emic = arc as EmicArchive;
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (null != emic)
                input = new ByteStringEncryptedStream (input, emic.Key, entry.Offset);
            return input;
        }
    }
}
