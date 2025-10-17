using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;

namespace GameRes.Formats.Liar
{
    public sealed class ScxScriptData : ScriptData
    {
        public uint FileHash;
        public uint MetadataCount;
        public uint TextNameCount;
        public uint VariablesCount;
        public uint BgCount;
        public uint CharCount;
        public uint SeCount;
        public uint BgmCount;
        public uint Version;

        public byte[] RawVariableValues;
        public List<ScxMetadataEntry> MetadataEntries { get; set; } = new List<ScxMetadataEntry> ();
        public List<string> TextNames { get; set; } = new List<string> ();
        public List<(string key, string value)> GameVariables { get; set; } = new List<(string, string)> ();
        public List<(string key, string value)> BgDatabase { get; set; } = new List<(string, string)> ();
        public List<(string key, string value)> CharDatabase { get; set; } = new List<(string, string)> ();
        public List<(string key, string value)> SeDatabase { get; set; } = new List<(string, string)> ();
        public List<(string key, string value)> BgmDatabase { get; set; } = new List<(string, string)> ();
    }

    public sealed class ScxMetadataEntry
    {
        public ushort ItemType { get; set; }
        public ushort ItemId { get; set; }
        public ushort Flags { get; set; }
        public ushort Param1 { get; set; }
        public short PrevItem { get; set; }
        public short NextItem { get; set; }
        public short[] ConditionValues { get; set; } = new short[4];
        public byte[] ConditionTypes { get; set; } = new byte[4];
        public byte[][] ConditionData { get; set; } = new byte[4][];
        public string Text { get; set; }
        public long FilePosition { get; set; }

        public override string ToString ()
        {
            return $"{FilePosition:X8}: Type={ItemType:X4} ID={ItemId:X4} Flags={Flags:X4} Text=\"{Text ?? "(null)"}\"";
        }

        public byte[] ToBytes ()
        {
            var result = new byte[216];

            BitConverter.GetBytes (ItemType).CopyTo (result, 0);
            BitConverter.GetBytes (ItemId).CopyTo (result, 2);
            BitConverter.GetBytes (Flags).CopyTo (result, 4);
            BitConverter.GetBytes (Param1).CopyTo (result, 6);
            BitConverter.GetBytes (PrevItem).CopyTo (result, 8);
            BitConverter.GetBytes (NextItem).CopyTo (result, 10);

            for (int i = 0; i < 4; i++)
            {
                BitConverter.GetBytes (ConditionValues[i]).CopyTo (result, 12 + i * 2);
            }

            for (int i = 0; i < 4; i++)
            {
                int typeOffset = 20 + (i * 48);
                int dataOffset = 22 + (i * 48);

                result[typeOffset] = ConditionTypes[i];
                if (ConditionData[i] != null)
                {
                    int copyLen = Math.Min (ConditionData[i].Length, 46);
                    Array.Copy (ConditionData[i], 0, result, dataOffset, copyLen);
                }
            }

            return result;
        }
    }

    [Export (typeof (ScriptFormat))]
    public sealed class ScxFormat : ScriptFormat
    {
        public override string Tag => "SCX/elf";
        public override string Description => "elf SCX scenario file";
        public override uint Signature => 0x00786373; // 'scx\0'
        public override ScriptType DataType => ScriptType.BinaryScript;

        private static readonly byte[] XorTable = new byte[] {
            0xA9, 0xB3, 0xF2, 0x87, 0xDC, 0xAF, 0x13, 0x67, 0xD5, 0x91, 0xEC
        };

        public ScxFormat ()
        {
            Extensions = new[] { "scx" };
        }

        public override bool IsScript (IBinaryStream file)
        {
            if (!file.Name.HasAnyOfExtensions (".scx"))
                return false;

            if (file.Length < 40)
                return false;

            file.Position = 0;
            uint signature = file.ReadUInt32 ();

            return signature == Signature;
        }

        public override ScriptData Read (string name, Stream stream)
        {
            return Read (name, stream, Encodings.cp932);
        }

        private static void DecryptData (byte[] data, int offset, int length)
        {
            for (int i = 0; i < length; i++)
            {
                byte keyByte = XorTable[i % 11];
                data[offset + i] ^= (byte)(keyByte + i);
            }
        }

        public override ScriptData Read (string name, Stream stream, Encoding encoding)
        {
            var metadata = new ScxScriptData { Encoding = encoding };

            using (var reader = new BinaryReader (stream, encoding, true))
            {
                uint signature = reader.ReadUInt32 ();
                if (signature != Signature)
                    throw new InvalidFormatException ("Invalid SCX signature");

                metadata.FileHash = reader.ReadUInt32 ();
            }

            stream.Position = 8;
            byte[] encryptedData = new byte[stream.Length - 8];
            stream.Read (encryptedData, 0, encryptedData.Length);
            DecryptData (encryptedData, 0, encryptedData.Length);

            List<uint> textOffsets = null;

            using (var ms = new MemoryStream (encryptedData))
            using (var reader = new BinaryReader (ms, encoding, true))
            {
                metadata.MetadataCount = reader.ReadUInt32 ();
                metadata.TextNameCount = reader.ReadUInt32 ();
                metadata.VariablesCount = reader.ReadUInt32 ();
                metadata.BgCount = reader.ReadUInt32 ();
                metadata.CharCount = reader.ReadUInt32 ();
                metadata.SeCount = reader.ReadUInt32 ();
                metadata.BgmCount = reader.ReadUInt32 ();
                metadata.Version = reader.ReadUInt32 ();

                const uint MAX_ENTRIES = 50000;
                const uint MAX_STRINGS = 50000;

                if (metadata.MetadataCount > MAX_ENTRIES)
                    throw new InvalidFormatException ($"Metadata count too large: {metadata.MetadataCount}");

                if (metadata.TextNameCount > MAX_STRINGS ||
                    metadata.VariablesCount > MAX_STRINGS ||
                    metadata.BgCount > MAX_STRINGS ||
                    metadata.CharCount > MAX_STRINGS ||
                    metadata.SeCount > MAX_STRINGS ||
                    metadata.BgmCount > MAX_STRINGS)
                {
                    throw new InvalidFormatException ("String counts too large");
                }

                textOffsets = new List<uint> ((int)metadata.MetadataCount);
                for (int i = 0; i < metadata.MetadataCount; i++)
                    textOffsets.Add (reader.ReadUInt32 ());

                reader.ReadBytes (28);

                for (int i = 0; i < metadata.MetadataCount; i++)
                {
                    if (reader.BaseStream.Position + 216 > reader.BaseStream.Length)
                        break;

                    var entryData = reader.ReadBytes (216);
                    var entry = ParseMetadataEntry (entryData, i * 216);
                    metadata.MetadataEntries.Add (entry);
                }

                int variableValuesSize = (int)Math.Min (metadata.VariablesCount * 12,
                    reader.BaseStream.Length - reader.BaseStream.Position);
                if (variableValuesSize > 0)
                    metadata.RawVariableValues = reader.ReadBytes (variableValuesSize);
                else
                    metadata.RawVariableValues = new byte[0];

                try
                {
                    if (reader.BaseStream.Position < reader.BaseStream.Length)
                        metadata.TextNames = ReadAlignedStrings (reader,
                            Math.Min (metadata.TextNameCount + 7, MAX_STRINGS), 32, encoding);

                    if (reader.BaseStream.Position < reader.BaseStream.Length)
                        reader.ReadByte (); // Alignment byte

                    if (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        metadata.GameVariables = ReadAlignedKvData (reader, Math.Min (metadata.VariablesCount, 10000), 64, encoding);
                        metadata.BgDatabase = ReadAlignedKvData (reader, Math.Min (metadata.BgCount, 10000), 64, encoding);
                        metadata.CharDatabase = ReadAlignedKvData (reader, Math.Min (metadata.CharCount, 10000), 64, encoding);
                        metadata.SeDatabase = ReadAlignedKvData (reader, Math.Min (metadata.SeCount, 1000), 64, encoding);
                        metadata.BgmDatabase = ReadAlignedKvData (reader, Math.Min (metadata.BgmCount, 1000), 64, encoding);
                    }
                }
                catch
                {
                }
            }

            for (int i = 0; i < metadata.MetadataEntries.Count; i++)
            {
                if (i < textOffsets.Count && textOffsets[i] != 0)
                {
                    try
                    {
                        uint textPosInDecrypted = textOffsets[i] - 8; // Text offset is absolute, subtract file header
                        if (textPosInDecrypted < encryptedData.Length)
                        {
                            // Find null terminator
                            int endPos = (int)textPosInDecrypted;
                            while (endPos < encryptedData.Length && encryptedData[endPos] != 0)
                                endPos++;

                            int length = endPos - (int)textPosInDecrypted;
                            if (length > 0)
                            {
                                metadata.MetadataEntries[i].Text = encoding.GetString (encryptedData, (int)textPosInDecrypted, length);
                            }
                        }
                    }
                    catch
                    {
                        metadata.MetadataEntries[i].Text = null;
                    }
                }
            }

            for (int i = 0; i < metadata.MetadataEntries.Count; i++)
            {
                var entry = metadata.MetadataEntries[i];
                metadata.TextLines.Add (new ScriptLine ((uint)i, entry.Text ?? ""));
            }

            metadata.Metadata["FileHash"] = $"0x{metadata.FileHash:X8}";
            metadata.Metadata["Version"] = metadata.Version;
            metadata.Metadata["EntryCount"] = metadata.MetadataEntries.Count;
            metadata.Metadata["TextCount"] = metadata.TextLines.Count;

            return metadata;
        }

        public override void Write (Stream stream, ScriptData script)
        {
            var scxData = script as ScxScriptData;
            if (scxData == null)
                throw new ArgumentException ("Script must be ScxScriptData type");

            var encoding = scxData.Encoding ?? Encodings.cp932;

            using (var writer = new BinaryWriter (stream, encoding, true))
            {
                writer.Write (Signature);
                writer.Write (scxData.FileHash);
            }

            using (var tempStream = new MemoryStream ())
            using (var writer = new BinaryWriter (tempStream, encoding, true))
            {
                writer.Write (scxData.MetadataCount);
                writer.Write (scxData.TextNameCount);
                writer.Write (scxData.VariablesCount);
                writer.Write (scxData.BgCount);
                writer.Write (scxData.CharCount);
                writer.Write (scxData.SeCount);
                writer.Write (scxData.BgmCount);
                writer.Write (scxData.Version);

                long textSectionStart = 40 + (scxData.MetadataCount * 4) + 28 +
                                       (scxData.MetadataCount * 216) +
                                       (scxData.VariablesCount * 12);

                textSectionStart += EstimateAlignedStringsSize (scxData.TextNames, 32, encoding);
                textSectionStart += 1; // Alignment byte
                textSectionStart += scxData.VariablesCount * 64;
                textSectionStart += scxData.BgCount * 64;
                textSectionStart += scxData.CharCount * 64;
                textSectionStart += scxData.SeCount * 64;
                textSectionStart += scxData.BgmCount * 64;

                long currentTextOffset = textSectionStart;
                foreach (var entry in scxData.MetadataEntries)
                {
                    if (!string.IsNullOrEmpty (entry.Text))
                    {
                        writer.Write ((uint)currentTextOffset);
                        currentTextOffset += encoding.GetByteCount (entry.Text) + 1;
                    }
                    else
                    {
                        writer.Write ((uint)0);
                    }
                }

                writer.Write (new byte[28]);

                foreach (var entry in scxData.MetadataEntries)
                    writer.Write (entry.ToBytes ());

                if (scxData.RawVariableValues != null)
                    writer.Write (scxData.RawVariableValues);
                else
                    writer.Write (new byte[scxData.VariablesCount * 12]);

                WriteAlignedStrings (writer, scxData.TextNames, 32, encoding);
                writer.Write ((byte)0); // Alignment byte

                WriteAlignedKvData (writer, scxData.GameVariables, 64, encoding);
                WriteAlignedKvData (writer, scxData.BgDatabase, 64, encoding);
                WriteAlignedKvData (writer, scxData.CharDatabase, 64, encoding);
                WriteAlignedKvData (writer, scxData.SeDatabase, 64, encoding);
                WriteAlignedKvData (writer, scxData.BgmDatabase, 64, encoding);

                foreach (var entry in scxData.MetadataEntries)
                {
                    if (!string.IsNullOrEmpty (entry.Text))
                    {
                        var textBytes = encoding.GetBytes (entry.Text);
                        writer.Write (textBytes);
                        writer.Write ((byte)0);
                    }
                }

                var data = tempStream.ToArray ();
                DecryptData (data, 0, data.Length); // XOR is symmetric
                stream.Write (data, 0, data.Length);
            }
        }

        public override Stream ConvertFrom (IBinaryStream file)
        {
            var script = Read (file.Name, file.AsStream);
            var scxData = script as ScxScriptData;

            var outputStream = new MemoryStream ();
            using (var writer = new StreamWriter (outputStream, Encoding.UTF8, 1024, true))
            {
                writer.WriteLine ($"# SCX Script: {Path.GetFileName (file.Name)}");
                writer.WriteLine ($"# Version: {scxData?.Version ?? 0}");
                writer.WriteLine ($"# Hash: 0x{scxData?.FileHash:X8}");
                writer.WriteLine ($"# Total Entries: {scxData?.MetadataCount ?? 0}");
                writer.WriteLine ($"# Entries with Text: {scxData?.MetadataEntries.Count (e => !string.IsNullOrEmpty (e.Text)) ?? 0}");
                writer.WriteLine ();

                if (scxData != null && scxData.MetadataEntries.Count > 0)
                {
                    writer.WriteLine ("# === Text ===");
                    writer.WriteLine ("# Format: [Index:ItemID] Text");
                    writer.WriteLine ();

                    for (int i = 0; i < scxData.MetadataEntries.Count; i++)
                    {
                        var entry = scxData.MetadataEntries[i];
                        writer.WriteLine ($"[{i:D4}:{entry.ItemId:X4}] {entry.Text ?? ""}");
                    }
                }

                writer.Flush ();
                outputStream.Position = 0;
                return outputStream;
            }
        }

        public override Stream ConvertBack (IBinaryStream file)
        {
            var filetext = string.Empty;
            using (var reader = new StreamReader (file.AsStream, Encoding.UTF8))
            {
                filetext = reader.ReadToEnd ();
            }

            var lines = filetext.Split (new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            uint fileHash = 0;
            uint version = 0;

            foreach (var line in lines)
            {
                if (line.StartsWith ("# Hash: 0x"))
                {
                    string hashStr = line.Substring (10);
                    uint.TryParse (hashStr, System.Globalization.NumberStyles.HexNumber, null, out fileHash);
                }
                else if (line.StartsWith ("# Version: "))
                {
                    uint.TryParse (line.Substring (11), out version);
                }
            }

            var scxData = new ScxScriptData
            {
                FileHash = fileHash,
                Version = version,
                Encoding = Encodings.cp932
            };

            // Parse text entries
            var textEntries = new Dictionary<int, string> ();
            var entryIds = new Dictionary<int, ushort> ();

            foreach (var line in lines)
            {
                if (line.StartsWith ("[") && line.Contains ("]"))
                {
                    int bracketEnd = line.IndexOf (']');
                    if (bracketEnd > 0)
                    {
                        string indexPart = line.Substring (1, bracketEnd - 1);
                        string[] parts = indexPart.Split (':');

                        if (parts.Length >= 1 && int.TryParse (parts[0], out int index))
                        {
                            string text = line.Substring (bracketEnd + 1).TrimStart ();
                            textEntries[index] = text;

                            if (parts.Length >= 2)
                            {
                                ushort.TryParse (parts[1], System.Globalization.NumberStyles.HexNumber, null, out ushort entryId);
                                entryIds[index] = entryId;
                            }
                        }
                    }
                }
            }

            // Create metadata entries
            int maxIndex = textEntries.Keys.Count > 0 ? textEntries.Keys.Max () : 0;
            for (int i = 0; i <= maxIndex; i++)
            {
                var entry = new ScxMetadataEntry
                {
                    ItemType = 0,
                    ItemId = entryIds.ContainsKey (i) ? entryIds[i] : (ushort)i,
                    Flags = 0,
                    Param1 = 0,
                    PrevItem = -1,
                    NextItem = -1,
                    FilePosition = i * 216
                };

                for (int j = 0; j < 4; j++)
                {
                    entry.ConditionValues[j] = 0;
                    entry.ConditionTypes[j] = 0;
                    entry.ConditionData[j] = new byte[46];
                }

                if (textEntries.ContainsKey (i))
                    entry.Text = textEntries[i];

                scxData.MetadataEntries.Add (entry);
            }

            scxData.MetadataCount = (uint)scxData.MetadataEntries.Count;
            scxData.TextNameCount = 0;
            scxData.VariablesCount = 0;
            scxData.BgCount = 0;
            scxData.CharCount = 0;
            scxData.SeCount = 0;
            scxData.BgmCount = 0;

            using (var outputStream = new MemoryStream ())
            {
                Write (outputStream, scxData);
                outputStream.Position = 0;
                return outputStream;
            }
        }

        private ScxMetadataEntry ParseMetadataEntry (byte[] data, long position)
        {
            var entry = new ScxMetadataEntry { FilePosition = position };

            entry.ItemType = BitConverter.ToUInt16 (data, 0);
            entry.ItemId = BitConverter.ToUInt16 (data, 2);
            entry.Flags = BitConverter.ToUInt16 (data, 4);
            entry.Param1 = BitConverter.ToUInt16 (data, 6);
            entry.PrevItem = BitConverter.ToInt16 (data, 8);
            entry.NextItem = BitConverter.ToInt16 (data, 10);

            for (int i = 0; i < 4; i++)
            {
                entry.ConditionValues[i] = BitConverter.ToInt16 (data, 12 + i * 2);
            }

            for (int i = 0; i < 4; i++)
            {
                int typeOffset = 20 + (i * 48);
                int dataOffset = 22 + (i * 48);

                entry.ConditionTypes[i] = data[typeOffset];
                entry.ConditionData[i] = new byte[46];
                Array.Copy (data, dataOffset, entry.ConditionData[i], 0, 46);
            }

            return entry;
        }

        private string ReadCString (BinaryReader reader, Encoding encoding)
        {
            var bytes = new List<byte> ();
            byte b;
            while ((b = reader.ReadByte ()) != 0)
            {
                bytes.Add (b);
            }
            return encoding.GetString (bytes.ToArray ());
        }

        private List<string> ReadAlignedStrings (BinaryReader reader, uint count, int alignment, Encoding encoding)
        {
            var result = new List<string> ();
            int remaining = (int)count;
            var currentBlock = new List<byte> ();

            while (remaining > 0 && reader.BaseStream.Position < reader.BaseStream.Length)
            {
                if (currentBlock.Count == 0)
                {
                    var blockData = reader.ReadBytes (alignment);
                    currentBlock.AddRange (blockData);
                }

                int nullPos = currentBlock.IndexOf (0);
                if (nullPos >= 0)
                {
                    var stringBytes = currentBlock.Take (nullPos).ToArray ();
                    if (stringBytes.Length > 0)
                    {
                        result.Add (encoding.GetString (stringBytes));
                        remaining--;
                    }
                    currentBlock.RemoveRange (0, nullPos + 1);
                }
                else
                {
                    var nextBlock = reader.ReadBytes (alignment);
                    if (nextBlock.Length == 0)
                        break;
                    currentBlock.AddRange (nextBlock);
                }
            }

            return result;
        }

        private List<(string key, string value)> ReadAlignedKvData (BinaryReader reader, uint count, int entrySize, Encoding encoding)
        {
            var result = new List<(string, string)> ();
            int halfSize = entrySize / 2;

            for (uint i = 0; i < count; i++)
            {
                if (reader.BaseStream.Position + entrySize > reader.BaseStream.Length)
                    break;

                var entryData = reader.ReadBytes (entrySize);

                var keyData = new byte[halfSize];
                var valData = new byte[halfSize];
                Array.Copy (entryData, 0, keyData, 0, halfSize);
                Array.Copy (entryData, halfSize, valData, 0, halfSize);

                int keyLen = Array.IndexOf (keyData, (byte)0);
                if (keyLen < 0) keyLen = halfSize;
                int valLen = Array.IndexOf (valData, (byte)0);
                if (valLen < 0) valLen = halfSize;

                string key = encoding.GetString (keyData, 0, keyLen);
                string val = encoding.GetString (valData, 0, valLen);

                result.Add ((key, val));
            }

            return result;
        }

        private int EstimateAlignedStringsSize (List<string> strings, int alignment, Encoding encoding)
        {
            int totalSize = 0;
            int currentBlockUsed = 0;

            foreach (var str in strings)
            {
                int strSize = encoding.GetByteCount (str) + 1;

                if (currentBlockUsed + strSize > alignment)
                {
                    totalSize += alignment;
                    currentBlockUsed = strSize % alignment;
                }
                else
                {
                    currentBlockUsed += strSize;
                }
            }

            if (currentBlockUsed > 0)
                totalSize += alignment;

            return totalSize;
        }

        private void WriteAlignedStrings (BinaryWriter writer, List<string> strings, int alignment, Encoding encoding)
        {
            var currentBlock = new byte[alignment];
            int blockPos = 0;

            foreach (var str in strings)
            {
                var strBytes = encoding.GetBytes (str);
                int totalSize = strBytes.Length + 1;

                if (blockPos + totalSize > alignment)
                {
                    writer.Write (currentBlock);
                    currentBlock = new byte[alignment];
                    blockPos = 0;
                }

                Array.Copy (strBytes, 0, currentBlock, blockPos, strBytes.Length);
                blockPos += strBytes.Length;
                currentBlock[blockPos++] = 0;
            }

            if (blockPos > 0)
            {
                writer.Write (currentBlock);
            }
        }

        private void WriteAlignedKvData (BinaryWriter writer, List<(string key, string value)> data, int entrySize, Encoding encoding)
        {
            int halfSize = entrySize / 2;
            var entryBuffer = new byte[entrySize];

            foreach (var (key, value) in data)
            {
                Array.Clear (entryBuffer, 0, entrySize);

                var keyBytes = encoding.GetBytes (key);
                var valBytes = encoding.GetBytes (value);

                int keyLen = Math.Min (keyBytes.Length, halfSize - 1);
                int valLen = Math.Min (valBytes.Length, halfSize - 1);

                Array.Copy (keyBytes, 0, entryBuffer, 0, keyLen);
                Array.Copy (valBytes, 0, entryBuffer, halfSize, valLen);

                writer.Write (entryBuffer);
            }
        }
    }
}