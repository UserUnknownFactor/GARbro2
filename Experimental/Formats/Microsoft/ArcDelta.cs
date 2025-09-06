using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.MSFormats
{
    public class DeltaEntry : Entry
    {
        public DeltaType DeltaType { get; set; }
        public string BaseFileName { get; set; }
        public string BaseFileHash { get; set; }
        public long   BaseFileSize { get; set; }
        public string   TargetHash { get; set; }
        public long     TargetSize { get; set; }
        public byte[]  DeltaHeader { get; set; }
        public bool       HasCrc32 { get; set; }
        public uint          Crc32 { get; set; }
    }

    public enum DeltaType
    {
        None = 0,
        PA30 = 0x30334150,  // Forward delta
        PA19 = 0x39314150,  // Reverse delta  
        PA31 = 0x31334150   // Null delta
    }

    [Export(typeof(ArchiveFormat))]
    [ExportMetadata("Priority", 2)]
    public class DeltaOpener : ArchiveFormat
    {
        public override string Tag { get { return "MS/DELTA"; } }
        public override string Description { get { return "Microsoft Delta Compression"; } }
        public override uint Signature { get { return 0; } }
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        // Windows system directories to search for base files
        private static readonly string[] SystemSearchPaths = new[]
        {
            Directory.GetCurrentDirectory(),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"),
        };

        // Cache for base files found in system
        private static readonly Dictionary<string, string> BaseFileCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new object();

        public DeltaOpener()
        {
            Signatures = new uint[] { 0x30334150, 0x39314150, 0x31334150 }; // PA30, PA19, PA31
        }

        public override ArcFile TryOpen(ArcView file)
        {
            var deltaInfo = AnalyzeDeltaFile(file);
            if (deltaInfo == null)
                return null;

            var dir = new List<Entry> { deltaInfo };
            return new DeltaArchive(file, this, dir, deltaInfo);
        }

        private DeltaEntry AnalyzeDeltaFile(ArcView file)
        {
            try
            {
                using (var stream = file.CreateStream())
                using (var reader = new BinaryReader(stream))
                {
                    // Check for CRC32 prefix
                    uint possibleCrc = 0;
                    bool hasCrc = false;
                    int deltaOffset = 0;

                    byte[] firstBytes = reader.ReadBytes(8);
                    if (firstBytes.Length < 8)
                        return null;

                    // Check if first 4 bytes are CRC32
                    if (IsDeltaSignature(firstBytes, 4))
                    {
                        possibleCrc = BitConverter.ToUInt32(firstBytes, 0);
                        hasCrc = true;
                        deltaOffset = 4;
                    }
                    else if (!IsDeltaSignature(firstBytes, 0))
                        return null;

                    stream.Position = deltaOffset;

                    // Read delta header
                    var header = ParseDeltaHeader(reader);
                    if (header == null)
                        return null;

                    // Try to determine base file from filename
                    string baseFileName = InferBaseFileName(file.Name);

                    var entry = new DeltaEntry
                    {
                        Name = Path.GetFileName(file.Name),
                        Type = "delta",
                        Offset = deltaOffset,
                        Size = (uint)(file.MaxOffset - deltaOffset),
                        DeltaType = header.DeltaType,
                        BaseFileName = baseFileName,
                        BaseFileSize = header.SourceSize,
                        TargetSize = header.TargetSize,
                        BaseFileHash = header.SourceHash,
                        TargetHash = header.TargetHash,
                        DeltaHeader = header.HeaderData,
                        HasCrc32 = hasCrc,
                        Crc32 = possibleCrc
                    };

                    return entry;
                }
            }
            catch
            {
                return null;
            }
        }

        private bool IsDeltaSignature(byte[] data, int offset)
        {
            if (offset + 3 >= data.Length)
                return false;

            return data[offset] == 'P' && data[offset + 1] == 'A' &&
                   ((data[offset + 2] == '3' && data[offset + 3] == '0') ||
                    (data[offset + 2] == '1' && data[offset + 3] == '9') ||
                    (data[offset + 2] == '3' && data[offset + 3] == '1'));
        }

        private class DeltaHeader
        {
            public DeltaType DeltaType { get; set; }
            public long SourceSize { get; set; }
            public long TargetSize { get; set; }
            public string SourceHash { get; set; }
            public string TargetHash { get; set; }
            public byte[] HeaderData { get; set; }
        }

        // from github.com/smilingthax/msdelta-pa30-format/blob/main/bitreader/bitreader.c
        public class BitReader
        {
            private byte[] buffer;
            private int position;
            private ulong value;
            private int fill;
            private int pad;

            public BitReader(byte[] data)
            {
                buffer = data;
                position = 0;
                value = 0;
                fill = 0;
                pad = 0;

                // Initialize - read 3-bit padding value
                uint padValue;
                if (ReadFast(3, out padValue))
                {
                    pad = (int)padValue;
                    if (position == buffer.Length && fill < pad)
                        throw new InvalidDataException("Invalid padding");
                    if (position == buffer.Length)
                        fill -= pad;
                }
            }

            private void Fill()
            {
                while (fill <= 56 && position < buffer.Length)
                {
                    value |= (ulong)buffer[position] << fill;
                    fill += 8;
                    position++;
                    if (position == buffer.Length)
                        fill -= pad;
                }
            }

            private bool ReadFast(int len, out uint result)
            {
                result = 0;
                if (len == 0)
                    return true;

                Fill();
                if (fill < len)
                    return false;

                result = (uint)(value & (~0u >> (32 - len)));
                value >>= len;
                fill -= len;
                return true;
            }

            // Count trailing zeros (like __builtin_ctz)
            private static int CountTrailingZeros(uint val)
            {
                if (val == 0) return 32;
                int count = 0;
                while ((val & 1) == 0)
                {
                    val >>= 1;
                    count++;
                }
                return count;
            }

            public uint ReadNumber32()
            {
                Fill();
                int nibbles = CountTrailingZeros((uint)value | 0x100);
                if (nibbles >= 8)
                    throw new InvalidDataException("Invalid number encoding");

                nibbles++;
                int bits = 4 * nibbles;

                if (fill < nibbles + bits)
                    throw new InvalidDataException("Not enough bits");

                uint result = (uint)((value >> nibbles) & (~0u >> (32 - bits)));
                value >>= nibbles + bits;
                fill -= nibbles + bits;

                return result;
            }

            public long ReadNumber64()
            {
                Fill();
                int nibbles = CountTrailingZeros((uint)value | 0x10000);
                if (nibbles >= 16)
                    throw new InvalidDataException("Invalid number64 encoding");

                nibbles++;

                if (fill < nibbles)
                    throw new InvalidDataException("Not enough bits");

                value >>= nibbles;
                fill -= nibbles;

                int bits = 4 * nibbles;
                if (fill < bits)
                {
                    Fill();
                    if (fill < bits)
                        throw new InvalidDataException("Not enough bits");
                }

                long result = (long)(value & (~0UL >> (64 - bits)));
                value >>= bits;
                fill -= bits;

                return result;
            }

            public ulong ReadNumberZ()
            {
                // For 64-bit systems, use ReadNumber64
                return (ulong)ReadNumber64();
            }

            public byte[] ReadBuffer()
            {
                var length = (int)ReadNumberZ();

                // Align to byte boundary
                int start = position - fill / 8;
                if (start + length > buffer.Length)
                    throw new InvalidDataException("Buffer too large");

                position = start + length;
                value = 0;
                fill = 0;

                var result = new byte[length];
                Array.Copy(buffer, start, result, 0, length);
                return result;
            }
        }

        private DeltaHeader ParseDeltaHeader(BinaryReader reader)
        {
            try
            {
                var startPos = reader.BaseStream.Position;
                var sig = reader.ReadUInt32();

                DeltaType deltaType;
                switch (sig)
                {
                    case 0x30334150: deltaType = DeltaType.PA30; break;
                    case 0x39314150: deltaType = DeltaType.PA19; break;
                    case 0x31334150: deltaType = DeltaType.PA31; break;
                    default: return null;
                }

                var header = new DeltaHeader
                {
                    DeltaType = deltaType,
                    HeaderData = null,
                    SourceSize = 0,
                    TargetSize = 0
                };

                if (deltaType == DeltaType.PA30)
                {
                    // Read 8-byte timestamp (TargetFileTime) - little endian
                    var targetFileTime = reader.ReadUInt64();

                    // Read remaining data for bitreader
                    var remainingSize = reader.BaseStream.Length - reader.BaseStream.Position;
                    var remainingData = reader.ReadBytes((int)remainingSize);

                    var bitReader = new BitReader(remainingData);
                    // Read fields in order as shown in dpa_GetDeltaInfo
                    var fileTypeSet = bitReader.ReadNumber64();
                    var fileType = bitReader.ReadNumber64();
                    var flags = bitReader.ReadNumber64();
                    header.TargetSize = (long)bitReader.ReadNumberZ(); // This is the target size!
                    var targetHashAlgId = bitReader.ReadNumber32();

                    // Read hash
                    var hashData = bitReader.ReadBuffer();
                    if (hashData != null && hashData.Length <= 32) // DPA_MAX_HASH_SIZE
                    {
                        header.TargetHash = BitConverter.ToString(hashData).Replace("-", "").ToLower();
                    }

                    // NOTE: Source size is not in the header for PA30
                    // It would need to be determined from the base file
                    header.SourceSize = 0;
                }
                else if (deltaType == DeltaType.PA19)
                {
                    // The C code mentions "fallback of PA19 files to legacy format"
                }
                else if (deltaType == DeltaType.PA31)
                {
                    // PA31 (null delta) format - similar structure to PA30
                    var targetFileTime = reader.ReadUInt64();
                    var remainingSize = reader.BaseStream.Length - reader.BaseStream.Position;
                    var remainingData = reader.ReadBytes((int)remainingSize);

                    var bitReader = new BitReader(remainingData);
                    var fileTypeSet = bitReader.ReadNumber64();
                    var fileType = bitReader.ReadNumber64();
                    var flags = bitReader.ReadNumber64();
                    header.TargetSize = (long)bitReader.ReadNumberZ();
                    var targetHashAlgId = bitReader.ReadNumber32();
                    var hashData = bitReader.ReadBuffer();
                    if (hashData != null && hashData.Length <= 32)
                        header.TargetHash = BitConverter.ToString(hashData).Replace("-", "").ToLower();
                }

                return header;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"ParseDeltaHeader error: {ex.Message}");
                return null;
            }
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var deltaArc = arc as DeltaArchive;
            var deltaEntry = entry as DeltaEntry;

            if (deltaArc == null || deltaEntry == null)
                return Stream.Null;

            // Try to apply delta if we have a base file
            var result = ApplyDelta(deltaArc, deltaEntry);
            if (result != null)
                return new MemoryStream(result, false);

            // Otherwise return the raw delta file
            return arc.File.CreateStream(entry.Offset, entry.Size);
        }

        private byte[] ApplyDelta(DeltaArchive arc, DeltaEntry entry)
        {
            try
            {
                // Find UpdateCompression.dll
                var dllPath = GetUpdateCompressionDllPath();
                if (string.IsNullOrEmpty(dllPath))
                {
                    System.Diagnostics.Trace.WriteLine("UpdateCompression.dll not found");
                    return null;
                }

                // Get base file
                byte[] baseData = null;

                if (!string.IsNullOrEmpty(entry.BaseFileName))
                {
                    baseData = GetBaseFile(entry.BaseFileName, entry.BaseFileHash);
                    if (baseData == null)
                    {
                        if (entry.DeltaType != DeltaType.PA31)
                        {
                            System.Diagnostics.Trace.WriteLine($"Base file not found: {entry.BaseFileName}");
                            return null;
                        }
                        // For null deltas, we can proceed without base
                    }
                }

                // Read delta data
                byte[] deltaData;
                using (var stream = arc.File.CreateStream(entry.Offset, entry.Size))
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    deltaData = ms.ToArray();
                }

                // Apply delta
                var result = UpdateCompression.ApplyDelta(baseData, deltaData, dllPath);

                // Verify result hash if available
                if (result != null && !string.IsNullOrEmpty(entry.TargetHash))
                {
                    using (var sha256 = SHA256.Create())
                    {
                        var hash = sha256.ComputeHash(result);
                        var hashStr = BitConverter.ToString(hash).Replace("-", "").ToLower();

                        if (!hashStr.Equals(entry.TargetHash, StringComparison.OrdinalIgnoreCase))
                        {
                            System.Diagnostics.Trace.WriteLine($"Hash mismatch: expected {entry.TargetHash}, got {hashStr}");
                            // Continue anyway - hash might be in different format
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"ApplyDelta error: {ex.Message}");
                return null;
            }
        }

        // Public method for MSU handler to use
        public static byte[] ApplyDeltaFile(string deltaPath, byte[] baseData = null)
        {
            try
            {
                var opener = new DeltaOpener();

                using (var deltaView = VFS.OpenView(deltaPath))
                {
                    var deltaInfo = opener.AnalyzeDeltaFile(deltaView);
                    if (deltaInfo == null)
                        return null;

                    // If no base data provided, try to find it
                    if (baseData == null && !string.IsNullOrEmpty(deltaInfo.BaseFileName))
                    {
                        baseData = GetBaseFile(deltaInfo.BaseFileName, deltaInfo.BaseFileHash);
                    }

                    var dllPath = GetUpdateCompressionDllPath();
                    if (string.IsNullOrEmpty(dllPath))
                        return null;

                    byte[] deltaData;
                    using (var stream = deltaView.CreateStream(deltaInfo.Offset, deltaInfo.Size))
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        deltaData = ms.ToArray();
                    }

                    return UpdateCompression.ApplyDelta(baseData, deltaData, dllPath);
                }
            }
            catch
            {
                return null;
            }
        }

        // Search for base file in Windows directories
        public static byte[] GetBaseFile(string fileName, string expectedHash = null)
        {
            lock (CacheLock)
            {
                // Check cache first
                if (BaseFileCache.TryGetValue(fileName, out var cachedPath))
                {
                    if (File.Exists(cachedPath))
                    {
                        try
                        {
                            var data = File.ReadAllBytes(cachedPath);
                            if (VerifyHash(data, expectedHash))
                                return data;
                        }
                        catch { }
                    }
                    BaseFileCache.Remove(fileName);
                }

                // Search system directories
                foreach (var searchPath in SystemSearchPaths)
                {
                    if (!Directory.Exists(searchPath))
                        continue;

                    try
                    {
                        // Direct file check
                        var directPath = Path.Combine(searchPath, fileName);
                        if (File.Exists(directPath))
                        {
                            var data = File.ReadAllBytes(directPath);
                            if (VerifyHash(data, expectedHash))
                            {
                                BaseFileCache[fileName] = directPath;
                                System.Diagnostics.Trace.WriteLine($"Found base file: {directPath}");
                                return data;
                            }
                        }

                        // Search subdirectories for WinSxS
                        if (searchPath.Contains("WinSxS"))
                        {
                            var matches = Directory.GetFiles(searchPath, fileName, SearchOption.AllDirectories)
                                                  .Take(10); // Limit search

                            foreach (var match in matches)
                            {
                                try
                                {
                                    var data = File.ReadAllBytes(match);
                                    if (VerifyHash(data, expectedHash))
                                    {
                                        BaseFileCache[fileName] = match;
                                        System.Diagnostics.Trace.WriteLine($"Found base file: {match}");
                                        return data;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                System.Diagnostics.Trace.WriteLine($"Base file not found in system: {fileName}");
                return null;
            }
        }

        private static bool VerifyHash(byte[] data, string expectedHash)
        {
            if (string.IsNullOrEmpty(expectedHash))
                return true; // No hash to verify

            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(data);
                var hashStr = BitConverter.ToString(hash).Replace("-", "").ToLower();
                return hashStr.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
            }
        }

        private string InferBaseFileName(string deltaFileName)
        {
            var name = Path.GetFileName(deltaFileName);

            // Remove common delta extensions
            foreach (var ext in new[] { ".p_", ".delta", ".patch", "_delta", "_patch" })
            {
                if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - ext.Length);
                    break;
                }
            }

            // Handle compressed extensions (e.g., file.dl_ -> file.dll)
            if (name.Length > 2 && name[name.Length - 1] == '_')
            {
                var lastDot = name.LastIndexOf('.');
                if (lastDot > 0 && lastDot < name.Length - 2)
                {
                    var ext = name.Substring(lastDot + 1, name.Length - lastDot - 2);
                    name = name.Substring(0, lastDot + 1) + ext + GetExpandedChar(name[name.Length - 2]);
                }
            }

            return name;
        }

        private char GetExpandedChar(char compressed)
        {
            // Handle compressed extension mapping
            switch (compressed)
            {
                case 'l': return 'l'; // .dl_ -> .dll
                case 'x': return 'x'; // .ex_ -> .exe
                case 'y': return 'y'; // .sy_ -> .sys
                default: return compressed;
            }
        }

        internal static string GetUpdateCompressionDllPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dllName = "UpdateCompression.dll";

            var possiblePaths = new[]
            {
                Path.Combine(baseDir, "x64", dllName),
                Path.Combine(baseDir, dllName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), dllName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), dllName),
            };

            return possiblePaths.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
        }

        // Public helper for MSU handler to detect delta files
        public static bool IsDeltaFile(byte[] data)
        {
            if (data == null || data.Length < 4)
                return false;

            // Check with and without CRC32
            for (int offset = 0; offset <= 4 && offset < data.Length - 3; offset += 4)
            {
                if (data[offset] == 'P' && data[offset + 1] == 'A' &&
                    ((data[offset + 2] == '3' && data[offset + 3] == '0') ||
                     (data[offset + 2] == '1' && data[offset + 3] == '9') ||
                     (data[offset + 2] == '3' && data[offset + 3] == '1')))
                {
                    return true;
                }
            }
            return false;
        }

        // Public helper for MSU handler
        // Public helper for MSU handler
        public static DeltaInfo GetDeltaInfo(byte[] deltaData)
        {
            try
            {
                // Check for CRC32 prefix and skip it if present
                int offset = 0;
                if (deltaData.Length >= 8)
                {
                    // Check if signature is at offset 4 (meaning CRC32 prefix)
                    if (deltaData[4] == 'P' && deltaData[5] == 'A' &&
                        ((deltaData[6] == '3' && deltaData[7] == '0') ||
                         (deltaData[6] == '1' && deltaData[7] == '9') ||
                         (deltaData[6] == '3' && deltaData[7] == '1')))
                    {
                        offset = 4; // Skip CRC32
                    }
                }

                using (var ms = new MemoryStream(deltaData, offset, deltaData.Length - offset))
                using (var reader = new BinaryReader(ms))
                {
                    var opener = new DeltaOpener();
                    var header = opener.ParseDeltaHeader(reader);

                    if (header != null)
                    {
                        return new DeltaInfo
                        {
                            Type = header.DeltaType,
                            SourceSize = header.SourceSize,
                            TargetSize = header.TargetSize,
                            SourceHash = header.SourceHash,
                            TargetHash = header.TargetHash
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"GetDeltaInfo error: {ex.Message}");
            }

            return null;
        }
    }

    internal class DeltaArchive : ArcFile
    {
        public DeltaEntry DeltaInfo { get; }

        public DeltaArchive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, DeltaEntry deltaInfo)
            : base(arc, impl, dir)
        {
            DeltaInfo = deltaInfo;
        }
    }

    public class DeltaInfo
    {
        public DeltaType Type { get; set; }
        public long SourceSize { get; set; }
        public long TargetSize { get; set; }
        public string SourceHash { get; set; }
        public string TargetHash { get; set; }
    }

    internal static class UpdateCompression
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DELTA_INPUT
        {
            public IntPtr lpcStart;
            public IntPtr uSize;
            public int Editable;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DELTA_OUTPUT
        {
            public IntPtr lpStart;
            public IntPtr uSize;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string dllToLoad);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        private delegate bool ApplyDeltaBDelegate(
            int ApplyFlags,
            ref DELTA_INPUT Source,
            ref DELTA_INPUT Delta,
            ref DELTA_OUTPUT lpTarget
        );

        private delegate bool DeltaFreeDelegate(IntPtr Buffer);

        public static byte[] ApplyDelta(byte[] source, byte[] delta, string updateCompressionDllPath)
        {
            if (string.IsNullOrEmpty(updateCompressionDllPath) || !File.Exists(updateCompressionDllPath))
                return null;

            // Check if delta has CRC32 prefix
            if (delta.Length >= 8)
            {
                for (var i = 0; i < 2; i++)
                {
                    var offset = i * 4;
                    if (offset + 3 < delta.Length &&
                        delta[offset] == 'P' && delta[offset + 1] == 'A' &&
                        (delta[offset + 2] == '3' && delta[offset + 3] == '0' ||
                         delta[offset + 2] == '1' && delta[offset + 3] == '9' ||
                         delta[offset + 2] == '3' && delta[offset + 3] == '1'))
                    {
                        if (i == 1)
                        {
                            uint crc = BitConverter.ToUInt32(delta, 0);
                            // Verify CRC if needed
                            var newDelta = new byte[delta.Length - 4];
                            Array.Copy(delta, 4, newDelta, 0, newDelta.Length);
                            delta = newDelta;
                        }
                        break;
                    }
                }
            }

            IntPtr hModule = IntPtr.Zero;
            IntPtr deltaBuffer = IntPtr.Zero;
            IntPtr sourceBuffer = IntPtr.Zero;

            try
            {
                hModule = LoadLibrary(updateCompressionDllPath);
                if (hModule == IntPtr.Zero)
                    return null;

                var applyPtr = GetProcAddress(hModule, "ApplyDeltaB");
                var freePtr = GetProcAddress(hModule, "DeltaFree");

                if (applyPtr == IntPtr.Zero || freePtr == IntPtr.Zero)
                    return null;

                var applyDeltaB = Marshal.GetDelegateForFunctionPointer<ApplyDeltaBDelegate>(applyPtr);
                var deltaFree = Marshal.GetDelegateForFunctionPointer<DeltaFreeDelegate>(freePtr);

                var deltaInput = new DELTA_INPUT();
                var sourceInput = new DELTA_INPUT();
                var output = new DELTA_OUTPUT();

                deltaBuffer = Marshal.AllocHGlobal(delta.Length);
                Marshal.Copy(delta, 0, deltaBuffer, delta.Length);

                deltaInput.lpcStart = deltaBuffer;
                deltaInput.uSize = (IntPtr)delta.Length;
                deltaInput.Editable = 0;

                if (source != null && source.Length > 0)
                {
                    sourceBuffer = Marshal.AllocHGlobal(source.Length);
                    Marshal.Copy(source, 0, sourceBuffer, source.Length);
                    sourceInput.lpcStart = sourceBuffer;
                    sourceInput.uSize = (IntPtr)source.Length;
                }
                else
                {
                    sourceInput.lpcStart = IntPtr.Zero;
                    sourceInput.uSize = IntPtr.Zero;
                }
                sourceInput.Editable = 0;

                bool result = applyDeltaB(0, ref sourceInput, ref deltaInput, ref output);

                if (result && output.lpStart != IntPtr.Zero)
                {
                    var outputData = new byte[(int)output.uSize];
                    Marshal.Copy(output.lpStart, outputData, 0, outputData.Length);
                    deltaFree(output.lpStart);
                    return outputData;
                }
            }
            finally
            {
                if (deltaBuffer != IntPtr.Zero) Marshal.FreeHGlobal(deltaBuffer);
                if (sourceBuffer != IntPtr.Zero) Marshal.FreeHGlobal(sourceBuffer);
                if (hModule != IntPtr.Zero) FreeLibrary(hModule);
            }

            return null;
        }
    }
}