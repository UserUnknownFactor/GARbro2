using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Liar
{
    [Export(typeof(ArchiveFormat))]
    public class XflOpener : ArchiveFormat
    {
        public override string         Tag { get { return "XFL"; } }
        public override string Description { get { return  Localization._T ("XFLDescription"); } }
        public override uint     Signature { get { return  0x0001424c; } }
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  true; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var dir = ReadDirectory (file, 0, file.MaxOffset, "");
            return (dir != null) ? new ArcFile (file, this, dir) : null;
        }

        internal List<Entry> ReadDirectory (ArcView file, long base_offset, long max_offset, string base_dir)
        {
            uint dir_size = file.View.ReadUInt32 (base_offset+4);
            int count     = file.View.ReadInt32  (base_offset+8);
            if (!IsSaneCount (count))
                return null;

            long data_offset = base_offset + dir_size + 12;
            if (dir_size >= max_offset || data_offset >= max_offset)
                return null;

            file.View.Reserve (base_offset, (uint)(data_offset - base_offset));
            long cur_offset = base_offset + 12;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (cur_offset+40 > data_offset)
                    return null;

                string name = file.View.ReadString (cur_offset, 32);
                var entry_offset = data_offset + file.View.ReadUInt32 (cur_offset+32);
                var entry_size   = file.View.ReadUInt32 (cur_offset+36);
                List<Entry> subdir = null;
                name = VFS.CombinePath (base_dir, name);
                if (name.HasExtension (".xfl") && file.View.ReadUInt32 (entry_offset) == Signature)
                    subdir = ReadDirectory (file, entry_offset, entry_offset + entry_size, name);
                if (subdir != null && subdir.Count > 0)
                    dir.AddRange (subdir);
                else
                {

                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = entry_offset;
                    entry.Size = entry_size;
                    if (!entry.CheckPlacement (max_offset))
                        return null;
                    dir.Add (entry);
                }
                cur_offset += 40;
            }
            return dir;
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            var entries = list.ToList();
            int file_count = entries.Count;
            if (file_count > 0xFFFF)
                throw new InvalidFormatException ("Too many files for XFL archive");

            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                writer.Write (Signature);
                int list_size = list.Count();
                uint dir_size = (uint)(list_size * 40);
                writer.Write (dir_size);
                writer.Write (list_size);

                var encoding = Encodings.cp932.WithFatalFallback();

                byte[] name_buf = new byte[32];
                int callback_count = 0;

                if (null != callback)
                    callback (callback_count++, null, Localization._T ("MsgWritingIndex"));

                // first, write names only
                foreach (var entry in entries)
                {
                    string name = Path.GetFileName (entry.Name);
                    try
                    {
                        int size = encoding.GetBytes (name, 0, name.Length, name_buf, 0);
                        if (size < name_buf.Length)
                            name_buf[size] = 0;
                    }
                    catch (EncoderFallbackException X)
                    {
                        throw new InvalidFileName (entry.Name, Localization._T ("MsgIllegalCharacters"), X);
                    }
                    catch (ArgumentException X)
                    {
                        throw new InvalidFileName (entry.Name, Localization._T ("MsgFileNameTooLong"), X);
                    }
                    writer.Write (name_buf);
                    writer.BaseStream.Seek (8, SeekOrigin.Current);
                }

                // now, write files and remember offset/sizes
                uint current_offset = 0;
                foreach (var entry in list)
                {
                    if (null != callback)
                        callback (callback_count++, entry, Localization._T ("MsgAddingFile"));

                    entry.Offset = current_offset;
                    using (var input = File.Open (entry.Name, FileMode.Open, FileAccess.Read))
                    {
                        var size = input.Length;
                        if (size > uint.MaxValue || current_offset + size > uint.MaxValue)
                            throw new FileSizeException();
                        current_offset += (uint)size;
                        entry.Size = (uint)size;
                        input.CopyTo (output);
                    }
                }

                if (null != callback)
                    callback (callback_count++, null, Localization._T ("MsgUpdatingIndex"));

                // at last, go back to directory and write offset/sizes
                long dir_offset = 12+32;
                foreach (var entry in list)
                {
                    writer.BaseStream.Position = dir_offset;
                    writer.Write ((uint)entry.Offset);
                    writer.Write (entry.Size);
                    dir_offset += 40;
                }
            }
        }
    }
}
