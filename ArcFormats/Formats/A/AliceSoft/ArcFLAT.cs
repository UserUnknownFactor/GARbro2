using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Compression;

namespace GameRes.Formats.AliceSoft
{
    internal class FlatSection
    {
        public bool Present { get; set; }
        public  uint Offset { get; set; }
        public    uint Size { get; set; }
    }

    internal class FlatEntry : Entry
    {
        public FlatDataType DataType { get; set; }
        public     uint UnpackedSize { get; set; }
        public      bool HasFrontPad { get; set; }
        public         uint FrontPad { get; set; }
    }

    internal enum FlatDataType
    {
        Unknown = 0,
        CG      = 2,
        ZLib    = 5
    }

    [Export(typeof(ArchiveFormat))]
    public class FlatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "FLAT"; } }
        public override string Description { get { return "AliceSoft texture archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  false; } }

        public FlatOpener()
        {
            Extensions = new string[] { "flat" };
            ContainedFormats  = new[] { "AJP", "PMS", "QNT", "PNG", "TGA" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset < 8)
                return null;

            var sections = new Dictionary<string, FlatSection> ();
            uint pos = 0;

            // Read sections in order
            while (pos + 8 <= file.MaxOffset)
            {
                var magic = Encoding.ASCII.GetString (file.View.ReadBytes (pos, 4));

                if (magic != "ELNA" && magic != "FLAT" && magic != "TMNL" &&
                    magic != "MTLC" && magic != "LIBL" && magic != "TALT")
                    break;

                uint size = file.View.ReadUInt32 (pos + 4);

                sections[magic] = new FlatSection {
                    Present = true,
                    Offset = pos,
                    Size = size
                };

                pos += 8 + size;
            }

            if (!sections.ContainsKey ("FLAT") ||
                !sections.ContainsKey ("MTLC") ||
                !sections.ContainsKey ("LIBL"))
                return null;

            var dir = new List<Entry> ();

            if (sections.TryGetValue ("LIBL", out var libl))
            {
                var libl_entries = ParseLiblSection (file, libl);
                if (libl_entries != null)
                    dir.AddRange (libl_entries);
            }

            if (sections.TryGetValue ("TALT", out var talt))
            {
                var talt_entries = ParseTaltSection (file, talt, dir.Count);
                if (talt_entries != null)
                    dir.AddRange (talt_entries);
            }

            if (dir.Count == 0)
                return null;

            return new ArcFile (file, this, dir);
        }

        private List<Entry> ParseLiblSection (ArcView file, FlatSection libl)
        {
            var dir = new List<Entry> ();
            uint pos = libl.Offset + 8;
            long end = file.MaxOffset;

            if (pos + 4 > end)
                return null;

            uint count = file.View.ReadUInt32 (pos);
            if (!IsSaneCount (count, 1000))
                return null;

            pos += 4;

            for (uint i = 0; i < count; i++)
            {
                if (pos + 4 > end)
                    break;

                uint unknown_size = file.View.ReadUInt32 (pos);
                pos += 4;
                pos += unknown_size;

                pos = (pos + 3) & ~3u;

                if (pos + 8 > end)
                    break;

                var data_type = (FlatDataType)file.View.ReadUInt32 (pos);
                uint size = file.View.ReadUInt32 (pos + 4);
                pos += 8;

                uint offset = pos;
                if (offset + size > end)
                    break;

                Entry entry;
                if (data_type == FlatDataType.ZLib && size > 4)
                {
                    uint unpacked_size = file.View.ReadUInt32(offset);

                    if (file.View.ReadByte(offset + 4) == 0x78)
                    {
                        var packed_entry = new PackedEntry {
                            Name         = string.Format ("LIBL_{0:D3}.dat", i),
                            Type         = "",
                            Offset       = offset + 4,
                            Size         = size - 4,
                            UnpackedSize = unpacked_size,
                            IsPacked     = true
                        };
                        entry = packed_entry;
                    }
                    else
                    {
                        var flat_entry = new FlatEntry {
                            Name       = string.Format ("LIBL_{0:D3}.dat", i),
                            Type       = "",
                            Offset     = offset,
                            Size       = size,
                            DataType   = data_type
                        };
                        entry = flat_entry;
                    }
                }
                else
                {
                    var flat_entry = new FlatEntry {
                        Name       = "",
                        Type       = "",
                        Offset     = offset,
                        Size       = size,
                        DataType   = data_type
                    };

                    if (data_type == FlatDataType.CG && size > 4 && !IsImageSignature (file, offset))
                    {
                        if (IsImageSignature (file, offset + 4))
                        {
                            flat_entry.HasFrontPad = true;
                            flat_entry.FrontPad    = file.View.ReadUInt32 (offset);
                            flat_entry.Offset     += 4;
                            flat_entry.Size       -= 4;
                        }
                    }
                    var (type, ext) = GetExtension(file, flat_entry.Offset, data_type);
                    flat_entry.Name = string.Format("LIBL_{0:D3}{1}", i, ext);
                    flat_entry.Type = type;
                    entry = flat_entry;
                }

                pos += size;
                pos = (pos + 3) & ~3u;

                dir.Add (entry);
            }
            return dir;
        }
        private List<Entry> ParseTaltSection(ArcView file, FlatSection talt, int base_index)
        {
            var dir = new List<Entry>();
            uint pos = talt.Offset + 8;
            uint end = talt.Offset + 8 + talt.Size;

            if (pos + 4 > file.MaxOffset)
                return null;

            uint count = file.View.ReadUInt32(pos);
            if (!IsSaneCount(count, 1000))
                return null;

            pos += 4;

            for (uint i = 0; i < count; i++)
            {
                if (pos + 8 > end)
                    break;

                uint size = file.View.ReadUInt32(pos);
                pos += 4;
                uint offset = pos;

                var entry = new FlatEntry {
                    Name = string.Format("TALT_{0:D3}.ajp", base_index + i),
                    Type = "image",
                    Offset = offset,
                    Size = size,
                    DataType = FlatDataType.CG
                };

                pos += size;
                pos = (pos + 3) & ~3u;

                // Skip metadata
                if (pos + 4 <= end)
                {
                    uint meta_count = file.View.ReadUInt32(pos);
                    pos += 4;

                    for (uint j = 0; j < meta_count && pos < end; j++)
                    {
                        if (pos + 4 > end)
                            break;

                        uint meta_size = file.View.ReadUInt32(pos);
                        pos += 4 + meta_size;
                        pos = (pos + 3) & ~3u;

                        if (pos + 16 <= end)
                            pos += 16; // Skip 4 unknown uint32 values
                    }
                }

                dir.Add(entry);
            }

            return dir;
        }

        private bool IsImageSignature(ArcView file, uint offset)
        {
            if (offset + 4 > file.MaxOffset)
                return false;

            return file.View.AsciiEqual(offset,   "AJP") ||
                   file.View.AsciiEqual(offset,   "QNT") ||
                   file.View.AsciiEqual(offset+1, "PNG");
        }

        private (string, string) GetExtension(ArcView file, long offset, FlatDataType type)
        {
            switch (type)
            {
                case FlatDataType.CG:
                    if (file.View.AsciiEqual(offset,   "AJP")) return ("image", ".ajp");
                    if (file.View.AsciiEqual(offset,   "QNT")) return ("image", ".qnt");
                    if (file.View.AsciiEqual(offset+1, "PNG")) return ("image", ".png");
                    return ("image", ".img");

                case FlatDataType.ZLib:
                    return ("", ".z");

                default:
                    return ("", ".dat");
            }
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var packed_entry = entry as PackedEntry;
            if (packed_entry != null && packed_entry.IsPacked)
            {
                var compressed = arc.File.View.ReadBytes(packed_entry.Offset, packed_entry.Size);
                using (var input = new BinMemoryStream(compressed))
                using (var zstream = new ZLibStream(input, CompressionMode.Decompress))
                {
                    var output = new byte[packed_entry.UnpackedSize];
                    zstream.Read(output, 0, output.Length);
                    return new BinMemoryStream(output, entry.Name);
                }
            }

            return base.OpenEntry(arc, entry);
        }
    }
}