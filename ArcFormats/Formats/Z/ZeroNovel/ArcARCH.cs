using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Compression;
using GameRes.Formats.Properties;
using GameRes.Utility;

namespace GameRes.Formats.ZeroNovel
{
    [Export(typeof(ArchiveFormat))]
    public class ArchFormat : ArchiveFormat
    {
        public override string         Tag { get { return "ARCH"; } }
        public override string Description { get { return "ZeroNovel archive"; } }
        public override uint     Signature { get { return  0x48435241; } }  // 'ARCH'

        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  false; } }

        private const int EntrySize         = 292;
        private const int MaxFileNameLength = 260;

        public ArchFormat()
        {
            Extensions = new[] { "arch" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var header = ArchHeader.Read (file);
            if (header == null || !header.IsValid (Signature))
                return null;

            uint indexSize = header.FileCount * EntrySize;
            if (!ValidateIndexBounds (header.IndexOffset, indexSize, file.MaxOffset))
                return null;

            var dir = ReadFileEntries (file, header);
            if (dir == null)
                return null;

            return new ArcFile (file, this, dir);
        }

        private List<Entry> ReadFileEntries (ArcView file, ArchHeader header)
        {
            var entries = new List<Entry>((int)header.FileCount);

            using (var index = file.CreateStream (header.IndexOffset, header.FileCount * EntrySize))
            {
                var buffer = new byte[EntrySize];

                for (uint i = 0; i < header.FileCount; ++i)
                {
                    if (index.Read (buffer, 0, buffer.Length) != buffer.Length)
                        return null;

                    var entry = ParseFileEntry (buffer);
                    if (entry == null || !entry.CheckPlacement (file.MaxOffset))
                        return null;

                    entries.Add (entry);
                }
            }

            return entries;
        }

        private PackedEntry ParseFileEntry (byte[] buffer)
        {
            const int mfl        = MaxFileNameLength;
            uint offset          = BitConverter.ToUInt32 (buffer, mfl);
            uint size            = BitConverter.ToUInt32 (buffer, mfl + 4);
            uint compressedSize  = BitConverter.ToUInt32 (buffer, mfl + 8);
            byte compressionType = buffer[mfl + 28];

            int nameLength = Array.IndexOf (buffer, (byte)0, 0, MaxFileNameLength);
            if (nameLength < 0) 
                nameLength = MaxFileNameLength;

            string name = Encoding.UTF8.GetString (buffer, 0, nameLength);

            return new PackedEntry
            {
                Name = name,
                Offset = offset,
                Size = compressionType == 1 ? compressedSize : size,
                UnpackedSize = size,
                IsPacked = compressionType == 1,
            };
        }

        private bool ValidateIndexBounds (uint indexOffset, uint indexSize, long maxOffset)
        {
            return indexOffset > 0 && indexOffset + (long)indexSize <= maxOffset;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var packedEntry = entry as PackedEntry;
            if (packedEntry == null || !packedEntry.IsPacked)
                return arc.File.CreateStream (entry.Offset, entry.Size);

            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new ZLibStream (input, CompressionMode.Decompress);
        }

        private class ArchHeader
        {
            public uint       Magic { get; private set; }
            public uint     Version { get; private set; }
            public uint   FileCount { get; private set; }
            public uint IndexOffset { get; private set; }
            public uint       Flags { get; private set; }
            public byte[]  Reserved { get; private set; }

            private const int Size = 64;

            private ArchHeader() { }

            public static ArchHeader Read (ArcView file)
            {
                if (file.MaxOffset < Size)
                    return null;

                using (var stream = file.CreateStream (0, Size))
                {
                    if (stream.Length < Size)
                        return null;

                    return new ArchHeader
                    {
                        Magic       = stream.ReadUInt32(),
                        Version     = stream.ReadUInt32(),
                        FileCount   = stream.ReadUInt32(),
                        IndexOffset = stream.ReadUInt32(),
                        Flags       = stream.ReadUInt32(),
                        Reserved    = stream.ReadBytes (44)
                    };
                }
            }

            public bool IsValid (uint expectedMagic)
            {
                return Magic == expectedMagic && FileCount > 0;
            }
        }
    }
}