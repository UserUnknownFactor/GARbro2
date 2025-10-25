using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;

namespace GameRes.Formats.Ucom
{
    [Export(typeof(ArchiveFormat))]
    public class DataOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DATA/UC"; } }
        public override string Description { get { return "For/Ucom scripts archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "PF"))
                return null;
            var base_name = Path.GetFileName (file.Name);
            if (!base_name.Equals ("data02", StringComparison.InvariantCultureIgnoreCase))
                return null;

            var index_name = VFS.CombinePath (VFS.GetDirectoryName (file.Name), "data01");
            if (!VFS.FileExists (index_name))
                return null;
            using (var index = VFS.OpenView (index_name))
            {
                if (!index.View.AsciiEqual (0, "IF"))
                    return null;

                int count = index.View.ReadInt16 (2);
                if (!IsSaneCount (count) || 4 + 0x18 * count > index.MaxOffset)
                    return null;

                uint index_offset = 4;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.View.ReadString (index_offset, 0x10);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = index.View.ReadUInt32 (index_offset+0x10);
                    entry.Size   = index.View.ReadUInt32 (index_offset+0x14);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_offset += 0x18;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            for (int i = 0; i < data.Length; ++i)
            {
                if ((i % 5) != 0)
                    data[i] ^= 0x45;
            }
            return new BinMemoryStream (data, entry.Name);
        }

        public override void Create (
            Stream output, IEnumerable<Entry> list, ResourceOptions options,
            EntryCallback callback)
        {
            var dir_name   = Directory.GetCurrentDirectory();
            var index_name = Path.Combine (dir_name, "Data01");

            var entries = list.ToList();
            if (entries.Count > 16383)
                throw new InvalidFormatException ("Too many entries");

            int callback_count = 0;

            // Write data02 file (data archive)
            using (var data_writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                data_writer.Write (Encoding.ASCII.GetBytes ("PF"));

                uint current_offset = 2;

                var index_entries = new List<IndexEntry>();
                foreach (var entry in entries)
                {
                    if (null != callback)
                        callback (callback_count++, entry, Localization._T("MsgAddingFile"));

                    var file_name = Path.GetFileName (entry.Name);
                    if (file_name.Length > 0x10)
                        throw new InvalidFormatException ($"File name '{file_name}' is too long (max 16 characters)");

                    using (var input = File.OpenRead (entry.Name))
                    {
                        var file_data = new byte[input.Length];
                        input.Read (file_data, 0, file_data.Length);

                        for (int i = 0; i < file_data.Length; ++i)
                        {
                            if ((i % 5) != 0)
                                file_data[i] ^= 0x45;
                        }

                        data_writer.Write (file_data);

                        index_entries.Add (new IndexEntry
                        {
                            Name = file_name,
                            Offset = current_offset,
                            Size = (uint)file_data.Length
                        });

                        current_offset += (uint)file_data.Length;
                    }
                }

                // Write data01 file (index)
                using (var index_stream = File.Create (index_name))
                using (var index_writer = new BinaryWriter (index_stream, Encoding.ASCII))
                {
                    index_writer.Write (Encoding.ASCII.GetBytes ("IF"));
                    index_writer.Write ((ushort)index_entries.Count);

                    foreach (var idx_entry in index_entries)
                    {
                        // Write name (fixed 16 bytes, zero-padded)
                        var name_bytes = new byte[0x10];
                        var name_data = Encodings.cp932.GetBytes (idx_entry.Name);
                        Array.Copy (name_data, name_bytes, Math.Min (name_data.Length, 0x10));
                        index_writer.Write (name_bytes);

                        index_writer.Write (idx_entry.Offset);
                        index_writer.Write (idx_entry.Size);
                    }
                }
            }
        }

        private class IndexEntry
        {
            public string Name;
            public uint Offset;
            public uint Size;
        }
    }
}
