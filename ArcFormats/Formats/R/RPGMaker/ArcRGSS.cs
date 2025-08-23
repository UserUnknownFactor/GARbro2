using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Formats;
using GameRes.Formats.Properties;

namespace GameRes.Formats.RPGMaker
{
    [Export (typeof (ArchiveFormat))]
    [ExportMetadata("Priority", 10)]
    public class RgssArchive : ArchiveFormat
    {
        public override string         Tag { get { return "RGSSAD"; } }
        public override string Description { get { return "RPG Maker XP/VX/ACE engine resource archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  true; } }

        public RgssArchive()
        {
            Extensions = new string[] { "rgss3a", "rgssad", "rgss2a", "exe" };
        }

        public byte[] hrgss_header = { (byte)'R', (byte)'G', (byte)'S', (byte)'S', (byte)'A', (byte)'D' };

        public override ArcFile TryOpen (ArcView file)
        {
            long base_offset = ExeFile.FindSignature (file, hrgss_header);

            if (!file.View.AsciiEqual (base_offset + 4, "AD\0"))
                return null;

            int version = file.View.ReadByte (base_offset + 7);

            using (var index = file.CreateStream (base_offset))
            {
                List<Entry> dir = null;
                switch (version)
                {
                case 1:
                    Comment = "XP Archive";
                    dir = ReadIndexV1 (index);
                    break;
                case 2:
                case 3:
                    Comment = version == 2 ? "VX Archive" : "VX Ace Archive";
                    dir = ReadIndexV3 (index);
                    break;
                default:
                    return null;
                }
                if (null == dir || 0 == dir.Count)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }

        List<Entry> ReadIndexV1 (IBinaryStream file)
        {
            var max_offset = file.Length;
            file.Position = 8;
            var key_gen = new KeyGenerator (0xDEADCAFE);
            var dir = new List<Entry>();
            while (file.PeekByte() != -1)
            {
                uint name_length = file.ReadUInt32() ^ key_gen.GetNext();
                if (!IsSaneCount (name_length, 1000))
                    break;
                var name_bytes   = file.ReadBytes ((int)name_length);
                var name         = DecryptName (name_bytes, key_gen);

                var entry = FormatCatalog.Instance.Create<RgssEntry>(name);
                entry.Size   = file.ReadUInt32() ^ key_gen.GetNext();
                entry.Offset = file.Position;
                entry.Key    = key_gen.Current;
                if (!entry.CheckPlacement (max_offset))
                    return null;
                dir.Add (entry);
                file.Seek (entry.Size, SeekOrigin.Current);
            }
            if (dir.Count == 0)
                return null;
            return dir;
        }

        List<Entry> ReadIndexV3 (IBinaryStream file)
        {
            var max_offset = file.Length;
            file.Position = 8;
            uint key = file.ReadUInt32() * 9 + 3;
            var dir = new List<Entry>();
            while (file.PeekByte() != -1)
            {
                uint offset = file.ReadUInt32() ^ key;
                if (0 == offset)
                    break;
                uint size        = file.ReadUInt32() ^ key;
                uint entry_key   = file.ReadUInt32() ^ key;
                uint name_length = file.ReadUInt32() ^ key;
                if (!IsSaneCount (name_length, 1000))
                    break;
                var name_bytes   = file.ReadBytes ((int)name_length);
                var name         = DecryptName (name_bytes, key);

                var entry = FormatCatalog.Instance.Create<RgssEntry>(name);
                entry.Offset = offset;
                entry.Size   = size;
                entry.Key    = entry_key;
                if (!entry.CheckPlacement (max_offset))
                    return null;

                dir.Add (entry);
            }
            if (dir.Count == 0)
                return null;
            return dir;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var rent = (RgssEntry)entry;
            var data = arc.File.View.ReadBytes (rent.Offset, rent.Size);
            XORDataWithKey (data, new KeyGenerator (rent.Key));
            return new BinMemoryStream (data);
        }

        public override void Create (
            Stream output, IEnumerable<Entry> list,
            ResourceOptions options, EntryCallback callback)
        {
            var rgss_options = GetOptions<RgssOptions>(options);
            byte version = rgss_options.Version;

            if (version < 1 || version > 3)
                version = 3;

            var encoding = Encoding.UTF8;
            var entries = list.ToArray();

            using (var writer = new BinaryWriter (output, encoding, true))
            {
                writer.Write (DefaultHeader);
                writer.Write ((byte)version);

                switch (version)
                {
                case 1:
                    WriteV1Archive (writer, entries, encoding, callback);
                    break;
                case 2:
                case 3:
                    WriteV3Archive (writer, entries, encoding, callback);
                    break;
                }
            }
        }

        void WriteV1Archive (BinaryWriter output, Entry[] entries, Encoding encoding, EntryCallback callback)
        {
            var key_gen = new KeyGenerator (0xDEADCAFE);
            var output_dir = Path.GetDirectoryName (Path.GetFullPath (entries[0].Name)) ?? "";

            //int i = 0;
            foreach (var entry in entries)
            {
                //if (null != callback)
                //callback (i++, entry, string.Foramt ("Adding file {0}/{1}", i, entries.Count));

                string relativePath = GetRelativePath (entry.Name, output_dir);
                var name_bytes      = encoding.GetBytes (relativePath);

                uint name_length_key = key_gen.GetNext();
                output.Write ((uint)name_bytes.Length ^ name_length_key);

                EncryptName (name_bytes, key_gen);
                output.Write (name_bytes);

                using (var input = File.OpenRead (entry.Name))
                {
                    uint file_size = (uint)input.Length;
                    uint size_key = key_gen.GetNext();
                    output.Write (file_size ^ size_key);
                    EncryptAndCopyStream (input, output, key_gen.Current);
                }
            }
        }

        void WriteV3Archive (BinaryWriter output, Entry[] entries, Encoding encoding, EntryCallback callback)
        {
            uint base_key = 0x55555555; // NOTE: This produces 0 key
            output.Write (base_key);
            uint key = base_key * 9 + 3;

            long index_size = 0;
            var entryInfo = new List<(Entry entry, string path, uint entryKey)>();

            var output_dir = Environment.CurrentDirectory;
            foreach (var entry in entries)
            {
                index_size += 16; // offset + size + entry_key + name_length
                string relativePath = GetRelativePath (entry.Name, output_dir);
                uint entryKey = 0;
                entryInfo.Add ((entry, relativePath, entryKey));
                index_size += encoding.GetByteCount (relativePath);
            }
            index_size += 4; // for terminating zero

            long data_offset = DefaultHeader.Length + 1 + 4 + index_size;

            // Write index
            foreach (var info in entryInfo)
            {
                var fileInfo = new FileInfo (info.entry.Name);
                uint file_size = (uint)fileInfo.Length;

                output.Write ((uint)data_offset ^ key);
                output.Write (file_size ^ key);
                output.Write (info.entryKey ^ key);

                var name_bytes = encoding.GetBytes (info.path);
                output.Write ((uint)name_bytes.Length ^ key);
                EncryptName (name_bytes, key);
                output.Write (name_bytes);

                data_offset += file_size;
            }

            // Write terminator
            output.Write (0u);

            //int i = 0;
            // Write file data
            foreach (var info in entryInfo)
            {
                //if (null != callback)
                //  callback (i++, entry, string.Foramt ("Adding file {0}/{1}", i, entries.Count));

                using (var input = File.OpenRead (info.entry.Name))
                {
                    EncryptAndCopyStream (input, output, info.entryKey);
                }
            }
        }

        private string GetRelativePath (string fullPath, string basePath)
        {
            // converts full path into a relative one in the dir we choose to pack
            string relativePath = fullPath;
            if (relativePath.StartsWith (basePath))
            {
                relativePath = relativePath.Substring (basePath.Length);
                if (relativePath.StartsWith (@"\"))
                    relativePath = relativePath.Substring (1);
                var pos = relativePath.IndexOf (@"\");
                if (pos != -1)
                    relativePath = relativePath.Substring (pos+1);
            }

            return relativePath;//.Replace(@"\", "/");
        }

        void EncryptAndCopyStream (Stream input, BinaryWriter output, uint data_key)
        {
            var buffer = new byte[input.Length];
            var key_gen = new KeyGenerator (data_key);
            int bytes_read;

            while ((bytes_read = input.Read (buffer, 0, buffer.Length)) > 0)
            {
                var data = new byte[bytes_read];
                Array.Copy (buffer, data, bytes_read);
                XORDataWithKey (data, key_gen);
                output.Write (data);
            }
        }

        void XORDataWithKey (byte[] data, KeyGenerator key_gen, int position = 0)
        {
            uint key = key_gen.GetNext();
            for (int i = 0; i < data.Length;)
            {
                data[i] ^= (byte)(key >> (i << 3));
                ++i;
                if (0 == (i & 3))
                {
                    key = key_gen.GetNext();
                }
            }
        }

        string DecryptName (byte[] name, KeyGenerator key_gen)
        {
            EncryptName (name, key_gen);
            return Encoding.UTF8.GetString (name);
        }

        string DecryptName (byte[] name, uint key)
        {
            EncryptName (name, key);
            return Encoding.UTF8.GetString (name);
        }

        void EncryptName (byte[] name, KeyGenerator key_gen)
        {
            for (int i = 0; i < name.Length; ++i)
            {
                name[i] ^= (byte)key_gen.GetNext();
            }
        }

        void EncryptName (byte[] name, uint key)
        {
            for (int i = 0; i < name.Length; ++i)
            {
                name[i] ^= (byte)(key >> (i << 3));
            }
        }

        public override ResourceOptions GetDefaultOptions()
        {
            return new RgssOptions 
            { 
                Version = Properties.Settings.Default.RGSSVersion
            };
        }

        public override object GetCreationWidget ()
        {
            return new GUI.CreateRGSSWidget ();
        }

        internal static readonly byte[] DefaultHeader = { 0x52, 0x47, 0x53, 0x53, 0x41, 0x44, 0x00 };
    }

    internal class RgssEntry : Entry
    {
        public uint Key;
    }

    internal class KeyGenerator
    {
        uint   m_seed;

        public KeyGenerator (uint seed) { m_seed = seed; }
        public uint Current { get { return m_seed; } }
        public uint GetNext ()
        {
            uint key = m_seed;
            m_seed = m_seed * 7 + 3;
            return key;
        }
    }

    public class RgssOptions : ResourceOptions, IExtensionProvider
    {
        public byte    Version { get; set; }

        public string GetExtension()
        {
            switch (Version)
            {
            case 1: return "rgssad";
            case 2: return "rgss2a";
            case 3:
            default:
                return "rgss3a";
            }
        }
    }
}