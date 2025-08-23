using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Silky
{
    [Export(typeof(ArchiveFormat))]
    public class Ai6Opener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/AI6WIN"; } }
        public override string Description { get { return "AI6WIN engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public Ai6Opener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            long index_offset = 4;
            uint index_size = (uint)(count * (0x104 + 12));
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            var name_buffer = new byte[0x104];
            var dir = new List<Entry>();
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (index_offset, name_buffer, 0, (uint)name_buffer.Length);
                int name_length = Array.IndexOf<byte> (name_buffer, 0);
                if (0 == name_length)
                    return null;
                if (-1 == name_length)
                    name_length = name_buffer.Length;
                byte key = (byte)(name_length+1);
                for (int j = 0; j < name_length; ++j)
                {
                    name_buffer[j] -= key--;
                    char c = (char)name_buffer[j];
                    if (VFS.InvalidFileNameChars.Contains (c) && c != '/')
                        return null;
                }
                var name = Encodings.cp932.GetString (name_buffer, 0, name_length);
                index_offset += 0x104;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Size          = Binary.BigEndian (file.View.ReadUInt32 (index_offset));
                entry.UnpackedSize  = Binary.BigEndian (file.View.ReadUInt32 (index_offset+4));
                entry.Offset        = Binary.BigEndian (file.View.ReadUInt32 (index_offset+8));
                if (entry.Offset < index_size+4 || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = entry.Size != entry.UnpackedSize;
                dir.Add (entry);
                index_offset += 12;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new LzssStream (input);
        }
    }
}
