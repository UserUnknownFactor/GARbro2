using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GameRes.Formats.Artemis
{
    [Export(typeof(ArchiveFormat))]
    public class PfsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PFS"; } }
        public override string Description { get { return "Artemis engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PfsOpener ()
        {
            Extensions = new string[] { "pfs", "000", "001", "002", "003", "004", "005", "010" };
            ContainedFormats = new string[] { "PNG", "JPEG", "IPT", "OGG", "TXT", "SCR" };
            Settings = new[] { PfsEncoding };
        }

        EncodingSetting PfsEncoding = new EncodingSetting ("PFSEncodingCP", "DefaultEncoding");

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "pf"))
                return null;
            int version = file.View.ReadByte (2) - '0';
            switch (version)
            {
            case 6:
            case 8:
                try
                {
                    return OpenPf (file, version, PfsEncoding.Get<Encoding>());
                }
                catch (System.ArgumentException)
                {
                    return OpenPf (file, version, GetAltEncoding());
                }
            case 2:     return OpenPf2 (file);
            default:    return null;
            }
        }

        ArcFile OpenPf (ArcView file, int version, Encoding encoding)
        {
            uint index_size = file.View.ReadUInt32 (3);
            int count = file.View.ReadInt32 (7);
            if (!IsSaneCount (count) || 7L + index_size > file.MaxOffset)
                return null;
            var index = file.View.ReadBytes (7, index_size);
            int index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_length = index.ToInt32 (index_offset);
                var name = encoding.GetString (index, index_offset+4, name_length);
                index_offset += name_length + 8;
                var entry = Create<Entry> (name);
                entry.Offset = index.ToUInt32 (index_offset);
                entry.Size   = index.ToUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                index_offset += 8;
                dir.Add (entry);
            }
            if (version != 8 && version != 9 && version != 4 && version != 5)
                return new ArcFile (file, this, dir);

            // key calculated for archive versions 4, 5, 8 and 9
            using (var sha1 = SHA1.Create())
            {
                var key = sha1.ComputeHash (index);
                return new PfsArchive (file, this, dir, key);
            }
        }

        ArcFile OpenPf2 (ArcView file)
        {
            uint index_size = file.View.ReadUInt32 (3);
            int count = file.View.ReadInt32 (0xB);
            if (!IsSaneCount (count) || 7L + index_size > file.MaxOffset)
                return null;
            var index = file.View.ReadBytes (7, index_size);
            int index_offset = 8;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_length = index.ToInt32 (index_offset);
                var name = Encodings.cp932.GetString (index, index_offset+4, name_length);
                index_offset += name_length + 0x10;
                var entry = Create<Entry> (name);
                entry.Offset = index.ToUInt32 (index_offset);
                entry.Size   = index.ToUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                index_offset += 8;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var parc = arc as PfsArchive;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (null == parc)
                return input;
            return new ByteStringEncryptedStream (input, parc.Key);
        }

        Encoding GetAltEncoding ()
        {
            var enc = PfsEncoding.Get<Encoding>();
            if (enc.CodePage == 932)
                return Encoding.UTF8;
            else
                return Encodings.cp932;
        }
    }

    internal class PfsArchive : ArcFile
    {
        public readonly byte[] Key;

        public PfsArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }
}
