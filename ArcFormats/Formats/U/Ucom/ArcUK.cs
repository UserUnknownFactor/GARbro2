using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;

namespace GameRes.Formats.Ucom
{
    [Export(typeof(ArchiveFormat))]
    public class UkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "RES/UC"; } }
        public override string Description { get { return "For/Ucom resource archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  false; } }

        public UkOpener ()
        {
            Extensions = new string[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "UK"))
                return null;
            int count = file.View.ReadInt16 (2);
            if (!IsSaneCount (count) || 4 + 0x18 * count >= file.MaxOffset)
                return null;

            uint index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x10);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x14);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            return new ArcFile (file, this, dir);
        }

        public override void Create (
            Stream output, IEnumerable<Entry> list, ResourceOptions options,
            EntryCallback callback)
        {
            var entries = list.ToList();
            if (entries.Count > 16383)
                throw new InvalidFormatException ("Too many entries");

            int callback_count = 0;

            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                writer.Write (Encoding.ASCII.GetBytes ("UK"));
                writer.Write ((ushort)entries.Count);

                uint data_offset = (uint)(4 + 0x18 * entries.Count);
                var index_entries = new List<IndexEntry>();
                uint current_offset = data_offset;

                foreach (var entry in entries)
                {
                    var file_name = Path.GetFileName (entry.Name);
                    if (file_name.Length > 0x10)
                        throw new InvalidFormatException ($"File name '{file_name}' is too long (max 16 characters)");

                    var file_info = new FileInfo (entry.Name);
                    if (!file_info.Exists)
                        throw new FileNotFoundException ("File not found", entry.Name);

                    index_entries.Add (new IndexEntry
                    {
                        Name = file_name,
                        Offset = current_offset,
                        Size = (uint)file_info.Length
                    });

                    current_offset += (uint)file_info.Length;
                }

                // Write index
                foreach (var idx_entry in index_entries)
                {
                    // Write name (16 bytes fixed, zero-padded)
                    var name_bytes = new byte[0x10];
                    var name_data = Encodings.cp932.GetBytes (idx_entry.Name);
                    Array.Copy (name_data, name_bytes, Math.Min (name_data.Length, 0x10));
                    writer.Write (name_bytes);

                    writer.Write (idx_entry.Offset);
                    writer.Write (idx_entry.Size);
                }

                foreach (var entry in entries)
                {
                    if (null != callback)
                        callback (callback_count++, entry,  Localization._T("MsgAddingFile"));

                    using (var input = File.OpenRead (entry.Name))
                    {
                        input.CopyTo (output);
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
