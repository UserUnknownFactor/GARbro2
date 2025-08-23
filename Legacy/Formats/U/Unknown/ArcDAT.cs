using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Utility;

// [010629][Unknown] No Reality

namespace GameRes.Formats.Unknown
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/UNKNOWN"; } }
        public override string Description { get { return "'Unknown' resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            int entry_size = file.View.ReadInt32 (4);
            int data_offset = file.View.ReadInt32 (8);
            int index_size = count * entry_size;
            if (!IsSaneCount (count) || entry_size == 0 || entry_size > 0x10
                || index_size + 12 != data_offset)
                return null;
            var index = file.View.ReadBytes (12, (uint)index_size);
            Decrypt (index);
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int id = index.ToInt32 (index_offset);
                var entry = new Entry {
                    Name   = string.Format ("{0}#{1:D4}", base_name, id),
                    Size   = index.ToUInt32 (index_offset+4),
                    Offset = index.ToUInt32 (index_offset+8),
                };
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += entry_size;
            }
            DetectFileTypes (file, dir);
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            Decrypt (data);
            return new BinMemoryStream (data, entry.Name);
        }

        internal void DetectFileTypes (ArcView file, IEnumerable<Entry> dir)
        {
            var buffer = new byte[4];
            foreach (var entry in dir.Where (e => e.Size > 4))
            {
                file.View.Read (entry.Offset, buffer, 0, 4);
                Decrypt (buffer);
                uint signature = buffer.ToUInt32 (0);
                var res = AutoEntry.DetectFileType (signature);
                entry.ChangeType (res);
            }
        }

        internal void Decrypt (byte[] data)
        {
            for (int i = 0; i < data.Length; ++i)
                data[i] = Binary.RotByteR (data[i], 4);
        }
    }
}
