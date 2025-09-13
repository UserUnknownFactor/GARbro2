using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;

namespace GameRes.Formats.HSP
{
    [Export (typeof (ArchiveFormat))]
    public class DpmOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DPM"; } }
        public override string Description { get { return "Hot Soup Processor resource archive"; } }
        public override uint     Signature { get { return  0; } } // 'DPMX'
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  true; } }


        private const uint    MAGIC_DPMX = 0x584D5044;
        private const int     ENTRY_SIZE = 32;
        private const int    HEADER_SIZE = 0x10;
        private const int  FILENAME_SIZE = 0x10;
        private const int  HSP_INIT_SIZE = 32;

        public DpmOpener()
        {
            Extensions = new string[] { "dpm", "bin", "dat", "exe" };
            Signatures   = new uint[] {  MAGIC_DPMX, 0 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var archiveInfo = DetectArchiveFormat (file);
            if (archiveInfo == null)
                return null;

            var (baseOffset, arcKey, isInsideExe, count, indexOffset) = archiveInfo.Value;

            if (!IsSaneCount (count))
                return null;

            uint dataSize = 0;
            if (isInsideExe)
            {
                var exe = new ExeFile (file);
                dataSize = (uint)(exe.Overlay.Size - (indexOffset - exe.Overlay.Offset + ENTRY_SIZE * count));
            }
            else
            {
                dataSize = (uint)(file.MaxOffset - (indexOffset + ENTRY_SIZE * count));
            }

            var dir = ReadDirectory (file, count, baseOffset, indexOffset);
            if (dir == null)
                return null;

            return isInsideExe || arcKey != 0
                ? new DpmArchive (file, this, dir, arcKey, dataSize)
                : new DpmArchive (file, this, dir);
        }

        private (long baseOffset, uint arcKey, bool isInsideExe, int count, long indexOffset)? 
            DetectArchiveFormat (ArcView file)
        {
            uint signature = file.View.ReadUInt32 (0);

            if ((signature & 0xFFFF) == ExeFile.MAGIC_MZ) // Executable with archive
                return HandleExecutableArchive (file);

            if (signature == MAGIC_DPMX) // Standalone archive
                return HandleStandaloneArchive (file);

            return null;
        }

        private (long baseOffset, uint arcKey, bool isInsideExe, int count, long indexOffset)? 
            HandleExecutableArchive (ArcView file)
        {
            var exe = new ExeFile (file);

            // Use the Overlay property which automatically finds appended data
            if (exe.Overlay.Size <= 4 || !file.View.AsciiEqual (exe.Overlay.Offset, "DPMX"))
                return null;

            long dpmOffset = exe.Overlay.Offset;

            // Search for the HSP key in the executable
            uint arcKey = SearchForHspKey (exe) ?? SearchForOffsetKey (exe, dpmOffset) ?? 0;
            if (arcKey == 0)
                return null;

            // Read DPM header from the overlay
            int count = file.View.ReadInt32 (dpmOffset + 8);
            long indexOffset = dpmOffset + HEADER_SIZE + file.View.ReadUInt32 (dpmOffset + 0xC);
            long baseOffset = dpmOffset + file.View.ReadUInt32 (dpmOffset + 4);

            return (baseOffset, arcKey, true, count, indexOffset);
        }

        private (long baseOffset, uint arcKey, bool isInsideExe, int count, long indexOffset)? 
            HandleStandaloneArchive (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            long indexOffset = HEADER_SIZE + file.View.ReadUInt32 (0xC);
            long baseOffset = file.View.ReadUInt32 (4);

            return (baseOffset, 0, false, count, indexOffset);
        }

        private List<Entry> ReadDirectory (ArcView file, int count, long baseOffset, long indexOffset)
        {
            var dir = new List<Entry>(count);

            for (int i = 0; i < count; ++i)
            {
                var entry = ReadEntry (file, indexOffset, baseOffset);
                if (entry == null || !entry.CheckPlacement (file.MaxOffset))
                    return null;

                dir.Add (entry);
                indexOffset += ENTRY_SIZE;
            }

            return dir;
        }

        private DpmEntry ReadEntry (ArcView file, long indexOffset, long baseOffset)
        {
            var name = file.View.ReadString (indexOffset, FILENAME_SIZE);
            if (string.IsNullOrWhiteSpace (name))
                return null;

            return new DpmEntry
            {
                Name   = name.Trim(),
                Type   = FormatCatalog.Instance.GetTypeFromName (name),
                Key    = file.View.ReadUInt32 (indexOffset + 0x14),
                Offset = file.View.ReadUInt32 (indexOffset + 0x18) + baseOffset,
                Size   = file.View.ReadUInt32 (indexOffset + 0x1C)
            };
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!(entry is DpmEntry dEnt) || !(arc is DpmArchive dArc) || dEnt.Key == 0)
                return base.OpenEntry (arc, entry);

            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            dArc.DecryptEntry (data, dEnt.Key);
            return new BinMemoryStream (data, entry.Name);
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options, 
            EntryCallback callback)
        {
            var entries = list.ToList();
            //if (!entries.Any())
                //throw new InvalidOperationException ("Empty archive");
            var dpmOptions = GetOptions<DpmOptions>(options);
            uint key = dpmOptions?.Key ?? 0;

            WriteArchive (output, entries, key, callback);
        }

        private void WriteArchive (Stream output, List<Entry> entries, uint key, EntryCallback callback)
        {
            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                // Write header
                writer.Write (MAGIC_DPMX);
                long dataOffsetPos = output.Position;
                writer.Write (0u); // Placeholder for data offset
                writer.Write (entries.Count);
                writer.Write (0u); // Magic2

                // Calculate offsets
                long indexStart = output.Position;
                long dataStart = indexStart + entries.Count * ENTRY_SIZE;
                uint currentOffset = 0;

                // Write index
                foreach (var entry in entries)
                {
                    WriteIndexEntry (writer, entry, currentOffset, key);
                    currentOffset += (uint)entry.Size;
                }

                // Update data offset
                output.Position = dataOffsetPos;
                writer.Write ((uint)(dataStart - HEADER_SIZE));
                output.Position = dataStart;

                // Write file data
                foreach (var entry in entries)
                    WriteEntryData (writer, entry, key, callback);
            }
        }

        private void WriteIndexEntry (BinaryWriter writer, Entry entry, uint offset, uint key)
        {
            var nameBytes = new byte[FILENAME_SIZE];
            Encoding.ASCII.GetBytes (entry.Name, 0, 
                Math.Min (entry.Name.Length, FILENAME_SIZE), nameBytes, 0);

            writer.Write (nameBytes);
            writer.Write (0xFFFFFFFF); // Magic
            writer.Write (key);
            writer.Write (offset);
            writer.Write ((uint)entry.Size);
        }

        private void WriteEntryData (BinaryWriter writer, Entry entry, uint key, EntryCallback callback)
        {
            callback?.Invoke ((int)entry.Size, entry, $"Packing {entry.Name}");

            using (var input = File.OpenRead (entry.Name))
            {
                var data = new byte[entry.Size];
                input.Read (data, 0, data.Length);

                if (key != 0)
                {
                    var archive = new DpmArchive (null, this, null, key, 0);
                    archive.EncryptEntry (data, key);
                }

                writer.Write (data);
            }
        }

        private static uint? SearchForHspKey (ExeFile exe)
        {
            // Search for HSP initialization data pattern: 'x??y??d??s??k'
            var searchPattern = new byte[] { 
                (byte)'x', 0, 0, (byte)'y', 0, 0, 
                (byte)'d', 0, 0, (byte)'s', 0, 0, (byte)'k' 
            };

            foreach (var sectionName in new[] { ".rdata", ".data", ".text" })
            {
                if (!exe.ContainsSection (sectionName))
                    continue;

                var section = exe.Sections[sectionName];
                long position = exe.FindString (section, searchPattern, step: 1);
                if (position >= 0)
                {
                    long structureStart = position - 19;
                    uint key = exe.View.ReadUInt32 (structureStart + 32);
                    ushort width = exe.View.ReadUInt16 (structureStart + 20);
                    ushort height = exe.View.ReadUInt16 (structureStart + 23);

                    if (width > 0 && width <= 4096 && height > 0 && height <= 4096)
                    {
                        var offsetBytes = new byte[9];
                        exe.View.Read (structureStart + 9, offsetBytes, 0, 9);
                        string hspOffset = Encoding.ASCII.GetString (offsetBytes).TrimEnd('\0');
                        System.Diagnostics.Debug.WriteLine ($"HSP Key found: 0x{key:X8}");
                        System.Diagnostics.Debug.WriteLine ($"  HSPInitDataOffset: {hspOffset}");
                        System.Diagnostics.Debug.WriteLine ($"  Resolution: {width}x{height}");

                        return key;
                    }
                }
            }

            return null;
        }

        private static uint? SearchForOffsetKey (ExeFile exe, long dpmOffset)
        {
            uint baseOffset = (uint)(dpmOffset - 0x10000);
            var offsetStr = baseOffset.ToString() + '\0';
            var offsetBytes = Encoding.ASCII.GetBytes (offsetStr);

            foreach (var sectionName in new[] { ".rdata", ".data" })
            {
                if (!exe.ContainsSection (sectionName))
                    continue;

                long keyPos = exe.FindString (exe.Sections[sectionName], offsetBytes);
                if (keyPos >= 0)
                    return exe.View.ReadUInt32 (keyPos + 0x17);
            }

            return null;
        }

        private DpmxScheme _defaultScheme = new DpmxScheme();

        public IDictionary<string, uint> KnownKeys => _defaultScheme.KnownKeys;

        public override ResourceScheme Scheme
        {
            get => _defaultScheme;
            set => _defaultScheme = (DpmxScheme)value;
        }
    }

    internal class DpmEntry : Entry
    {
        public uint Key { get; set; }
    }

    internal class DpmArchive : ArcFile
    {
        private byte _seed1;
        private byte _seed2;

        private const byte DRIFT1 = 0x55;
        private const byte DRIFT2 = 0xAA;

        public DpmArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
            _seed1 = DRIFT2;
            _seed2 = DRIFT1;
        }

        public DpmArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, 
            uint arcKey, uint dpmSize)
            : base (arc, impl, dir)
        {
            CalculateSeeds (arcKey, dpmSize);
        }

        private static (byte b0, byte b1, byte b2, byte b3) ExtractBytes (uint value)
        {
            byte b0 = (byte)( value        & 0xFF);
            byte b1 = (byte)((value >>  8) & 0xFF);
            byte b2 = (byte)((value >> 16) & 0xFF);
            byte b3 = (byte)((value >> 24) & 0xFF);
            return (b0, b1, b2, b3);
        }

        private void CalculateSeeds (uint arcKey, uint dpmSize)
        {
            var (b0, b1, b2, b3) = ExtractBytes (arcKey);

            /*
            // Compiler generated:
            long product1 = (long)b0 * b2 * 0x55555556L; // 0x55555556 is ~1/3 in fixed-point arithmetic
            uint x1 = (uint)(product1 >> 32);  // Divide by 2^32 to get the integer part
            _seed1 = (byte)((x1 ^ dpmSize) & 0xFF);

            long product2 = (long)b1 * b3 * 0x66666667L; // 0x66666667 is ~1/5 in fixed-point arithmetic
            uint x2 = (uint)(product2 >> 33);  // Divide by 2^33 to get the integer part
            _seed2 = (byte)((x2 ^ dpmSize ^ 0xAA) & 0xFF);
            */

            // But we can do the integer division directly:
            _seed1 = (byte)(((b0 * b2 / 3) ^ dpmSize) & 0xFF);
            _seed2 = (byte)(((b1 * b3 / 5) ^ dpmSize ^ DRIFT2) & 0xFF);

        }

        internal void DecryptEntry (byte[] data, uint entryKey)
        {
            var (key1, key2) = GetPersonalFileKey (entryKey);
            byte res = 0;

            for (int i = 0; i < data.Length; ++i)
            {
                res = (byte)(res + ((data[i] - key2) ^ key1));
                data[i] = res;
            }
        }

        internal void EncryptEntry (byte[] data, uint entryKey)
        {
            var (key1, key2) = GetPersonalFileKey (entryKey);

            byte val = 0;
            for (int i = 0; i < data.Length; ++i)
            {
                byte original = data[i];
                data[i] = (byte)((val ^ key1) + key2);
                val = original;
            }
        }

        private (byte key1, byte key2) GetPersonalFileKey (uint fileKey)
        {
            var (b0, b1, b2, b3) = ExtractBytes (fileKey);

            byte key1 = (byte)(((b0 + DRIFT1) ^ b2) + _seed1);
            byte key2 = (byte)(((b1 + DRIFT2) ^ b3) + _seed2);

            return (key1, key2);
        }
    }

    [Serializable]
    public class DpmxScheme : ResourceScheme
    {
        public IDictionary<string, uint> KnownKeys { get; set; } = new Dictionary<string, uint>() 
        { 
            { "Game1", 0xAC52AE58 }, 
            { "Game2", 0x233A66DF } 
        };
    }

    public class DpmOptions : ResourceOptions
    {
        public uint Key { get; set; }
    }
}
