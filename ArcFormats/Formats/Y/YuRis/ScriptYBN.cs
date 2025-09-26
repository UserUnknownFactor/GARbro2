using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;

namespace GameRes.Formats.Yuris
{
    [Export(typeof(ScriptFormat))]
    public class YstbScriptFormat : ScriptFormat
    {
        public override string          Tag { get { return "YSTB"; } }
        public override string  Description { get { return "YU-RIS script file"; } }
        public override uint      Signature { get { return 0x42545359; } } // 'YSTB'
        public override ScriptType DataType { get { return ScriptType.BinaryScript; } }

        // Known keys from various games
        private static readonly uint[] KNOWN_KEYS = new uint[]
        {
            0x96ac6fd3, // Default key
            0x00000000, // No encryption
            0x30731B78,
            0x6cfddadb,
        };

        public YstbScriptFormat()
        {
            Extensions = new[] { "ybn", "ystb" };
        }

        public override bool IsScript(IBinaryStream file)
        {
            if (file.Length < 0x20)
                return false;
            
            return file.Signature == Signature;
        }

        public override Stream ConvertFrom(IBinaryStream file)
        {
            var data = ReadFileData(file);
            var script = ParseYstb(data);
            
            if (script == null)
                return null;

            var output = new MemoryStream();
            using (var writer = new StreamWriter(output, Encoding.UTF8, 1024, true))
            {
                WriteScriptText(writer, script);
            }
            
            output.Position = 0;
            return output;
        }

        public override Stream ConvertBack(IBinaryStream file)
        {
            throw new NotSupportedException("YSTB script packing not implemented");
        }

        public override ScriptData Read(string name, Stream file)
        {
            return Read(name, file, Encoding.GetEncoding(932)); // Shift-JIS
        }

        public override ScriptData Read(string name, Stream file, Encoding encoding)
        {
            var data = ReadStreamData(file);
            var script = ParseYstb(data);
            
            if (script == null)
                return null;

            var sb = new StringBuilder();
            using (var writer = new StringWriter(sb))
            {
                WriteScriptText(writer, script);
            }

            var scriptData = new ScriptData(sb.ToString(), DataType) 
            { 
                Encoding = encoding 
            };
            
            if (script.Key != 0)
            {
                scriptData.Metadata["EncryptionKey"] = $"0x{script.Key:X8}";
            }

            return scriptData;
        }

        public override void Write(Stream file, ScriptData script)
        {
            throw new NotSupportedException("YSTB script writing not implemented");
        }

        private byte[] ReadFileData(IBinaryStream file)
        {
            var data = new byte[file.Length];
            file.Position = 0;
            file.Read(data, 0, data.Length);
            return data;
        }

        private byte[] ReadStreamData(Stream stream)
        {
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private YstbScript ParseYstb(byte[] data)
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                var script = new YstbScript();
                
                script.Magic = reader.ReadUInt32();
                if (script.Magic != Signature)
                    return null;
                
                script.Version = reader.ReadUInt32();
                Trace.WriteLine($"YSTB Version: {script.Version} (v{script.Version / 100.0:F2})");
                
                if (script.Version > 200 && script.Version < 300)
                {
                    return ParseYstbV2(reader, data);
                }
                else
                {
                    // Version >= 300 uses V5 format
                    return ParseYstbV5(reader, data);
                }
            }
        }

        private YstbScript ParseYstbV2(BinaryReader reader, byte[] data)
        {
            var script = new YstbScript();
            script.Magic = Signature;
            
            // Read header
            reader.BaseStream.Position = 0;
            var header = new YstbHeaderV2();
            header.Magic = reader.ReadUInt32();
            header.Version = reader.ReadUInt32();
            header.InstCount = reader.ReadUInt32();
            header.CodeSize = reader.ReadUInt32();
            header.ArgSize = reader.ReadUInt32();
            header.ResourceSize = reader.ReadUInt32();
            header.OffsetSize = reader.ReadUInt32();
            header.Reserved = reader.ReadUInt32();
            
            // Set version from header
            script.Version = header.Version;
            
            Trace.WriteLine($"YSTB V2: {header.InstCount} instructions, Code={header.CodeSize}, Args={header.ArgSize}, Res={header.ResourceSize}");
            
            // Auto-detect encryption key
            uint key = AutoDetectKeyV2(data, header);
            script.Key = key;
            
            // Decrypt if needed
            byte[] decryptedData = (byte[])data.Clone();
            if (key != 0)
            {
                DecryptSection(decryptedData, 0x20, header.CodeSize, key);
                DecryptSection(decryptedData, 0x20 + header.CodeSize, header.ArgSize, key);
                DecryptSection(decryptedData, 0x20 + header.CodeSize + header.ArgSize, header.ResourceSize, key);
                DecryptSection(decryptedData, 0x20 + header.CodeSize + header.ArgSize + header.ResourceSize, header.OffsetSize, key);
            }
            
            // Parse instructions
            using (var decReader = new BinaryReader(new MemoryStream(decryptedData)))
            {
                decReader.BaseStream.Position = 0x20;
                script.Instructions = new List<YstbInstruction>();
                
                var codeData = decReader.ReadBytes((int)header.CodeSize);
                var argData = decReader.ReadBytes((int)header.ArgSize);
                var resData = decReader.ReadBytes((int)header.ResourceSize);
                
                int codePos = 0;
                int argPos = 0;
                
                for (int i = 0; i < header.InstCount; i++)
                {
                    if (codePos + 4 > codeData.Length)
                        break;
                    
                    var inst = new YstbInstruction();
                    inst.Opcode = codeData[codePos];
                    inst.ArgCount = codeData[codePos + 1];
                    inst.Reserved = BitConverter.ToUInt16(codeData, codePos + 2);
                    codePos += 4;
                    
                    inst.Arguments = new List<YstbArgument>();
                    
                    for (int j = 0; j < inst.ArgCount; j++)
                    {
                        if (argPos + 12 > argData.Length)
                            break;
                            
                        var arg = new YstbArgument();
                        arg.Value = BitConverter.ToUInt16(argData, argPos);
                        arg.Type = BitConverter.ToUInt16(argData, argPos + 2);
                        arg.ResourceSize = BitConverter.ToUInt32(argData, argPos + 4);
                        arg.ResourceOffset = BitConverter.ToUInt32(argData, argPos + 8);
                        argPos += 12;
                        
                        if (arg.ResourceSize > 0 && arg.ResourceOffset < resData.Length)
                        {
                            arg.ResourceData = new byte[Math.Min(arg.ResourceSize, resData.Length - arg.ResourceOffset)];
                            Array.Copy(resData, arg.ResourceOffset, arg.ResourceData, 0, arg.ResourceData.Length);
                        }
                        
                        inst.Arguments.Add(arg);
                    }
                    
                    script.Instructions.Add(inst);
                }
                
                Trace.WriteLine($"YSTB: Parsed {script.Instructions.Count} instructions");
            }
            
            return script;
        }

        private YstbScript ParseYstbV5(BinaryReader reader, byte[] data)
        {
            var script = new YstbScript();
            script.Magic = Signature;
            
            // Read V5 header
            reader.BaseStream.Position = 0;
            var header = new YstbHeaderV5();
            header.Magic = reader.ReadUInt32();
            header.Version = reader.ReadUInt32();
            header.InstCount = reader.ReadUInt32();
            header.InstIndexSize = reader.ReadUInt32();
            header.ArgsIndexSize = reader.ReadUInt32();
            header.ArgsDataSize = reader.ReadUInt32();
            header.LineNumbersSize = reader.ReadUInt32();
            header.Reserved = reader.ReadUInt32();
            
            // Set version from header
            script.Version = header.Version;
            
            Trace.WriteLine($"YSTB V5: {header.InstCount} instructions");
            
            // Detect key for V5
            uint key = AutoDetectKeyV5(data, header);
            script.Key = key;
            
            // Decrypt if needed
            byte[] decryptedData = (byte[])data.Clone();
            if (key != 0)
            {
                uint offset = 0x20;
                DecryptSection(decryptedData, offset, header.InstIndexSize, key);
                offset += header.InstIndexSize;
                DecryptSection(decryptedData, offset, header.ArgsIndexSize, key);
                offset += header.ArgsIndexSize;
                DecryptSection(decryptedData, offset, header.ArgsDataSize, key);
                offset += header.ArgsDataSize;
                DecryptSection(decryptedData, offset, header.LineNumbersSize, key);
            }
            
            // Parse V5 instructions
            using (var decReader = new BinaryReader(new MemoryStream(decryptedData)))
            {
                decReader.BaseStream.Position = 0x20;
                script.Instructions = new List<YstbInstruction>();
                
                var instIndexData = decReader.ReadBytes((int)header.InstIndexSize);
                var argIndexData = decReader.ReadBytes((int)header.ArgsIndexSize);
                var argData = decReader.ReadBytes((int)header.ArgsDataSize);
                
                int instPos = 0;
                int argIndexPos = 0;
                
                for (int i = 0; i < header.InstCount; i++)
                {
                    if (instPos + 4 > instIndexData.Length)
                        break;
                    
                    var inst = new YstbInstruction();
                    inst.Opcode = instIndexData[instPos];
                    inst.ArgCount = instIndexData[instPos + 1];
                    inst.Reserved = BitConverter.ToUInt16(instIndexData, instPos + 2);
                    instPos += 4;
                    
                    inst.Arguments = new List<YstbArgument>();
                    
                    for (int j = 0; j < inst.ArgCount; j++)
                    {
                        if (argIndexPos + 12 > argIndexData.Length)
                            break;
                        
                        var arg = new YstbArgument();
                        arg.Value = BitConverter.ToUInt16(argIndexData, argIndexPos);
                        arg.Type = BitConverter.ToUInt16(argIndexData, argIndexPos + 2);
                        arg.ResourceSize = BitConverter.ToUInt32(argIndexData, argIndexPos + 4);
                        arg.ResourceOffset = BitConverter.ToUInt32(argIndexData, argIndexPos + 8);
                        argIndexPos += 12;
                        
                        if (arg.ResourceSize > 0 && arg.ResourceOffset < argData.Length)
                        {
                            arg.ResourceData = new byte[Math.Min(arg.ResourceSize, argData.Length - arg.ResourceOffset)];
                            Array.Copy(argData, arg.ResourceOffset, arg.ResourceData, 0, arg.ResourceData.Length);
                        }
                        
                        inst.Arguments.Add(arg);
                    }
                    
                    script.Instructions.Add(inst);
                }
                
                Trace.WriteLine($"YSTB V5: Parsed {script.Instructions.Count} instructions");
            }
            
            return script;
        }

        private uint AutoDetectKeyV2(byte[] data, YstbHeaderV2 header)
        {
            // V2 key detection: offset 0x2C contains first argument's offset field
            // which should decrypt to 0
            uint keyOffset = 0x2C;
            
            if (keyOffset + 4 <= data.Length)
            {
                uint encryptedOffset = BitConverter.ToUInt32(data, (int)keyOffset);
                // This value XOR key should equal 0
                // So the encrypted value IS the key
                uint key = encryptedOffset;
                if (ValidateKey(data, header, key))
                {
                    Trace.WriteLine($"YSTB V2: Found key 0x{key:X8}");
                    return key;
                }
            }

            foreach (var key in KNOWN_KEYS)
            {
                if (ValidateKey(data, header, key))
                {
                    Trace.WriteLine($"YSTB V2: Found known key 0x{key:X8}");
                    return key;
                }
            }

            Trace.WriteLine("YSTB V2: No encryption works");
            return 0;
        }

        private uint AutoDetectKeyV5(byte[] data, YstbHeaderV5 header)
        {
            // V5 key detection: read first argument's offset field
            // which should decrypt to 0
            uint argIndexOffset = 0x20 + header.InstIndexSize;
            uint keyOffset = argIndexOffset + 8; // First arg's offset field
            
            if (keyOffset + 4 <= data.Length)
            {
                uint encryptedOffset = BitConverter.ToUInt32(data, (int)keyOffset);
                // This value XOR key should equal 0
                // So the encrypted value IS the key
                uint key = encryptedOffset;
                
                Trace.WriteLine($"YSTB V5: Testing key from offset 0x{keyOffset:X8}: 0x{key:X8}");
                
                if (ValidateKeyV5(data, header, key))
                {
                    Trace.WriteLine($"YSTB V5: Found key 0x{key:X8}");
                    return key;
                }
            }

            foreach (var key in KNOWN_KEYS)
            {
                if (ValidateKeyV5(data, header, key))
                {
                    Trace.WriteLine($"YSTB V5: Found known key 0x{key:X8}");
                    return key;
                }
            }
            
            Trace.WriteLine("YSTB V5: No encryption works");
            return 0;
        }

        private bool ValidateKey(byte[] data, object header, uint key)
        {
            int instCount = 0;
            if (header is YstbHeaderV2 v2)
                instCount = (int)v2.InstCount;
            else if (header is YstbHeaderV5 v5)
                instCount = (int)v5.InstCount;
            
            byte[] keyBytes = BitConverter.GetBytes(key);
            uint offset = 0x20;
            int validCount = 0;
            int checkCount = Math.Min(20, instCount);
            
            for (int i = 0; i < checkCount; i++)
            {
                if (offset + 4 > data.Length)
                    break;

                byte opcode = (byte)(data[offset] ^ keyBytes[0]);
                byte argCount = (byte)(data[offset + 1] ^ keyBytes[1]);
                ushort reserved = (ushort)((data[offset + 2] ^ keyBytes[2]) | 
                                           ((data[offset + 3] ^ keyBytes[3]) << 8));

                // Validate
                if (opcode < 0xFF && argCount <= 20)
                    validCount++;

                offset += 4;
            }

            return validCount >= (checkCount * 0.9);
        }

        private bool ValidateKeyV5(byte[] data, YstbHeaderV5 header, uint key)
        {
            byte[] keyBytes = BitConverter.GetBytes(key);
            uint offset = 0x20;
            int validCount = 0;
            int checkCount = Math.Min(20, (int)header.InstCount);
            
            for (int i = 0; i < checkCount; i++)
            {
                if (offset + 4 > data.Length)
                    break;
                
                byte opcode = (byte)(data[offset] ^ keyBytes[0]);
                byte argCount = (byte)(data[offset + 1] ^ keyBytes[1]);
                ushort reserved = (ushort)((data[offset + 2] ^ keyBytes[2]) | 
                                           ((data[offset + 3] ^ keyBytes[3]) << 8));
                
                if (opcode < 0xFF && argCount <= 20)
                    validCount++;
                
                offset += 4;
            }
            
            return validCount >= (checkCount * 0.9);
        }

        private void DecryptSection(byte[] data, uint offset, uint size, uint key)
        {
            byte[] keyBytes = BitConverter.GetBytes(key);
            
            for (uint i = 0; i < size && offset + i < data.Length; i++)
            {
                data[offset + i] ^= keyBytes[i & 3];
            }
        }

        private void WriteScriptText(TextWriter writer, YstbScript script)
{
    writer.WriteLine($"# YSTB Script v{script.Version / 100.0:F2}");
    writer.WriteLine($"# Instructions: {script.Instructions.Count}");
    if (script.Key != 0)
    {
        writer.WriteLine($"# Encryption Key: 0x{script.Key:X8}");
    }
    writer.WriteLine();
    
    // First pass - collect all instructions that have text
    var textInstructions = new List<(int instIndex, YstbInstruction inst, List<(int argIndex, string text)> texts)>();
    
    for (int instIdx = 0; instIdx < script.Instructions.Count; instIdx++)
    {
        var inst = script.Instructions[instIdx];
        var textsInInst = new List<(int argIndex, string text)>();
        
        for (int argIdx = 0; argIdx < inst.Arguments.Count; argIdx++)
        {
            var arg = inst.Arguments[argIdx];
            
            if (arg.ResourceData != null && arg.ResourceData.Length > 0)
            {
                string text = ExtractText(arg.ResourceData);
                
                // Only include non-empty text
                if (!string.IsNullOrEmpty(text) && text.Length > 1)
                    textsInInst.Add((argIdx, text));
            }
        }
        
        // Only add instructions that have at least one text argument
        if (textsInInst.Count > 0)
            textInstructions.Add((instIdx, inst, textsInInst));
    }
    
    // Output instructions in sequence
    if (textInstructions.Count > 0)
    {
        int textCount = 0;
        
        foreach (var (instIdx, inst, texts) in textInstructions)
        {
            foreach (var (argIdx, text) in texts)
            {
                writer.WriteLine($"[{instIdx:D4} Op:{inst.Opcode:X2} Arg:{argIdx}]");
                writer.WriteLine(text);
                writer.WriteLine();
                textCount++;
            }
        }
    }
    else
    {
        writer.WriteLine("# No text entries found");
    }
}

        private string ExtractText(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;
            
            try
            {
                // Type 0x4D resource (common format for strings)
                if (data.Length > 3 && data[0] == 0x4D)
                {
                    ushort len = BitConverter.ToUInt16(data, 1);
                    if (len > 0 && len <= data.Length - 3)
                    {
                        // Extract the string data
                        var textBytes = new byte[len];
                        Array.Copy(data, 3, textBytes, 0, len);
                        
                        // Try to decode as CP932 (Shift-JIS)
                        string text = Encoding.GetEncoding(932).GetString(textBytes);
                        
                        // Clean up control characters
                        text = text.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\r");
                        
                        // Remove quotes if present
                        if (text.StartsWith("\"") && text.EndsWith("\""))
                            text = text.Substring(1, text.Length - 2);
                        
                        // Basic validation - should have mostly printable characters
                        int validChars = 0;
                        foreach (char c in text)
                        {
                            if (c >= 0x20 || c == '\t' || c == '\n' || c == '\r')
                                validChars++;
                        }
                        
                        if (validChars >= text.Length * 0.8)
                            return text;
                    }
                }
                
                // Check for raw text (Japanese text often starts with bytes >= 0x80)
                bool hasJapanese = false;
                bool hasAscii = false;
                
                for (int i = 0; i < Math.Min(data.Length, 20); i++)
                {
                    if (data[i] >= 0x80)
                        hasJapanese = true;
                    else if (data[i] >= 0x20 && data[i] < 0x7F)
                        hasAscii = true;
                }
                
                // If it has Japanese characters or looks like text
                if (hasJapanese || hasAscii)
                {
                    // Try to decode the entire buffer
                    string text = Encoding.GetEncoding(932).GetString(data).TrimEnd('\0');
                    
                    // Clean up control characters
                    text = text.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\r");
                    return text;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Text extraction error: {ex.Message}");
            }
            
            return string.Empty;
        }

        // Data structures
        private class YstbScript
        {
            public uint Magic { get; set; }
            public uint Version { get; set; }
            public uint Key { get; set; }
            public List<YstbInstruction> Instructions { get; set; }
        }

        private class YstbInstruction
        {
            public byte Opcode { get; set; }
            public byte ArgCount { get; set; }
            public ushort Reserved { get; set; }
            public List<YstbArgument> Arguments { get; set; }
        }

        private class YstbArgument
        {
            public ushort Value { get; set; }
            public ushort Type { get; set; }
            public uint ResourceSize { get; set; }
            public uint ResourceOffset { get; set; }
            public byte[] ResourceData { get; set; }
        }

        private class YstbHeaderV2
        {
            public uint Magic { get; set; }
            public uint Version { get; set; }
            public uint InstCount { get; set; }
            public uint CodeSize { get; set; }
            public uint ArgSize { get; set; }
            public uint ResourceSize { get; set; }
            public uint OffsetSize { get; set; }
            public uint Reserved { get; set; }
        }

        private class YstbHeaderV5
        {
            public uint Magic { get; set; }
            public uint Version { get; set; }
            public uint InstCount { get; set; }
            public uint InstIndexSize { get; set; }
            public uint ArgsIndexSize { get; set; }
            public uint ArgsDataSize { get; set; }
            public uint LineNumbersSize { get; set; }
            public uint Reserved { get; set; }
        }
    }
}