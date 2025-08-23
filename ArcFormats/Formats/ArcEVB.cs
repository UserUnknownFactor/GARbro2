// credits: mos9527/evbunpack
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Xml.Linq;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.EVB
{
    [Export(typeof(ArchiveFormat))]
    public class EvbOpener : ArchiveFormat
    {
        public override string         Tag { get { return "EVB"; } }
        public override string Description { get { return "Enigma Virtual Box archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        const int   NODE_TYPE_FILE = 2;
        const int NODE_TYPE_FOLDER = 3;

        const string DEFAULT_ROOT = "%DEFAULT FOLDER%";

        public EvbOpener()
        {
            Extensions = new[] { "exe" };
        }

        static byte[] s_evb_signature = { 0x45, 0x56, 0x42, 0x00 }; // EVB\0
        static string NEW_ROOT = "";

        public override ArcFile TryOpen (ArcView file)
        {
            long evb_offset = ExeFile.AutoFindSignature (file, s_evb_signature);
            if (evb_offset == 0) // it can't be at the start
                return null;

            var dir = new List<Entry>();

            using (var input = file.CreateStream (evb_offset, file.MaxOffset - evb_offset))
            {
                // Skip magic and pack header
                input.Seek (64, SeekOrigin.Current);

                // Read main node
                var main_node = ReadMainNode (input);
                if (main_node == null)
                    return null;

                long abs_offset = input.Position + main_node.Value.Size - 12;
                input.Seek(-1, SeekOrigin.Current);

                // Try to detect if it's legacy format
                bool is_legacy = false;
                long test_pos = input.Position;
                try
                {
                    var test_header = ReadHeaderNode (input);
                    var test_named = ReadNamedNode (input);

                    if (test_named == null || test_header == null)
                        is_legacy = true;
                }
                catch
                {
                    is_legacy = true;
                }
                input.Position = test_pos;

                if (is_legacy)
                {
                    if (!ReadLegacyFileSystem (input, dir, evb_offset + 64))
                        return null;
                }
                else
                {
                    if (!ReadModernFileSystem (input, dir, main_node.Value.ObjectsCount, ref abs_offset, evb_offset))
                        return null;
                }
            }

            if (dir.Count == 0)
                return null;

            return new ArcFile (file, this, dir);
        }

        private bool ReadModernFileSystem (IBinaryStream stream, List<Entry> dir, uint maxObjects, ref long absOffset, long baseOffset)
        {
            for (uint i = 0; i < maxObjects; i++)
            {
                if (!ReadNodeRecursive (stream, dir, ref absOffset, baseOffset, ""))
                    break;
            }

            return dir.Count > 0;
        }

        private bool ReadNodeRecursive (IBinaryStream stream, List<Entry> dir, ref long absOffset, long baseOffset, string currentPath)
        {
            try
            {
                var headerNode = ReadHeaderNode (stream);
                var namedNode = ReadNamedNode (stream);

                if (headerNode == null || namedNode == null)
                    return false;

                string nodeName = namedNode.Value.Name.Replace (DEFAULT_ROOT, NEW_ROOT);

                if (namedNode.Value.Type == NODE_TYPE_FILE)
                {
                    var optionalNode = ReadOptionalFileNode (stream);
                    if (optionalNode != null)
                    {
                        var name = string.IsNullOrEmpty (currentPath) ? nodeName : VFS.CombinePath (currentPath, nodeName);
                        var entry = new PackedEntry
                        {
                            Name = name,
                            Type = FormatCatalog.Instance.GetTypeFromName (name),
                            Offset = baseOffset + absOffset,
                            Size = optionalNode.Value.StoredSize,
                            UnpackedSize = optionalNode.Value.OriginalSize,
                            IsPacked = optionalNode.Value.StoredSize != optionalNode.Value.OriginalSize
                        };
                        dir.Add (entry);
                        absOffset += optionalNode.Value.StoredSize;
                    }
                    return true;
                }
                else if (namedNode.Value.Type == NODE_TYPE_FOLDER)
                {
                    stream.Seek (25, SeekOrigin.Current); // Skip folder optional data

                    string folderPath;
                    if (string.IsNullOrEmpty (nodeName))  // %DEFAULT FOLDER%
                        folderPath = currentPath;
                    else
                        folderPath = string.IsNullOrEmpty (currentPath) ? nodeName : VFS.CombinePath (currentPath, nodeName);

                    // Recursively read all children of this folder
                    for (uint i = 0; i < headerNode.Value.ObjectsCount; i++)
                    {
                        if (!ReadNodeRecursive (stream, dir, ref absOffset, baseOffset, folderPath))
                            break;
                    }

                    return true;
                }
                else
                    return false;
            }
            catch
            {
                return false;
            }
        }

        private bool ReadLegacyFileSystem(IBinaryStream stream, List<Entry> dir, long baseOffset)
        {
            stream.Position = 0;
            stream.Seek(64, SeekOrigin.Current); // Skip header

            // Read main node to get the initial object count
            long mainNodePos = stream.Position;
            var mainHeader = ReadHeaderNode(stream);
            var mainNamed = ReadNamedNode(stream);

            if (mainHeader == null || mainNamed == null)
                return false;

            // Skip the main node's optional data
            stream.Position = mainNodePos + mainHeader.Value.Size + 4;

            // Process all children of the main node
            for (uint i = 0; i < mainHeader.Value.ObjectsCount; i++)
            {
                if (!ReadLegacyNodeRecursive(stream, dir, baseOffset, ""))
                    break;
            }

            return dir.Count > 0;
        }

        private bool ReadLegacyNodeRecursive(IBinaryStream stream, List<Entry> dir, long baseOffset, string currentPath)
        {
            long seekOrigin = stream.Position;

            try
            {
                var headerNode = ReadHeaderNode(stream);
                var namedNode = ReadNamedNode(stream);

                if (headerNode == null || namedNode == null)
                    return false;

                string nodeName = namedNode.Value.Name.Replace(DEFAULT_ROOT, NEW_ROOT);

                if (namedNode.Value.Type == NODE_TYPE_FILE)
                {
                    // Position to the optional file node (legacy format has different size)
                    stream.Position = seekOrigin + headerNode.Value.Size + 4 - 43;
                    var optionalNode = ReadLegacyOptionalFileNode(stream);

                    if (optionalNode != null)
                    {
                        var name = string.IsNullOrEmpty(currentPath) ? nodeName : VFS.CombinePath(currentPath, nodeName);
                        var entry = new PackedEntry
                        {
                            Name = name,
                            Type = FormatCatalog.Instance.GetTypeFromName(name),
                            Offset = baseOffset + stream.Position,
                            Size = optionalNode.Value.StoredSize,
                            UnpackedSize = optionalNode.Value.OriginalSize,
                            IsPacked = optionalNode.Value.StoredSize != optionalNode.Value.OriginalSize
                        };
                        dir.Add(entry);

                        // Skip the file data
                        stream.Seek(optionalNode.Value.StoredSize, SeekOrigin.Current);
                    }
                    return true;
                }
                else if (namedNode.Value.Type == NODE_TYPE_FOLDER)
                {
                    // Position after the folder node
                    stream.Position = seekOrigin + headerNode.Value.Size + 4;

                    // Build the new path for this folder
                    string folderPath;
                    if (string.IsNullOrEmpty(nodeName))  // %DEFAULT FOLDER%
                        folderPath = currentPath;
                    else
                        folderPath = string.IsNullOrEmpty(currentPath) ? nodeName : VFS.CombinePath(currentPath, nodeName);

                    // Recursively read all children of this folder
                    for (uint i = 0; i < headerNode.Value.ObjectsCount; i++)
                    {
                        if (!ReadLegacyNodeRecursive(stream, dir, baseOffset, folderPath))
                            break;
                    }

                    return true;
                }
                else
                    return false;
            }
            catch
            {
                return false;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);

            if (pent != null && pent.IsPacked)
            {
                // Handle compressed data with chunks
                using (var reader = new BinaryReader (input))
                {
                    uint chunkBlockSize = reader.ReadUInt32();
                    reader.ReadUInt32(); // Skip padding

                    var chunkSizes = new List<uint>();
                    long chunkDataSize = chunkBlockSize - 8;
                    long chunkDataRead = 0;

                    while (chunkDataRead < chunkDataSize)
                    {
                        uint chunkSize = reader.ReadUInt32();
                        chunkSizes.Add (chunkSize);
                        reader.ReadUInt32(); // Total size
                        reader.ReadUInt32(); // Padding
                        chunkDataRead += 12;
                    }

                    var output = new MemoryStream ((int)pent.UnpackedSize);

                    foreach (var chunkSize in chunkSizes)
                    {
                        var chunk = reader.ReadBytes ((int)chunkSize);
                        var decompressed = ApLib.Decompress (chunk);
                        output.Write (decompressed, 0, decompressed.Length);
                    }

                    output.Position = 0;
                    return output;
                }
            }

            return input;
        }

        private struct MainNode
        {
            public uint Size;
            public uint ObjectsCount;
        }

        private struct HeaderNode
        {
            public uint Size;
            public uint ObjectsCount;
        }

        private struct NamedNode
        {
            public string Name;
            public byte Type;
        }

        private struct OptionalFileNode
        {
            public uint OriginalSize;
            public uint StoredSize;
        }

        private MainNode? ReadMainNode (IBinaryStream stream)
        {
            try
            {
                var node = new MainNode {
                    Size = stream.ReadUInt32(),
                };
                stream.Seek (8, SeekOrigin.Current); // Skip padding
                node.ObjectsCount = stream.ReadUInt32();
                return node;
            }
            catch
            {
                return null;
            }
        }

        private HeaderNode? ReadHeaderNode (IBinaryStream stream)
        {
            try
            {
                var node = new HeaderNode {
                    Size = stream.ReadUInt32(),
                };
                stream.Seek (8, SeekOrigin.Current); // Skip padding
                node.ObjectsCount = stream.ReadUInt32();
                return node;
            }
            catch
            {
                return null;
            }
        }

        private NamedNode? ReadNamedNode (IBinaryStream stream)
        {
            try
            {
                var nameBytes = new List<byte>();

                while (true)
                {
                    byte b1 = stream.ReadUInt8();
                    byte b2 = stream.ReadUInt8();

                    if (b1 == 0 && b2 == 0)
                        break;

                    nameBytes.Add (b1);
                    nameBytes.Add (b2);
                }

                var node = new NamedNode {
                    Name = Encoding.Unicode.GetString (nameBytes.ToArray()),
                    Type = stream.ReadUInt8()
                };

                return node;
            }
            catch
            {
                return null;
            }
        }

        private OptionalFileNode? ReadOptionalFileNode (IBinaryStream stream)
        {
            try
            {
                stream.Seek (2, SeekOrigin.Current); // Skip padding

                var node = new OptionalFileNode {
                    OriginalSize = stream.ReadUInt32(),
                };

                stream.Seek (4,  SeekOrigin.Current); // Skip padding
                stream.Seek (24, SeekOrigin.Current); // Skip file times
                stream.Seek (15, SeekOrigin.Current); // Skip padding

                node.StoredSize = stream.ReadUInt32();

                return node;
            }
            catch
            {
                return null;
            }
        }

        private OptionalFileNode? ReadLegacyOptionalFileNode (IBinaryStream stream)
        {
            try
            {
                stream.Seek (2, SeekOrigin.Current); // Skip padding

                var node = new OptionalFileNode {
                    OriginalSize = stream.ReadUInt32(),
                };

                stream.Seek (4,  SeekOrigin.Current); // Skip padding
                stream.Seek (24, SeekOrigin.Current); // Skip file times
                stream.Seek (7,  SeekOrigin.Current); // Skip padding (different from modern)

                node.StoredSize = stream.ReadUInt32();
                stream.Seek (4,  SeekOrigin.Current); // Skip extra padding

                return node;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// aPLib decompressor ported from Python implementation
    /// </summary>
    public static class ApLib
    {
        public static byte[] Decompress (byte[] data, bool strict = true)
        {
            uint packedSize = 0;
            uint packedCrc = 0;
            uint origSize = 0;
            uint origCrc = 0;

            // Check for AP32 header
            if (data.Length >= 24 && data[0] == 'A' && data[1] == 'P' && data[2] == '3' && data[3] == '2')
            {
                using (var reader = new BinaryReader (new MemoryStream (data)))
                {
                    reader.BaseStream.Position = 4;
                    uint headerSize = reader.ReadUInt32();
                    packedSize      = reader.ReadUInt32();
                    packedCrc       = reader.ReadUInt32();
                    origSize        = reader.ReadUInt32();
                    origCrc         = reader.ReadUInt32();

                    // Extract actual data
                    byte[] newData = new byte[packedSize];
                    Array.Copy (data, headerSize, newData, 0, packedSize);
                    data = newData;
                }

                if (strict)
                {
                    if (packedSize != 0 && packedSize != data.Length)
                        throw new InvalidDataException ("Packed size is incorrect; got: {data.Length}, expected: {packedSize}");
                    //uint crc   = Crc32Reversed.UpdateCrc (0xFFFFFFFF, data, 0, data.Length);
                    uint ccrc  = Crc32Reversed.Compute (data);
                    if (packedCrc != 0 && packedCrc != ccrc)
                        throw new InvalidDataException ($"Incorrect packed CRC: {ccrc:X} ({ccrc}), expected: {packedCrc:X}");
                }
            }

            var decompressor = new ApLibDecompressor (data, strict);
            byte[] result = decompressor.Depack();

            if (strict)
            {
                if (origSize != 0 && origSize != result.Length)
                    throw new InvalidDataException ("Unpacked size is incorrect; got: {result.Length}, expected: {origSize}");
                    //uint crc   = Crc32Reversed.UpdateCrc (0xFFFFFFFF, result, 0, result.Length);
                    uint ccrc  = Crc32Reversed.Compute (result);
                if (origCrc != 0 && origCrc != ccrc)
                    throw new InvalidDataException ($"Incorrect unpacked CRC: {ccrc:X} ({ccrc}), expected: {origCrc:X}");
            }

            return result;
        }


    }

    internal class ApLibDecompressor
    {
        private readonly byte[] source;
        private readonly List<byte> destination;
        private int sourcePos;
        private int tag;
        private int bitcount;
        private readonly bool strict;

        public ApLibDecompressor (byte[] source, bool strict = true)
        {
            this.source      = source;
            this.destination = new List<byte>();
            this.sourcePos   = 0;
            this.tag         = 0;
            this.bitcount    = 0;
            this.strict      = strict;
        }

        private int GetBit()
        {
            // Check if tag is empty
            bitcount--;
            if (bitcount < 0)
            {
                // Load next tag
                if (sourcePos >= source.Length)
                {
                    if (strict)
                        throw new InvalidDataException ("Unexpected end of input");
                    return 0;
                }
                tag = source[sourcePos++];
                bitcount = 7;
            }

            // Shift bit out of tag
            int bit = (tag >> 7) & 1;
            tag <<= 1;

            return bit;
        }

        private int GetGamma()
        {
            int result = 1;

            // Input gamma2-encoded bits
            while (true)
            {
                result = (result << 1) + GetBit();
                if (GetBit() == 0)
                    break;
            }

            return result;
        }

        public byte[] Depack()
        {
            int r0 = -1;
            int lwm = 0;
            bool done = false;

            try
            {
                // First byte verbatim
                if (sourcePos < source.Length)
                    destination.Add (source[sourcePos++]);

                // Main decompression loop
                while (!done && sourcePos < source.Length)
                {
                    if (GetBit() == 1)
                    {
                        if (GetBit() == 1)
                        {
                            if (GetBit() == 1)
                            {
                                int offs = 0;
                                for (int i = 0; i < 4; i++)
                                {
                                    offs = (offs << 1) + GetBit();
                                }

                                if (offs != 0)
                                {
                                    if (destination.Count >= offs)
                                        destination.Add (destination[destination.Count - offs]);
                                    else
                                        destination.Add (0);
                                }
                                else
                                {
                                    destination.Add (0);
                                }

                                lwm = 0;
                            }
                            else
                            {
                                if (sourcePos >= source.Length)
                                    break;

                                int offs = source[sourcePos++];
                                int length = 2 + (offs & 1);
                                offs >>= 1;

                                if (offs != 0)
                                {
                                    for (int i = 0; i < length; i++)
                                    {
                                        if (destination.Count >= offs)
                                            destination.Add (destination[destination.Count - offs]);
                                        else
                                            destination.Add (0);
                                    }
                                }
                                else
                                {
                                    done = true;
                                }

                                r0 = offs;
                                lwm = 1;
                            }
                        }
                        else
                        {
                            int offs = GetGamma();

                            if (lwm == 0 && offs == 2)
                            {
                                offs = r0;
                                int length = GetGamma();

                                for (int i = 0; i < length; i++)
                                {
                                    if (destination.Count >= offs)
                                        destination.Add (destination[destination.Count - offs]);
                                    else
                                        destination.Add (0);
                                }
                            }
                            else
                            {
                                if (lwm == 0)
                                    offs -= 3;
                                else
                                    offs -= 2;

                                offs <<= 8;
                                if (sourcePos < source.Length)
                                    offs += source[sourcePos++];

                                int length = GetGamma();

                                if (offs >= 32000)
                                    length += 1;
                                if (offs >= 1280)
                                    length += 1;
                                if (offs < 128)
                                    length += 2;

                                for (int i = 0; i < length; i++)
                                {
                                    if (destination.Count >= offs)
                                        destination.Add (destination[destination.Count - offs]);
                                    else
                                        destination.Add (0);
                                }

                                r0 = offs;
                            }

                            lwm = 1;
                        }
                    }
                    else
                    {
                        if (sourcePos < source.Length)
                            destination.Add (source[sourcePos++]);
                        lwm = 0;
                    }
                }
            }
            catch (Exception)
            {
                if (strict)
                    throw new InvalidDataException ("aPLib decompression error");
            }

            return destination.ToArray();
        }
    }
}