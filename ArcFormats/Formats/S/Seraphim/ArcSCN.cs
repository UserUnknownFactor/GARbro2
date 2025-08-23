using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Seraphim
{
    [Export(typeof(ArchiveFormat))]
    public class ScnOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SERAPH/SCN"; } }
        public override string Description { get { return "Seraphim engine scripts archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public ScnOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!VFS.IsPathEqualsToFileName (file.Name, "SCNPAC.DAT"))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_size = 4 * (uint)count;
            if (index_size > file.View.Reserve (4, index_size))
                return null;

            int index_offset = 4;
            uint next_offset = file.View.ReadUInt32 (index_offset);
            if (next_offset < index_offset + index_size)
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 4;
                var entry = new Entry { Name = i.ToString ("D5"), Type = "script" };
                entry.Offset = next_offset;
                next_offset = file.View.ReadUInt32 (index_offset);
                if (next_offset < entry.Offset || next_offset > file.MaxOffset)
                    return null;
                entry.Size = next_offset - (uint)entry.Offset;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0 == entry.Size)
                return Stream.Null;
            uint signature = arc.File.View.ReadUInt32 (entry.Offset);
            IBinaryStream input;
            if (1 == signature && 0x78 == arc.File.View.ReadByte (entry.Offset+4))
            {
                input = arc.File.CreateStream (entry.Offset+4, entry.Size-4);
                return new ZLibStream (input.AsStream, CompressionMode.Decompress);
                /*
                using (var compr = new ZLibStream (input.AsStream, CompressionMode.Decompress))
                using (var bin = new BinaryStream (compr, entry.Name))
                {
                    var data = LzDecompress (bin);
                    return new BinMemoryStream (data, entry.Name);
                }
                */
            }
            input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (signature < 4 || 0 != (signature & 0xFF000000))
            {
                if (0x78 == (signature & 0xFF))
                {
                    var compr = new ZLibStream (input.AsStream, CompressionMode.Decompress);
                    input = new BinaryStream (compr, entry.Name);
                }
                else
                    return input.AsStream;
            }
            try
            {
                var data = LzDecompress (input);
                return new BinMemoryStream (data, entry.Name);
            }
            catch
            {
                return arc.File.CreateStream (entry.Offset, entry.Size);
            }
            finally
            {
                input.Dispose();
            }
        }

        internal static byte[] LzDecompress (IBinaryStream input)
        {
            int unpacked_size = input.ReadInt32();
            var data = new byte[unpacked_size];
            int dst = 0;
            while (dst < unpacked_size)
            {
                int ctl = input.ReadByte();
                if (-1 == ctl)
                    throw new EndOfStreamException();
                if (0 != (ctl & 0x80))
                {
                    byte lo = input.ReadUInt8();
                    int offset = ((ctl << 3 | lo >> 5) & 0x3FF) + 1;
                    int count = (lo & 0x1F) + 1;
                    Binary.CopyOverlapped (data, dst-offset, dst, count);
                    dst += count;
                }
                else
                {
                    int count = ctl + 1;
                    if (input.Read (data, dst, count) != count)
                        throw new EndOfStreamException();
                    dst += count;
                }
            }
            return data;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class Scn95Opener : ArchiveFormat
    {
        public override string         Tag { get { return "SCN/ARCH"; } }
        public override string Description { get { return "Archangel engine scripts archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Scn95Opener ()
        {
            Extensions = new[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!VFS.IsPathEqualsToFileName (file.Name, "SCNPAC.DAT"))
                return null;
            uint offset = file.View.ReadUInt32 (0);
            int count = (int)offset / 4;
            if (offset >= file.MaxOffset || !IsSaneCount (count))
                return null;

            int index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint size = file.View.ReadUInt32 (index_offset);
                if (0 == size)
                    return null;
                var entry = new Entry {
                    Name = i.ToString ("D5"),
                    Type = "script",
                    Offset = offset + 4,
                    Size = size,
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                offset += size;
                index_offset += 4;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            IBinaryStream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (input.Signature < 4 || 0 != (input.Signature & 0xFF000000))
            {
                return input.AsStream;
            }
            try
            {
                var data = ScnOpener.LzDecompress (input);
                return new BinMemoryStream (data, entry.Name);
            }
            catch
            {
                return arc.File.CreateStream (entry.Offset, entry.Size);
            }
            finally
            {
                input.Dispose();
            }
        }
    }
}
