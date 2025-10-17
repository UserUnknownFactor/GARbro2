using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;

namespace GameRes.Formats.Liar
{
    /// <summary>
    /// Metadata container for Liar-soft GSC script files.
    /// </summary>
    public sealed class GscScriptData : ScriptData
    {
        public uint FileSize;
        public uint HeaderSize;
        public uint CodeSectionSize;
        public uint StringIndexSize;
        public uint StringDataSize;
        public uint[] UnknownHeaderValues = new uint[4];

        public byte[] RawHeaderData;
        public byte[] RawCodeData;
        public byte[] RawStringIndexData;
        public byte[] RawStringData;
        public byte[] RawFooterData;

        public List<GscCommand> ParsedCommands { get; set; } = new List<GscCommand>();
    }

    /// <summary>
    /// Represents a single GSC bytecode command.
    /// </summary>
    public sealed class GscCommand
    {
        public ushort Opcode { get; set; }
        public string CommandName { get; set; }
        public string ArgumentFormat { get; set; }
        public List<int> Arguments { get; set; } = new List<int>();
        public long FilePosition { get; set; }

        public override string ToString()
        {
            string argumentList = string.Join(", ", Arguments.Select(arg => arg.ToString()));
            string displayName = !string.IsNullOrEmpty(CommandName) ? CommandName : $"CMD_{Opcode:X4}";
            return $"{FilePosition:X8}: {displayName}({argumentList})";
        }
    }

    /// <summary>
    /// GSC opcode definitions from Tester's decompiler.
    /// </summary>
    internal static class GscOpcodeDatabase
    {
        private static readonly Dictionary<ushort, (string format, string name)> KnownOpcodes =
            new Dictionary<ushort, (string, string)>
        {
            // Named commands
            {0x0003, ("i", "JUMP_UNLESS")},
            {0x0005, ("i", "JUMP")},
            {0x000C, ("ii", "CALL_SCRIPT")},
            {0x000D, ("i", "PAUSE")},
            {0x000E, ("hiiiiiiiiiiiiii", "CHOICE")},
            {0x0014, ("ii", "IMAGE_GET")},
            {0x001A, ("", "IMAGE_SET")},
            {0x001C, ("iii", "BLEND_IMG")},
            {0x001E, ("iiiiii", "IMAGE_DEF")},
            {0x0051, ("iiiiiii", "MESSAGE")},
            {0x0052, ("iiiiii", "APPEND_MESSAGE")},
            {0x0053, ("i", "CLEAR_MESSAGE_WINDOW")},
            {0x0079, ("ii", "GET_DIRECTORY")},
            {0x00C8, ("iiiiiiiiiii", "READ_SCENARIO")},
            {0x00FF, ("iiiii", "SPRITE")},
            {0x3500, ("hhh", "AND")},
            {0x4800, ("hhh", "EQUALS")},
            {0x5400, ("hhh", "GREATER_EQUALS")},
            {0xAA00, ("hhh", "ADD")},
            {0xF100, ("hh", "ASSIGN")},

            // Unnamed commands with known argument structures
            {0x0004, ("i", "")},
            {0x0008, ("", "")},
            {0x0009, ("h", "")},
            {0x000A, ("", "")},
            {0x000B, ("", "")},
            {0x000F, ("iiiiiiiiiiii", "")},
            {0x0010, ("i", "")},
            {0x0011, ("", "")},
            {0x0012, ("ii", "")},
            {0x0013, ("i", "")},
            {0x0015, ("i", "")},
            {0x0016, ("iiii", "")},
            {0x0017, ("iiii", "")},
            {0x0018, ("ii", "")},
            {0x0019, ("ii", "")},
            {0x001B, ("", "")},
            {0x001D, ("ii", "")},
            {0x0020, ("iiiiii", "")},
            {0x0021, ("iiiii", "")},
            {0x0022, ("iiiii", "")},
            {0x0023, ("ii", "")},
            {0x0024, ("ii", "")},
            {0x0025, ("ii", "")},
            {0x0026, ("iii", "")},
            {0x0027, ("iii", "")},
            {0x0028, ("ii", "")},
            {0x0029, ("ii", "")},
            {0x002A, ("ii", "")},
            {0x002B, ("ii", "")},
            {0x002C, ("i", "")},
            {0x002D, ("ii", "")},
            {0x002E, ("i", "")},
            {0x002F, ("ii", "")},
            {0x0030, ("ii", "")},
            {0x0031, ("ii", "")},
            {0x0032, ("", "")},
            {0x0033, ("", "")},
            {0x0034, ("", "")},
            {0x0035, ("i", "")},
            {0x0037, ("", "")},
            {0x0038, ("iiiii", "")},
            {0x0039, ("", "")},
            {0x003A, ("", "")},
            {0x003B, ("iiii", "")},
            {0x003C, ("iii", "")},
            {0x003D, ("ii", "")},
            {0x003E, ("i", "")},
            {0x003F, ("iii", "")},
            {0x0040, ("i", "")},
            {0x0041, ("i", "")},
            {0x0042, ("iiii", "")},
            {0x0043, ("i", "")},
            {0x0044, ("", "")},
            {0x0045, ("", "")},
            {0x0046, ("iiii", "")},
            {0x0047, ("iiii", "")},
            {0x0048, ("i", "")},
            {0x0049, ("iii", "")},
            {0x004A, ("i", "")},
            {0x004B, ("iiiii", "")},
            {0x004D, ("iiii", "")},
            {0x0050, ("i", "")},
            {0x005A, ("iii", "")},
            {0x005B, ("iiiii", "")},
            {0x005C, ("ii", "")},
            {0x005D, ("ii", "")},
            {0x005E, ("i", "")},
            {0x005F, ("ii", "")},
            {0x0060, ("ii", "")},
            {0x0061, ("ii", "")},
            {0x0062, ("ii", "")},
            {0x0063, ("iii", "")},
            {0x0064, ("iii", "")},
            {0x0065, ("ii", "")},
            {0x0066, ("i", "")},
            {0x0067, ("ii", "")},
            {0x0068, ("iiii", "")},
            {0x0069, ("i", "")},
            {0x006A, ("iiiii", "")},
            {0x006B, ("iii", "")},
            {0x006C, ("iii", "")},
            {0x006E, ("iii", "")},
            {0x006F, ("iii", "")},
            {0x0070, ("i", "")},
            {0x0071, ("ii", "")},
            {0x0072, ("ii", "")},
            {0x0073, ("ii", "")},
            {0x0074, ("ii", "")},
            {0x0075, ("ii", "")},
            {0x0078, ("ii", "")},
            {0x0082, ("iiii", "")},
            {0x0083, ("iiiii", "")},
            {0x0084, ("ii", "")},
            {0x0086, ("iii", "")},
            {0x0087, ("iiiii", "")},
            {0x0088, ("iii", "")},
            {0x0096, ("ii", "")},
            {0x0097, ("ii", "")},
            {0x0098, ("ii", "")},
            {0x0099, ("ii", "")},
            {0x009A, ("ii", "")},
            {0x009B, ("ii", "")},
            {0x009C, ("iii", "")},
            {0x009D, ("iiiii", "")},
            {0x009E, ("ii", "")},
            {0x009F, ("ii", "")},
            {0x00C9, ("iiiii", "")},
            {0x00CA, ("iii", "")},
            {0x00D2, ("ii", "")},
            {0x00D3, ("iiii", "")},
            {0x00D4, ("i", "")},
            {0x00D5, ("iii", "")},
            {0x00DC, ("iii", "")},
            {0x00DD, ("ii", "")},
            {0x00DE, ("", "")},
            {0x00DF, ("ii", "")},
            {0x00E1, ("iiiii", "")},
            {0x00E6, ("i", "")},
            {0x00E7, ("i", "")},
            {0x1800, ("hhh", "")},
            {0x1810, ("hhh", "")},
            {0x1900, ("hhh", "")},
            {0x1910, ("hhh", "")},
            {0x1A00, ("hhh", "")},
            {0x1A01, ("hhh", "")},
            {0x2500, ("hhh", "")},
            {0x4400, ("hhh", "")},
            {0x4810, ("hhh", "")},
            {0x4900, ("hhh", "")},
            {0x4A00, ("hhh", "")},
            {0x5800, ("hhh", "")},
            {0x6800, ("hhh", "")},
            {0x7800, ("hhh", "")},
            {0x7A00, ("hhh", "")},
            {0x8800, ("hhh", "")},
            {0x8A00, ("hhh", "")},
            {0x9800, ("hhh", "")},
            {0x9810, ("hhh", "")},
            {0x9A00, ("hhh", "")},
            {0xA100, ("hhh", "")},
            {0xA200, ("hhh", "")},
            {0xA201, ("hhh", "")},
            {0xA400, ("hhh", "")},
            {0xA500, ("hhh", "")},
            {0xA600, ("hhh", "")},
            {0xA800, ("hhh", "")},
            {0xA810, ("hhh", "")},
            {0xA900, ("hhh", "")},
            {0xB400, ("hhh", "")},
            {0xB800, ("hhh", "")},
            {0xB900, ("hhh", "")},
            {0xC400, ("hhh", "")},
            {0xC800, ("hhh", "")},
            {0xD400, ("hhh", "")},
            {0xD800, ("hhh", "")},
            {0xE400, ("hhh", "")},
            {0xE800, ("hhh", "")}
        };

        public static (string format, string name) GetOpcodeInfo(ushort opcode)
        {
            if (KnownOpcodes.TryGetValue(opcode, out var opcodeInfo))
                return opcodeInfo;

            // Use heuristic for unknown opcodes
            string format;
            ushort highNibble = (ushort)(opcode & 0xF000);
            
            if (highNibble == 0xF000)
                format = "hh";
            else if (highNibble == 0x0000)
                format = "";
            else
                format = "hhh";
            
            return (format, "");
        }
    }

    /// <summary>
    /// Main GSC format handler.
    /// </summary>
    [Export(typeof(ScriptFormat))]
    public sealed class GscFormat : ScriptFormat
    {
        public override string Tag => "GSC";
        public override string Description => "Liar-soft RScript scenario file";
        public override uint Signature => 0;
        public override ScriptType DataType => ScriptType.BinaryScript;

        public GscFormat()
        {
            Extensions = new[] { "gsc" };
        }

        public override bool IsScript(IBinaryStream file)
        {
            if (!file.Name.HasExtension(".gsc"))
                return false;
            
            if (file.Length < 0x24) // Minimum header size
                return false;
            
            file.Position = 0;
            uint declaredFileSize = file.ReadUInt32();
            uint declaredHeaderSize = file.ReadUInt32();
            
            // Basic sanity checks
            if (declaredFileSize > file.Length + 16) // Allow some padding
                return false;
            
            if (declaredHeaderSize < 0x14 || declaredHeaderSize > 0x100)
                return false;
            
            return true;
        }

        public override ScriptData Read(string name, Stream stream)
        {
            return Read(name, stream, Encodings.cp932);
        }

        public override ScriptData Read(string name, Stream stream, Encoding encoding)
        {
            var metadata = new GscScriptData { Encoding = encoding };
            
            using (var reader = new BinaryReader(stream, encoding, true))
            {
                // Read header fields
                metadata.FileSize = reader.ReadUInt32();
                metadata.HeaderSize = reader.ReadUInt32();
                metadata.CodeSectionSize = reader.ReadUInt32();
                metadata.StringIndexSize = reader.ReadUInt32();
                metadata.StringDataSize = reader.ReadUInt32();
                
                // Read unknown header fields
                for (int i = 0; i < 4; i++)
                {
                    metadata.UnknownHeaderValues[i] = reader.ReadUInt32();
                }

                // Calculate actual readable sizes based on stream length
                long streamLength = stream.Length;
                long currentPosition = stream.Position;
                
                // Read raw header (includes the fields we just read)
                stream.Position = 0;
                int headerBytesToRead = (int)Math.Min(metadata.HeaderSize, streamLength);
                metadata.RawHeaderData = reader.ReadBytes(headerBytesToRead);
                
                // Read code section
                stream.Position = metadata.HeaderSize;
                int codeBytesToRead = (int)Math.Min(metadata.CodeSectionSize, 
                    Math.Max(0, streamLength - stream.Position));
                metadata.RawCodeData = codeBytesToRead > 0 
                    ? reader.ReadBytes(codeBytesToRead) 
                    : new byte[0];
                
                // Read string index section
                long stringIndexPosition = metadata.HeaderSize + metadata.CodeSectionSize;
                if (stringIndexPosition < streamLength)
                {
                    stream.Position = stringIndexPosition;
                    int indexBytesToRead = (int)Math.Min(metadata.StringIndexSize,
                        Math.Max(0, streamLength - stream.Position));
                    metadata.RawStringIndexData = indexBytesToRead > 0
                        ? reader.ReadBytes(indexBytesToRead)
                        : new byte[0];
                }
                else
                {
                    metadata.RawStringIndexData = new byte[0];
                }
                
                // Read string data section
                long stringDataPosition = metadata.HeaderSize + metadata.CodeSectionSize + 
                                         metadata.StringIndexSize;
                if (stringDataPosition < streamLength)
                {
                    stream.Position = stringDataPosition;
                    int stringBytesToRead = (int)Math.Min(metadata.StringDataSize,
                        Math.Max(0, streamLength - stream.Position));
                    metadata.RawStringData = stringBytesToRead > 0
                        ? reader.ReadBytes(stringBytesToRead)
                        : new byte[0];
                }
                else
                {
                    metadata.RawStringData = new byte[0];
                }
                
                // Read footer (everything after the main sections)
                long expectedEndPosition = metadata.HeaderSize + metadata.CodeSectionSize +
                                          metadata.StringIndexSize + metadata.StringDataSize;
                if (expectedEndPosition < streamLength)
                {
                    stream.Position = expectedEndPosition;
                    int footerBytesToRead = (int)(streamLength - stream.Position);
                    metadata.RawFooterData = footerBytesToRead > 0
                        ? reader.ReadBytes(footerBytesToRead)
                        : new byte[0];
                }
                else
                {
                    metadata.RawFooterData = new byte[0];
                }
            }

            // Parse the sections
            metadata.ParsedCommands = ParseCodeSection(metadata.RawCodeData);
            ParseStringSection(metadata, encoding);
            
            // Store metadata
            metadata.Metadata["FileSize"] = metadata.FileSize;
            metadata.Metadata["CommandCount"] = metadata.ParsedCommands.Count;
            metadata.Metadata["StringCount"] = metadata.TextLines.Count;
            
            return metadata;
        }

        private List<GscCommand> ParseCodeSection(byte[] codeData)
        {
            var commands = new List<GscCommand>();
            
            if (codeData == null || codeData.Length < 2)
                return commands;
            
            using (var codeStream = new MemoryStream(codeData))
            using (var reader = new BinaryReader(codeStream))
            {
                while (codeStream.Position < codeStream.Length)
                {
                    long commandPosition = codeStream.Position;
                    
                    // Check if we have at least 2 bytes for opcode
                    if (codeStream.Length - codeStream.Position < 2)
                        break;
                    
                    ushort opcode = reader.ReadUInt16();
                    var (format, name) = GscOpcodeDatabase.GetOpcodeInfo(opcode);
                    
                    var command = new GscCommand
                    {
                        Opcode = opcode,
                        CommandName = name,
                        ArgumentFormat = format,
                        FilePosition = commandPosition
                    };
                    
                    // Parse arguments based on format string
                    bool readError = false;
                    foreach (char formatChar in format)
                    {
                        switch (formatChar)
                        {
                            case 'i':
                            case 'I':
                                if (codeStream.Position + 4 <= codeStream.Length)
                                    command.Arguments.Add(reader.ReadInt32());
                                else
                                    readError = true;
                                break;
                                
                            case 'h':
                            case 'H':
                                if (codeStream.Position + 2 <= codeStream.Length)
                                    command.Arguments.Add(reader.ReadInt16());
                                else
                                    readError = true;
                                break;
                        }
                        
                        if (readError)
                            break;
                    }
                    
                    commands.Add(command);
                    
                    if (readError)
                        break;
                }
            }
            
            return commands;
        }

        private void ParseStringSection(GscScriptData metadata, Encoding encoding)
        {
            if (metadata.RawStringIndexData == null || metadata.RawStringIndexData.Length == 0)
                return;
            
            using (var indexStream = new MemoryStream(metadata.RawStringIndexData))
            using (var indexReader = new BinaryReader(indexStream))
            using (var textStream = new MemoryStream(metadata.RawStringData ?? new byte[0]))
            {
                while (indexStream.Position < indexStream.Length)
                {
                    // Check if we have 4 bytes for offset
                    if (indexStream.Length - indexStream.Position < 4)
                        break;
                    
                    uint stringOffset = indexReader.ReadUInt32();
                    
                    // Check if offset is within text data
                    if (stringOffset >= textStream.Length)
                        break;
                    
                    // Read null-terminated string
                    textStream.Position = stringOffset;
                    var stringBytes = new List<byte>();
                    int byteValue;
                    
                    while ((byteValue = textStream.ReadByte()) != -1 && byteValue != 0)
                    {
                        stringBytes.Add((byte)byteValue);
                    }
                    
                    string text = encoding.GetString(stringBytes.ToArray());
                    metadata.TextLines.Add(new ScriptLine((uint)metadata.TextLines.Count, text));
                }
            }
        }

        public override void Write(Stream stream, ScriptData script)
        {
            var GscScriptData = script as GscScriptData;
            
            if (GscScriptData == null)
            {
                // Convert from generic ScriptData
                GscScriptData = new GscScriptData
                {
                    Encoding = script.Encoding ?? Encodings.cp932,
                    RawHeaderData = new byte[0],
                    RawCodeData = new byte[0],
                    RawFooterData = new byte[0]
                };
                
                // Copy text lines
                foreach (var line in script.TextLines)
                {
                    GscScriptData.TextLines.Add(line);
                }
            }
            
            // Build string sections if needed
            if (GscScriptData.RawStringIndexData == null || GscScriptData.RawStringData == null)
            {
                BuildStringSections(GscScriptData);
            }
            
            using (var writer = new BinaryWriter(stream, GscScriptData.Encoding ?? Encodings.cp932, true))
            {
                uint headerSize = (uint)(GscScriptData.RawHeaderData?.Length ?? 36);
                uint codeSize = (uint)(GscScriptData.RawCodeData?.Length ?? 0);
                uint indexSize = (uint)(GscScriptData.RawStringIndexData?.Length ?? 0);
                uint stringSize = (uint)(GscScriptData.RawStringData?.Length ?? 0);
                uint footerSize = (uint)(GscScriptData.RawFooterData?.Length ?? 0);
                uint totalSize = headerSize + codeSize + indexSize + stringSize + footerSize;
                
                // Write header
                writer.Write(totalSize);
                writer.Write(headerSize);
                writer.Write(codeSize);
                writer.Write(indexSize);
                writer.Write(stringSize);
                
                // Write unknown header values
                for (int i = 0; i < 4; i++)
                {
                    uint value = (i < GscScriptData.UnknownHeaderValues.Length) 
                        ? GscScriptData.UnknownHeaderValues[i] 
                        : 0;
                    writer.Write(value);
                }
                
                // Write sections
                if (GscScriptData.RawHeaderData != null && GscScriptData.RawHeaderData.Length > 36)
                {
                    // Write additional header data if present
                    writer.Write(GscScriptData.RawHeaderData, 36, GscScriptData.RawHeaderData.Length - 36);
                }
                
                if (GscScriptData.RawCodeData != null && GscScriptData.RawCodeData.Length > 0)
                {
                    writer.Write(GscScriptData.RawCodeData);
                }
                
                if (GscScriptData.RawStringIndexData != null && GscScriptData.RawStringIndexData.Length > 0)
                {
                    writer.Write(GscScriptData.RawStringIndexData);
                }
                
                if (GscScriptData.RawStringData != null && GscScriptData.RawStringData.Length > 0)
                {
                    writer.Write(GscScriptData.RawStringData);
                }
                
                if (GscScriptData.RawFooterData != null && GscScriptData.RawFooterData.Length > 0)
                {
                    writer.Write(GscScriptData.RawFooterData);
                }
            }
        }

        private void BuildStringSections(GscScriptData metadata)
        {
            var encoding = metadata.Encoding ?? Encodings.cp932;
            
            using (var indexStream = new MemoryStream())
            using (var textStream = new MemoryStream())
            using (var indexWriter = new BinaryWriter(indexStream))
            {
                uint currentOffset = 0;
                
                foreach (var line in metadata.TextLines)
                {
                    // Write offset to index
                    indexWriter.Write(currentOffset);
                    
                    // Write string data
                    byte[] textBytes = encoding.GetBytes(line.Text ?? string.Empty);
                    textStream.Write(textBytes, 0, textBytes.Length);
                    textStream.WriteByte(0); // Null terminator
                    
                    currentOffset += (uint)(textBytes.Length + 1);
                }
                
                metadata.RawStringIndexData = indexStream.ToArray();
                metadata.RawStringData = textStream.ToArray();
            }
        }

        public override Stream ConvertFrom(IBinaryStream file)
        {
            var script = Read(file.Name, file.AsStream);
            var GscScriptData = script as GscScriptData;

            var outputStream = new MemoryStream();
            using (var writer = new StreamWriter(outputStream, Encoding.UTF8, 1024, true))
            {
                writer.WriteLine($"# GSC Script: {Path.GetFileName(file.Name)}");
                writer.WriteLine($"# Commands: {GscScriptData?.ParsedCommands.Count ?? 0}");
                writer.WriteLine($"# Strings: {script.TextLines.Count}");
                writer.WriteLine();
                
                if (GscScriptData != null && GscScriptData.ParsedCommands.Count > 0)
                {
                    writer.WriteLine("# === Commands ===");
                    foreach (var command in GscScriptData.ParsedCommands)
                        writer.WriteLine(command.ToString());
                    writer.WriteLine();
                }
                
                if (script.TextLines.Count > 0)
                {
                    writer.WriteLine("# === Text ===");
                    int index = 0;
                    foreach (var line in script.TextLines)
                    {
                        writer.WriteLine($"[{index:D4}] {line.Text}");
                        index++;
                    }
                }
                
                writer.Flush();
                outputStream.Position = 0;
                return outputStream;
            }
        }

        public override Stream ConvertBack(IBinaryStream file)
        {
            var text = string.Empty;
            using (var reader = new StreamReader(file.AsStream, Encoding.UTF8))
            {
                text = reader.ReadToEnd();
            }
            
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var metadata = new GscScriptData();
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;
                
                // Extract text from formatted lines
                if (line.StartsWith("[") && line.Contains("]"))
                {
                    int endIndex = line.IndexOf(']');
                    if (endIndex > 0 && endIndex + 1 < line.Length)
                    {
                        string textContent = line.Substring(endIndex + 1).TrimStart();
                        metadata.TextLines.Add(new ScriptLine((uint)metadata.TextLines.Count, textContent));
                    }
                }
                else
                {
                    metadata.TextLines.Add(new ScriptLine((uint)metadata.TextLines.Count, line));
                }
            }
            
            using (var outputStream = new MemoryStream())
            {
                Write(outputStream, metadata);
                outputStream.Position = 0;
                return outputStream;
            }
        }
    }
}