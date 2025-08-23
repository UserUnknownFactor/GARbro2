using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace GameRes.Formats.MyAdv
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/MyAdv"; } }
        public override string Description { get { return "MyAdv engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PacOpener ()
        {
            ContainedFormats = new[] { "DDS", "OGG", "TXT" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;

            var name_buffer = new byte[0x100];
            var zlib_buffer = new byte[0x100];
            long pos = 4;
            var zlib = new Inflater();
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_len = file.View.ReadInt32 (pos);
                if (name_len <= 0 || name_len > name_buffer.Length)
                    return null;
                int unpacked_size = file.View.ReadInt32 (pos+4);
                if (unpacked_size < name_len || unpacked_size > name_buffer.Length)
                    return null;
                int packed_size = file.View.ReadInt32 (pos+8);
                if (packed_size <= 0 || packed_size > zlib_buffer.Length)
                    return null;
                pos += 12;
                file.View.Read (pos, zlib_buffer, 0, (uint)packed_size);
                pos += packed_size;
                zlib.Reset();
                zlib.SetInput (zlib_buffer, 0, packed_size);
                zlib.Inflate (name_buffer);
                var name = Encodings.cp932.GetString (name_buffer, 0, name_len);
                var entry = Create<PackedEntry> (name);
                dir.Add (entry);
            }
            foreach (PackedEntry entry in dir)
            {
                entry.Offset       = file.View.ReadUInt32 (pos);
                entry.Size         = file.View.ReadUInt32 (pos+4);
                entry.UnpackedSize = file.View.ReadUInt32 (pos+8);
                entry.IsPacked = true;
                pos += 12;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (null == pent || !pent.IsPacked)
                return input;
            return new ZLibStream (input, CompressionMode.Decompress);
        }
    }
}
