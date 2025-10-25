using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Utility;
using Newtonsoft.Json;

namespace GameRes.Formats.Solfa
{
    [Export(typeof(ArchiveFormat))]
    public class Sec4Opener : ArchiveFormat
    {
        public override string         Tag { get { return "SEC4"; } }
        public override string Description { get { return "SAS old engine resource index"; } }
        public override uint     Signature { get { return  0x34434553; } } // 'SEC4'
        public override bool  IsHierarchic { get { return  false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint version = file.View.ReadUInt32 (4);
            if (version < 0x57E40 || version > 0x57E43)
                return null;

            // Read header (108 bytes)
            var header = new Sec4Header
            {
                Signature = file.View.ReadUInt32 (0),
                Version = version,
                ClassNamePtr = file.View.ReadUInt32 (0x0C),
                Pointers = new uint[7]
            };

            for (int i = 0; i < 7; i++)
                header.Pointers[i] = file.View.ReadUInt32 ((uint)(0x10 + i * 4));

            header.Array1Count = file.View.ReadInt32 (0x48);
            header.Array2Count = file.View.ReadInt32 (0x4C);
            header.Array3Count = file.View.ReadInt32 (0x50);
            header.DataSize = file.View.ReadUInt32 (0x54);
            header.Array1Offset = file.View.ReadUInt32 (0x5C);
            header.Array2Offset = file.View.ReadUInt32 (0x60);
            header.Array3Offset = file.View.ReadUInt32 (0x64);
            header.DataBlobOffset = file.View.ReadUInt32 (0x68);

            if (header.DataBlobOffset + header.DataSize > file.MaxOffset)
                return null;

            header.DataBlob = file.View.ReadBytes (header.DataBlobOffset, header.DataSize);

            header.Array1 = ReadArrayEntries (file, header.Array1Offset, header.Array1Count);
            header.Array2 = ReadArrayEntries (file, header.Array2Offset, header.Array2Count);
            header.Array3 = ReadArrayEntries (file, header.Array3Offset, header.Array3Count);

            var metadata = BuildMetadata (file, header);
            string json = JsonConvert.SerializeObject (metadata, Formatting.Indented);
            byte[] jsonBytes = Encoding.UTF8.GetBytes (json);

            var entries = new List<Entry> ();

            entries.Add (new Entry {
                Name = "metadata.json",
                Type = "",
                Offset = 0,
                Size = (uint)jsonBytes.Length
            });

            m_metadata[file.Name] = jsonBytes;

            return new Sec4Archive (file, this, entries, header);
        }

        Sec4Metadata BuildMetadata (ArcView file, Sec4Header header)
        {
            var metadata = new Sec4Metadata
            {
                Version = string.Format ("0x{0:X8} ({0})", header.Version),
                ClassNamePointer = string.Format ("0x{0:X8}", header.ClassNamePtr),
                ClassName = ReadStringFromBlob (header.DataBlob, header.ClassNamePtr),
                HeaderPointers = new List<PointerInfo> ()
            };

            for (int i = 0; i < 7; i++)
            {
                metadata.HeaderPointers.Add (new PointerInfo {
                    Name = string.Format ("Pointer[{0}]", i),
                    RawValue = string.Format ("0x{0:X8}", header.Pointers[i]),
                    PointsToString = ReadStringFromBlob (header.DataBlob, header.Pointers[i])
                });
            }

            metadata.Array1 = DecodeArray (header.Array1, header.DataBlob);
            metadata.Array2 = DecodeArray (header.Array2, header.DataBlob);
            metadata.Array3 = DecodeArray (header.Array3, header.DataBlob);

            metadata.DataBlobStrings = ExtractAllStrings (header.DataBlob, header.DataBlobOffset);

            return metadata;
        }

        List<ArrayEntryInfo> DecodeArray (Sec4ArrayEntry[] array, byte[] dataBlob)
        {
            var result = new List<ArrayEntryInfo> ();
            if (array == null)
                return result;

            for (int i = 0; i < array.Length; i++)
            {
                var entry = array[i];
                var info = new ArrayEntryInfo
                {
                    Index = i,
                    Field1 = string.Format ("0x{0:X8} ({0})", entry.Field1),
                    Field2 = string.Format ("0x{0:X8} ({0})", entry.Field2),
                    Pointer = string.Format ("0x{0:X8}", entry.Pointer),
                    PointsToString = ReadStringFromBlob (dataBlob, entry.Pointer)
                };

                result.Add (info);
            }
            return result;
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            if (entry.Name == "metadata.json")
            {
                var sarc = arc as Sec4Archive;
                if (sarc != null && m_metadata.TryGetValue(sarc.OriginalPath, out byte[] json))
                    return new BinMemoryStream(json);
            }

            return base.OpenEntry(arc, entry);
        }

        Sec4ArrayEntry[] ReadArrayEntries(ArcView file, uint offset, int count)
        {
            if (count <= 0 || offset == 0 || offset + count * 12 > file.MaxOffset)
                return new Sec4ArrayEntry[0];

            var entries = new Sec4ArrayEntry[count];
            for (int i = 0; i < count; i++)
            {
                uint pos = offset + (uint)(i * 12);
                entries[i] = new Sec4ArrayEntry
                {
                    Field1 = file.View.ReadUInt32(pos),
                    Field2 = file.View.ReadUInt32(pos + 4),
                    Pointer = file.View.ReadUInt32(pos + 8)
                };
            }
            return entries;
        }

        List<StringInfo> ExtractAllStrings(byte[] dataBlob, uint baseOffset)
        {
            var strings = new List<StringInfo>();
            if (dataBlob == null)
                return strings;

            int pos = 0;
            while (pos < dataBlob.Length)
            {
                while (pos < dataBlob.Length && dataBlob[pos] == 0)
                    pos++;

                if (pos >= dataBlob.Length)
                    break;

                int start = pos;
                
                while (pos < dataBlob.Length && dataBlob[pos] != 0)
                    pos++;

                if (pos > start)
                {
                    try
                    {
                        string str = Encodings.cp932.GetString(dataBlob, start, pos - start);
                        if (str.Length >= 2 && !string.IsNullOrWhiteSpace(str))
                        {
                            strings.Add(new StringInfo
                            {
                                Offset = string.Format("0x{0:X8}", baseOffset + start),
                                RelativeOffset = string.Format("0x{0:X8}", start),
                                Value = str
                            });
                        }
                    }
                    catch { }
                }
                pos++;
            }
            return strings;
        }

        string ReadStringAt(ArcView file, uint offset)
        {
            if (offset >= file.MaxOffset)
                return null;

            var bytes = new List<byte>();
            uint pos = offset;
            while (pos < file.MaxOffset)
            {
                byte b = file.View.ReadByte(pos++);
                if (b == 0)
                    break;
                bytes.Add(b);
                if (bytes.Count > 256)
                    break;
            }

            if (bytes.Count == 0)
                return null;

            try
            {
                return Encodings.cp932.GetString(bytes.ToArray());
            }
            catch
            {
                return null;
            }
        }

        string ReadStringFromBlob(byte[] blob, uint offset)
        {
            if (offset >= blob.Length)
                return null;

            int end = (int)offset;
            while (end < blob.Length && blob[end] != 0)
                end++;

            if (end == offset)
                return null;

            try
            {
                return Encodings.cp932.GetString(blob, (int)offset, end - (int)offset);
            }
            catch
            {
                return null;
            }
        }

        static Dictionary<string, byte[]> m_metadata = new Dictionary<string, byte[]>();
    }

    class Sec4Header
    {
        public uint Signature;
        public uint Version;
        public uint ClassNamePtr;
        public uint[] Pointers;
        public int Array1Count;
        public int Array2Count;
        public int Array3Count;
        public uint DataSize;
        public uint Array1Offset;
        public uint Array2Offset;
        public uint Array3Offset;
        public uint DataBlobOffset;
        public byte[] DataBlob;
        public Sec4ArrayEntry[] Array1;
        public Sec4ArrayEntry[] Array2;
        public Sec4ArrayEntry[] Array3;
    }

    class Sec4ArrayEntry
    {
        public uint Field1;
        public uint Field2;
        public uint Pointer;
    }

    class Sec4Metadata
    {
        public string Version { get; set; }
        public string ClassNamePointer { get; set; }
        public string ClassName { get; set; }
        public List<PointerInfo> HeaderPointers { get; set; }
        public List<ArrayEntryInfo> Array1 { get; set; }
        public List<ArrayEntryInfo> Array2 { get; set; }
        public List<ArrayEntryInfo> Array3 { get; set; }
        public List<StringInfo> DataBlobStrings { get; set; }
    }

    class PointerInfo
    {
        public string Name { get; set; }
        public string RawValue { get; set; }
        public string RelocatedValue { get; set; }
        public string PointsToString { get; set; }
    }

    class ArrayEntryInfo
    {
        public int Index { get; set; }
        public string Field1 { get; set; }
        public string Field2 { get; set; }
        public string Pointer { get; set; }
        public string PointsToString { get; set; }
    }

    class StringInfo
    {
        public string Offset { get; set; }
        public string RelativeOffset { get; set; }
        public string Value { get; set; }
    }

    class Sec4Archive : ArcFile
    {
        public readonly Sec4Header Header;
        public readonly string OriginalPath;

        public Sec4Archive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, Sec4Header header)
            : base(arc, impl, dir)
        {
            Header = header;
            OriginalPath = arc.Name;
        }
    }
}