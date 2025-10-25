using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GameRes.Formats.Artemis
{
    [Export(typeof(ArchiveFormat))]
    public class PfsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PFS"; } }
        public override string Description { get { return "Artemis engine resource archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        public PfsOpener ()
        {
            Extensions = new string[] { "pfs", "000", "001", "002", "003", "004", "005", "010", "011", "012" };
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

        public override void Create (
            Stream output, IEnumerable<Entry> list, ResourceOptions options, 
            EntryCallback callback)
        {
            var pfs_options = GetOptions<PfsOptions> (options);
            var encoding = pfs_options.Encoding ?? PfsEncoding.Get<Encoding>();
            int version = pfs_options.Version;

            var entries = list.ToList();
            int file_count = entries.Count;

            long data_offset_base = 7; // "pf" + version + index_size (4)
            if (version == 2)
                data_offset_base = 0x0F; // version 2 has extra 4 bytes
            else
                data_offset_base = 0x0B; // + count (4)

            using (var index_stream = new MemoryStream())
            using (var index_writer = new BinaryWriter (index_stream))
            {
                index_writer.Write (file_count);
                if (version == 2)
                {
                    index_writer.Write ((int)0); // reserved
                }
                var file_sizes = new List<uint>();
                foreach (var entry in entries)
                {
                    var file_info = new FileInfo (entry.Name);
                    if (!file_info.Exists)
                        throw new FileNotFoundException ("File not found", entry.Name);
                    if (file_info.Length > uint.MaxValue)
                        throw new FileSizeException();

                    file_sizes.Add ((uint)file_info.Length);

                    if (version == 2)
                    {
                        var name_bytes = Encodings.cp932.GetBytes (entry.Name);
                        index_writer.Write (name_bytes.Length);
                        index_writer.Write (name_bytes);

                        index_writer.Write ((int)0); // unknown field 1
                        index_writer.Write ((int)0); // unknown field 2
                        index_writer.Write ((int)0); // unknown field 3

                        index_writer.Write ((uint)0); // offset placeholder
                        index_writer.Write ((uint)0); // size placeholder
                    }
                    else
                    {
                        var name_bytes = encoding.GetBytes (entry.Name);
                        index_writer.Write (name_bytes.Length);
                        index_writer.Write (name_bytes);
                        index_writer.Write ((int)0); // reserved
                        index_writer.Write ((uint)0); // offset placeholder
                        index_writer.Write ((uint)0); // size placeholder
                    }
                }

                var index_bytes = index_stream.ToArray();

                long current_offset = data_offset_base + index_bytes.Length;
                int index_pos = (version == 2) ? 8 : 4; // skip header

                for (int i = 0; i < file_count; ++i)
                {
                    int name_length = BitConverter.ToInt32 (index_bytes, index_pos);
                    index_pos += 4 + name_length;

                    if (version == 2)
                        index_pos += 12; // skip reserved
                    else
                        index_pos += 4; // skip reserved

                    // Write actual offset
                    var offset_bytes = BitConverter.GetBytes ((uint)current_offset);
                    Buffer.BlockCopy (offset_bytes, 0, index_bytes, index_pos, 4);

                    // Write actual size
                    var size_bytes = BitConverter.GetBytes (file_sizes[i]);
                    Buffer.BlockCopy (size_bytes, 0, index_bytes, index_pos + 4, 4);

                    entries[i].Offset = current_offset;
                    entries[i].Size = file_sizes[i];

                    current_offset += file_sizes[i];
                    index_pos += 8;
                }

                byte[] key = null;
                if (version == 4 || version == 5 || version == 8 || version == 9)
                using (var sha1 = SHA1.Create())
                {
                    key = sha1.ComputeHash (index_bytes);
                }

                output.WriteByte ((byte)'p');
                output.WriteByte ((byte)'f');
                output.WriteByte ((byte)('0' + version));

                var header_writer = new BinaryWriter (output);
                header_writer.Write ((uint)index_bytes.Length);

                if (version == 2)
                    header_writer.Write ((int)0); // reserved

                header_writer.Write (file_count);

                output.Write (index_bytes, 0, index_bytes.Length);

                foreach (var entry in entries)
                {
                    if (null != callback)
                        callback (file_count, entry, Localization._T ("MsgAddingFile"));

                    using (var input = File.OpenRead (entry.Name))
                    {
                        if (key != null)
                            CopyEncrypted (input, output, key);
                        else
                            input.CopyTo (output);
                    }
                }
            }
        }

        void CopyEncrypted (Stream input, Stream output, byte[] key)
        {
            var buffer = new byte[0x10000];
            int key_pos = 0;
            int read;
            while ((read = input.Read (buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; ++i)
                    buffer[i] ^= key[key_pos++ % key.Length];
                output.Write (buffer, 0, read);
            }
        }

        Encoding GetAltEncoding ()
        {
            var enc = PfsEncoding.Get<Encoding>();
            if (enc.CodePage == 932)
                return Encoding.UTF8;
            else
                return Encodings.cp932;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new PfsOptions 
            { 
                Version = 6,
                Encoding = PfsEncoding.Get<Encoding>()
            };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            var w = widget as GUI.WidgetPFS;
            if (null == w)
                return GetDefaultOptions();
            return new PfsOptions
            {
                Version = w.Version,
                Encoding = w.GetEncoding()
            };
        }

        public override object GetCreationWidget ()
        {
            return new GUI.WidgetPFS();
        }
    }

    public class PfsOptions : ResourceOptions
    {
        public int       Version { get; set; }
        public Encoding Encoding { get; set; }
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
