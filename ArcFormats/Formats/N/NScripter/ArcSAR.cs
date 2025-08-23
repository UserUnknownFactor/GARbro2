using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.NScripter
{
    [Export(typeof(ArchiveFormat))]
    public class SarOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SAR"; } }
        public override string Description { get { return Localization._T ("NSADescription"); } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return true; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int num_of_files = Binary.BigEndian (file.View.ReadInt16 (0));
            if (num_of_files <= 0)
                return null;
            uint base_offset = Binary.BigEndian (file.View.ReadUInt32 (2));
            if (base_offset >= file.MaxOffset || base_offset < 10 * (uint)num_of_files)
                return null;

            uint cur_offset = 6;
            var dir = new List<Entry>();
            for (int i = 0; i < num_of_files; ++i)
            {
                if (base_offset - cur_offset < 10)
                    return null;
                int name_len;
                byte[] name_buffer = ReadName (file, cur_offset, base_offset-cur_offset, out name_len);
                if (0 == name_len || base_offset-cur_offset == name_len)
                    return null;
                cur_offset += (uint)(name_len + 1);
                if (base_offset - cur_offset < 8)
                    return null;

                string name = Encodings.cp932.GetString (name_buffer, 0, name_len);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = Binary.BigEndian (file.View.ReadUInt32 (cur_offset)) + (long)base_offset;
                entry.Size   = Binary.BigEndian (file.View.ReadUInt32 (cur_offset+4));
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;

                cur_offset += 8;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            var encoding = Encodings.cp932.WithFatalFallback();
            int callback_count = 0;

            var real_entry_list = new List<Entry>();
            var used_names = new HashSet<string>();
            int index_size = 0;
            foreach (var entry in list)
            {
                if (!used_names.Add (entry.Name)) // duplicate name
                    continue;
                try
                {
                    index_size += encoding.GetByteCount (entry.Name) + 1;
                }
                catch (EncoderFallbackException X)
                {
                    throw new InvalidFileName (entry.Name, Localization._T ("MsgIllegalCharacters"), X);
                }
                index_size += 8;
                real_entry_list.Add (entry);
            }

            long start_offset = output.Position;
            long base_offset = 6+index_size;
            output.Seek (base_offset, SeekOrigin.Current);
            foreach (var entry in real_entry_list)
            {
                using (var input = File.OpenRead (entry.Name))
                {
                    var file_size = input.Length;
                    if (file_size > uint.MaxValue)
                        throw new FileSizeException();
                    long file_offset = output.Position - base_offset;
                    if (file_offset+file_size > uint.MaxValue)
                        throw new FileSizeException();
                    entry.Offset = file_offset;
                    entry.Size   = (uint)file_size;
                    if (null != callback)
                        callback (callback_count++, entry, Localization._T ("MsgAddingFile"));

                    input.CopyTo (output);
                }
            }

            if (null != callback)
                callback (callback_count++, null, Localization._T ("MsgWritingIndex"));
            output.Position = start_offset;
            using (var writer = new BinaryWriter (output, encoding, true))
            {
                writer.Write (Binary.BigEndian ((short)real_entry_list.Count));
                writer.Write (Binary.BigEndian ((uint)base_offset));
                foreach (var entry in real_entry_list)
                {
                    writer.Write (encoding.GetBytes (entry.Name));
                    writer.Write ((byte)0);
                    writer.Write (Binary.BigEndian ((uint)entry.Offset));
                    writer.Write (Binary.BigEndian ((uint)entry.Size));
                }
            }
        }

        protected static byte[] ReadName (ArcView file, uint offset, uint limit, out int name_len)
        {
            byte[] name_buffer = new byte[40];
            for (name_len = 0; name_len < limit; ++name_len)
            {
                byte b = file.View.ReadByte (offset+name_len);
                if (0 == b)
                    break;
                if (name_buffer.Length == name_len)
                {
                    Array.Resize (ref name_buffer, checked(name_len/2*3));
                }
                name_buffer[name_len] = b;
            }
            return name_buffer;
        }
    }
}
