using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Ego
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/EGO/1"; } }
        public override string Description { get { return "Studio e.go! engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return true; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint data_offset = 4 + file.View.ReadUInt32 (0);
            if (data_offset <= 0x14 || data_offset >= file.MaxOffset)
                return null;
            var dir = new List<Entry>();
            long index_offset = 4;
            while (index_offset < data_offset)
            {
                uint entry_len = file.View.ReadUInt32 (index_offset);
                if (entry_len <= 0x10 || entry_len > 0x100 || index_offset + entry_len > data_offset)
                    return null;
                var name = file.View.ReadString (index_offset+0x10, entry_len-0x10);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+8);
                entry.Size   = file.View.ReadUInt32 (index_offset+12);
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += entry_len;
            }
            return new ArcFile (file, this, dir);
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                var encoding = Encodings.cp932.WithFatalFallback();
                int callback_count = 0;
                if (null != callback)
                    callback (callback_count++, null, Localization._T ("MsgWritingIndex"));

                writer.Write (0);
                byte[] name_buf = new byte[256];
                uint index_size = 0;
                var entry_sizes = new List<int>();

                // first, write names only
                foreach (var entry in list)
                {
                    try
                    {
                        int size = encoding.GetBytes (entry.Name, 0, entry.Name.Length, name_buf, 0);
                        if (name_buf.Length == size)
                            throw new InvalidFileName (entry.Name, Localization._T ("MsgFileNameTooLong"));
                        name_buf[size] = 0;
                        int entry_size = size+17;
                        writer.Write (entry_size);
                        writer.BaseStream.Seek (12, SeekOrigin.Current);
                        writer.Write (name_buf, 0, size+1);
                        entry_sizes.Add (entry_size);
                        index_size += (uint)entry_size;
                    }
                    catch (EncoderFallbackException X)
                    {
                        throw new InvalidFileName (entry.Name, Localization._T ("MsgIllegalCharacters"), X);
                    }
                    catch (ArgumentException X)
                    {
                        throw new InvalidFileName (entry.Name, Localization._T ("MsgFileNameTooLong"), X);
                    }
                }

                // now, write files and remember offset/sizes
                long current_offset = writer.BaseStream.Position;
                foreach (var entry in list)
                {
                    if (null != callback)
                        callback (callback_count++, entry, Localization._T ("MsgAddingFile"));

                    entry.Offset = current_offset;
                    using (var input = File.OpenRead (entry.Name))
                    {
                        var file_size = input.Length;
                        if (file_size > uint.MaxValue || current_offset + file_size > uint.MaxValue)
                            throw new FileSizeException();
                        current_offset += file_size;
                        entry.Size = (uint)file_size;
                        input.CopyTo (output);
                    }
                }

                if (null != callback)
                    callback (callback_count++, null, Localization._T ("MsgUpdatingIndex"));

                // at last, go back to directory and write offset/sizes
                writer.BaseStream.Position = 0;
                writer.Write (index_size);
                long index_offset = 4+8;
                int i = 0;
                foreach (var entry in list)
                {
                    writer.BaseStream.Position = index_offset;
                    int entry_size = entry_sizes[i++];
                    index_offset += entry_size;
                    writer.Write ((uint)entry.Offset);
                    writer.Write (entry.Size);
                }
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class OldDatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/EGO/0"; } }
        public override string Description { get { return "Studio e.go! engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint data_offset = 4 + file.View.ReadUInt32 (0);
            if (data_offset <= 0x14 || data_offset >= file.MaxOffset)
                return null;
            var dir = new List<Entry>();
            long index_offset = 4;
            while (index_offset < data_offset)
            {
                uint entry_len = file.View.ReadUInt32 (index_offset);
                if (entry_len <= 0xC || entry_len > 0x100 || index_offset + entry_len > data_offset)
                    return null;
                var name = file.View.ReadString (index_offset+0xC, entry_len-0xC);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+4);
                entry.Size   = file.View.ReadUInt32 (index_offset+8);
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += entry_len;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
