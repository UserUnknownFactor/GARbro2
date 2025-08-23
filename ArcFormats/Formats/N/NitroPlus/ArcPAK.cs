using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Magi
{
    // this format is very similar to NitroPlus.PakOpener v3, but has no encryption.
    //
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/MAGI"; } }
        public override string Description { get { return "MAGI resource archive"; } }
        public override uint     Signature { get { return 3; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            Signatures = new uint[] { 3, 4 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadInt32 (0);
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            uint index_size = file.View.ReadUInt32 (0xC);
            if (index_size < 2 || index_size > file.MaxOffset)
                return null;

            long base_offset = 0x118 + index_size;

            try // this format is too generic and is causing premature throws below
            {
                using (var mem = file.CreateStream (0x118, index_size))
                using (var z = new ZLibStream (mem, CompressionMode.Decompress))
                using (var index = new BinaryStream (z, file.Name))
                {
                    var dir = new List<Entry>(count);
                    string cur_dir = "";

                    for (int i = 0; i < count; ++i)
                    {
                        int name_length = index.ReadInt32();
                        if (name_length <= 0)
                            return null;
                        var name = index.ReadCString (name_length);
                        if (version > 3)
                        {
                            bool is_dir = index.ReadInt32() != 0;
                            if (is_dir)
                            {
                                cur_dir = name;
                                index.ReadInt64();
                                index.ReadInt32();
                                index.ReadInt64();
                                continue;
                            }
                            if (cur_dir.Length > 0)
                                name = Path.Combine (cur_dir, name);
                        }
                        var entry = Create<PackedEntry> (name);
                        entry.Offset        = index.ReadUInt32() + base_offset;
                        entry.UnpackedSize  = index.ReadUInt32();
                        index.ReadUInt32();
                        uint is_packed      = index.ReadUInt32();
                        uint packed_size    = index.ReadUInt32();
                        entry.IsPacked      = is_packed != 0 && packed_size > 0;
                        if (entry.IsPacked)
                            entry.Size = packed_size;
                        else
                            entry.Size = entry.UnpackedSize;

                        if (!entry.CheckPlacement (file.MaxOffset))
                            return null;
                        dir.Add(entry);
                    }
                    return new ArcFile (file, this, dir);
                }
            }
            catch (InvalidDataException)
            {
                return null;
            }
            catch (EndOfStreamException)
            {
                return null;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size, entry.Name);
            var pentry = entry as PackedEntry;
            if (null != pentry && pentry.IsPacked)
                input = new ZLibStream (input, CompressionMode.Decompress);
            return input;
        }
    }
}
