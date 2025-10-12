using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using GameRes.Compression;
using GameRes.Formats.Properties;

namespace GameRes.Formats.AliceSoft
{
    [Export(typeof(ArchiveFormat))]
    public class AfaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AFA"; } }
        public override string Description { get { return "AliceSoft System 4 resource archive"; } }
        public override uint     Signature { get { return  0x48414641; } } // 'AFAH'
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        private const int MaxFileNameLength = 256;
        private const int HeaderSize = 44;

        private static readonly byte[] AffKey = {
            0xC8, 0xBB, 0x8F, 0xB7, 0xED, 0x43, 0x99, 0x4A,
            0xA2, 0x7E, 0x5B, 0xB0, 0x68, 0x18, 0xF8, 0x88
        };

        internal readonly EncodingSetting AfaEncoding;
        internal Encoding NameEncoding => AfaEncoding.Get<Encoding>();

        public AfaOpener()
        {
            ContainedFormats = new[] { "QNT", "PMS", "AJP", "PNG", "DCF", "OGG", "FLAT", "PACTEX" };
            AfaEncoding = new EncodingSetting ("AFAEncodingCP", "DefaultEncoding");
            Settings = new[] { AfaEncoding };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset < HeaderSize)
                return null;

            if (!file.View.AsciiEqual (8, "AlicArch"))
            {
                if (file.View.ReadInt32 (8) == 3)
                    return TryOpenV3 (file);
                return null;
            }

            return TryOpenV1or2 (file);
        }

        private ArcFile TryOpenV1or2 (ArcView file)
        {
            if (!file.View.AsciiEqual (0x1C, "INFO"))
                return null;

            var header = new AfaHeader {
                Version          = file.View.ReadInt32  (0x10),
                DataOffset       = file.View.ReadUInt32 (0x18),
                CompressedSize   = file.View.ReadUInt32 (0x20),
                UncompressedSize = file.View.ReadInt32  (0x24),
                FileCount        = file.View.ReadInt32  (0x28)
            };

            if (!ValidateHeader (header, file.MaxOffset))
                return null;

            var dir = ReadDirectoryV1or2 (file, header);
            return dir != null ? new ArcFile (file, this, dir) : null;
        }

        private List<Entry> ReadDirectoryV1or2 (ArcView file, AfaHeader header)
        {
            var encoding = NameEncoding;
            var dir = new List<Entry>(header.FileCount);
            var nameBuffer = new byte[MaxFileNameLength];

            using (var input = file.CreateStream (0x2C, header.CompressedSize))
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
            using (var index = new BinaryReader (zstream))
            {
                for (int i = 0; i < header.FileCount; i++)
                {
                    var entry = ReadEntryV1or2 (index, header, nameBuffer, encoding);
                    if (entry == null || !entry.CheckPlacement (file.MaxOffset))
                        return null;

                    entry.Offset += header.DataOffset;
                    dir.Add (entry);
                }
            }
            return dir;
        }

        private Entry ReadEntryV1or2 (BinaryReader reader, AfaHeader header, byte[] nameBuffer, Encoding encoding)
        {
            int nameLength = reader.ReadInt32();
            int indexStep = reader.ReadInt32();

            if (!ValidateNameLength (nameLength, indexStep, header.UncompressedSize))
                return null;

            if (indexStep > nameBuffer.Length)
                nameBuffer = new byte[indexStep];

            if (reader.Read (nameBuffer, 0, indexStep) != indexStep)
                return null;

            var name = encoding.GetString (nameBuffer, 0, nameLength);
            var entry = FormatCatalog.Instance.Create<Entry>(name);

            reader.ReadInt32(); // unknown0
            reader.ReadInt32(); // unknown1

            if (header.Version < 2)
                reader.ReadInt32(); // additional field in v1

            entry.Offset = reader.ReadUInt32();
            entry.Size = reader.ReadUInt32();

            return entry;
        }

        private ArcFile TryOpenV3 (ArcView file)
        {
            if (file.View.ReadInt32 (8) != 3)
                return null;

            uint indexSize = file.View.ReadUInt32 (4);
            if (indexSize > file.MaxOffset - 8)
                return null;

            var reader = new AfaV3IndexReader (file, indexSize);
            var dir = reader.Read();

            return dir?.Count > 0 ? new ArcFile (file, this, dir) : null;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Size <= 0x10 || !arc.File.View.AsciiEqual (entry.Offset, "AFF\0"))
                return base.OpenEntry (arc, entry);

            return DecryptAffEntry (arc, entry);
        }

        private Stream DecryptAffEntry (ArcFile arc, Entry entry)
        {
            long dataSize = entry.Size - 0x10u;
            long encryptedLength = Math.Min (0x40u, dataSize);

            var prefix = arc.File.View.ReadBytes (entry.Offset + 0x10, encryptedLength);
            for (int i = 0; i < prefix.Length; i++)
                prefix[i] ^= AffKey[i & 0xF];

            if (dataSize <= 0x40)
                return new BinMemoryStream (prefix, entry.Name);

            var rest = arc.File.CreateStream (entry.Offset + 0x10 + encryptedLength,
                                             dataSize - encryptedLength);
            return new PrefixStream (prefix, rest);
        }

        public override ResourceOptions GetDefaultOptions()
        {
            return new AfaOptions
            {
                Version = Properties.Settings.Default.AFAVersion,
                EncryptAff = Properties.Settings.Default.AFAEncryptAff,
                CompressionLevel = Properties.Settings.Default.AFACompressionLevel
            };
        }

        public override object GetCreationWidget()
        {
            return new GUI.WidgetAFA();
        }

        public override void Create(
            Stream output, IEnumerable<Entry> list, ResourceOptions options,
            EntryCallback callback)
        {
            var afa_options = GetOptions<AfaOptions>(options);
            var encoding = NameEncoding;

            var entries = list.ToList();
            if (entries.Count == 0)
                throw new InvalidFormatException ("Archive cannot be empty");

            int version = afa_options.Version;
            if (version < 1 || version > 3)
                throw new InvalidFormatException ("Invalid AFA version specified");

            if (version == 3)
                CreateV3 (output, entries, afa_options, encoding, callback);
            else
                CreateV1 (output, entries, afa_options, encoding, callback);
        }

        void CreateV1 (Stream output, List<Entry> entries, AfaOptions options,
                        Encoding encoding, EntryCallback callback)
        {
            // Build index
            var index_stream = new MemoryStream();
            using (var writer = new BinaryWriter (index_stream, encoding, true))
            {
                uint data_offset = 8; // DATA size

                foreach (var entry in entries)
                {
                    var name_bytes = encoding.GetBytes (entry.Name);
                    int name_length = name_bytes.Length;
                    int aligned_length = (name_length + 3) & ~3;

                    writer.Write (name_length);
                    writer.Write (aligned_length);
                    writer.Write (name_bytes);
                    if (aligned_length > name_length)
                        writer.Write (new byte[aligned_length - name_length]);

                    writer.Write (0); // unknown0
                    writer.Write (0); // unknown1

                    if (options.Version < 2)
                        writer.Write (0); // additional field in v1

                    writer.Write (data_offset);
                    writer.Write ((uint)entry.Size);

                    data_offset += (uint)entry.Size;
                }
            }

            // Compress index
            var index_data = index_stream.ToArray();
            var compressed = new MemoryStream();
            using (var zstream = new ZLibStream (compressed, CompressionMode.Compress,
                                               (CompressionLevel)options.CompressionLevel))
            {
                zstream.Write (index_data, 0, index_data.Length);
            }
            var compressed_data = compressed.ToArray();

            uint header_size = 0x2C;
            uint data_start = (uint)((header_size + compressed_data.Length + 0xF) & ~0xF);

            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                writer.Write (Signature);
                writer.Write (0x1C);
                writer.Write (Encoding.ASCII.GetBytes ("AlicArch"));
                writer.Write (options.Version);
                writer.Write (1); // unknown
                writer.Write (data_start);
                writer.Write (Encoding.ASCII.GetBytes ("INFO"));
                writer.Write ((uint)compressed_data.Length);
                writer.Write (index_data.Length);
                writer.Write (entries.Count);

                writer.Write (compressed_data);

                // Padding
                var padding = data_start - (uint)output.Position;
                if (padding > 0)
                    writer.Write (new byte[padding]);

                writer.Write (Encoding.ASCII.GetBytes ("DATA"));
                long data_size_pos = output.Position;
                writer.Write (0u); // placeholder

                // Write files
                uint total_size = 0;
                for (int i = 0; i < entries.Count; ++i)
                {
                    var entry = entries[i];
                    if (null != callback)
                        callback (i, entry, Localization._T ("MsgAddingFile"));

                    using (var input = File.OpenRead (entry.Name))
                    {
                        var size = input.Length;
                        if (size > uint.MaxValue)
                            throw new FileSizeException();

                        if (options.EncryptAff && IsAffFile (entry.Name))
                        {
                            total_size += WriteAffFile (writer, input, (uint)size);
                        }
                        else
                        {
                            input.CopyTo (output);
                            total_size += (uint)size;
                        }
                    }
                }

                // Update data size
                long end_pos = output.Position;
                output.Position = data_size_pos;
                writer.Write (total_size);
                output.Position = end_pos;
            }
        }

        void CreateV3 (Stream output, List<Entry> entries, AfaOptions options,
                      Encoding encoding, EntryCallback callback)
        {
            var index_writer = new AfaV3IndexWriter();
            var index_data = index_writer.BuildIndex (entries, encoding, options.CompressionLevel);

            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                writer.Write (Signature);
                writer.Write ((uint)index_data.Length);
                writer.Write (3); // version

                writer.Write (index_data);

                // Files
                for (int i = 0; i < entries.Count; ++i)
                {
                    var entry = entries[i];
                    if (null != callback)
                        callback (i, entry, Localization._T ("MsgAddingFile"));

                    using (var input = File.OpenRead (entry.Name))
                    {
                        if (options.EncryptAff && IsAffFile (entry.Name))
                            WriteAffFile (writer, input, (uint)input.Length);
                        else
                            input.CopyTo (output);
                    }
                }
            }
        }

        bool IsAffFile (string name)
        {
            return VFS.IsPathEqualsToFileName (name, "default.aff") ||
                   name.EndsWith (".aff", StringComparison.OrdinalIgnoreCase);
        }

        uint WriteAffFile (BinaryWriter writer, Stream input, uint size)
        {
            writer.Write (Encoding.ASCII.GetBytes ("AFF\0"));
            writer.Write (0L); // 8 bytes padding
            writer.Write (0);  // 4 bytes padding

            var buffer = new byte[Math.Min (0x40, size)];
            int read = input.Read (buffer, 0, buffer.Length);

            for (int i = 0; i < read; ++i)
                buffer[i] ^= AffKey[i & 0xF];

            writer.Write (buffer, 0, read);

            input.CopyTo (writer.BaseStream);

            return size + 0x10; // AFF header adds 16 bytes
        }

        [MethodImpl (MethodImplOptions.AggressiveInlining)]
        private static bool ValidateHeader (AfaHeader header, long maxOffset)
        {
            return header.FileCount > 0 && header.FileCount <= 0x10000 &&
                   header.CompressedSize >= 0 && header.UncompressedSize >= 0 &&
                   header.CompressedSize < maxOffset &&
                   header.DataOffset < maxOffset;
        }

        [MethodImpl (MethodImplOptions.AggressiveInlining)]
        private static bool ValidateNameLength (int nameLength, int indexStep, int maxSize)
        {
            return nameLength > 0 &&
                   nameLength <= MaxFileNameLength &&
                   nameLength <= indexStep &&
                   indexStep  <= maxSize;
        }

        private struct AfaHeader
        {
            public  int Version;
            public uint DataOffset;
            public uint CompressedSize;
            public  int UncompressedSize;
            public  int FileCount;
        }
    }

    public class AfaOptions : ResourceOptions
    {
        public int      Version { get; set; }
        public bool     EncryptAff { get; set; }
        public int      CompressionLevel { get; set; }
    }

    internal sealed class AfaV3IndexReader
    {
        private readonly ArcView _file;
        private readonly uint _dataOffset;
        private byte[] _dictionary;
        private readonly MersenneTwisterLike _rng;

        public AfaV3IndexReader (ArcView file, uint indexSize)
        {
            _file = file;
            _dataOffset = indexSize + 8;
            _rng = new MersenneTwisterLike();
        }

        public List<Entry> Read()
        {
            try
            {
                byte[] packed = ReadCompressedIndex();
                if (packed == null)
                    return null;

                return DecompressAndParseIndex (packed);
            }
            catch
            {
                return null;
            }
        }

        private byte[] ReadCompressedIndex()
        {
            using (var input = _file.CreateStream (12, _dataOffset - 12))
            using (var bits = new MsbBitStream (input))
            {
                bits.GetNextBit(); // Skip obfuscation bit

                _dictionary = ReadDictionary (bits);
                if (_dictionary == null)
                    return null;

                int packedSize   = ReadInt32 (bits);
                int unpackedSize = ReadInt32 (bits);

                if (packedSize <= 0 || unpackedSize <= 0 || packedSize > 0x1000000)
                    return null;

                var packed = new byte[packedSize];
                for (int i = 0; i < packedSize; i++)
                {
                    int b = bits.GetBits (8);
                    if (b == -1)
                        return null;
                    packed[i] = (byte)b;
                }

                return packed;
            }
        }

        private List<Entry> DecompressAndParseIndex (byte[] packed)
        {
            using (var bstr = new BinMemoryStream (packed))
            using (var zstr = new ZLibStream (bstr, CompressionMode.Decompress))
            using (var index = new MsbBitStream (zstr))
            {
                index.GetNextBit(); // Skip obfuscation bit

                int count = ReadInt32 (index);
                if (!ArchiveFormat.IsSaneCount (count))
                    return null;

                var dir = new List<Entry>(count);
                for (int i = 0; i < count; i++)
                {
                    if (index.GetBits (2) == -1)
                        break;

                    var entry = ReadEntry (index);
                    if (entry == null || !entry.CheckPlacement (_file.MaxOffset))
                        return null;

                    dir.Add (entry);
                }

                return dir;
            }
        }

        private Entry ReadEntry (MsbBitStream input)
        {
            var nameBuffer = ReadEncryptedString (input);
            if (nameBuffer == null)
                return null;

            var name = DecryptString (nameBuffer);
            if (string.IsNullOrEmpty (name))
                return null;

            var entry = FormatCatalog.Instance.Create<Entry>(name);

            ReadInt32 (input); // unknown0
            ReadInt32 (input); // unknown1

            entry.Offset = (uint)ReadInt32 (input) + _dataOffset;
            entry.Size = (uint)ReadInt32 (input);

            return entry;
        }

        private byte[] ReadDictionary (MsbBitStream input)
        {
            int size = ReadInt32 (input);
            if (size <= 0 || size > 0x10000)
                return null;

            var buffer = new byte[size];
            _rng.Init ((uint)size);

            for (int dst = 0; dst < size; dst++)
            {
                int skipBits = (int)(_rng.GetNext() & 3) + 1;
                if (input.GetBits (skipBits) == -1)
                    return null;

                _rng.GetNext(); // Advance PRNG

                int value = input.GetBits (8);
                if (value == -1)
                    return null;

                buffer[dst] = (byte)value;
            }

            return buffer;
        }

        private ushort[] ReadEncryptedString (MsbBitStream input)
        {
            int size = ReadInt32 (input);
            if (size <= 0 || size > 0x1000)
                return null;

            var buffer = new ushort[size];
            _rng.Init ((uint)size);

            for (int dst = 0; dst < size; dst++)
            {
                int skipBits = (int)(_rng.GetNext() & 3) + 1;
                if (input.GetBits (skipBits) == -1)
                    return null;

                _rng.GetNext(); // Advance PRNG

                int lo = input.GetBits (8);
                int hi = input.GetBits (8);
                if (lo == -1 || hi == -1)
                    return null;

                buffer[dst] = (ushort)(lo | (hi << 8));
            }

            return buffer;
        }

        private string DecryptString (ushort[] input)
        {
            var buffer = new byte[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] >= _dictionary.Length)
                    return null;
                buffer[i] = (byte)(_dictionary[input[i]] ^ 0xA4);
            }
            return Encodings.cp932.GetString (buffer);
        }

        private static int ReadInt32 (MsbBitStream input)
        {
            int b0 = input.GetBits (8);
            int b1 = input.GetBits (8);
            int b2 = input.GetBits (8);
            int b3 = input.GetBits (8);

            if (b0 == -1 || b1 == -1 || b2 == -1 || b3 == -1)
                return -1;

            return b3 << 24 | b2 << 16 | b1 << 8 | b0;
        }
    }

    internal sealed class AfaV3IndexWriter
    {
        private readonly MersenneTwisterLike _rng = new MersenneTwisterLike();
        private byte[] _dictionary;

        public byte[] BuildIndex (List<Entry> entries, Encoding encoding, int compressionLevel)
        {
            _dictionary = GenerateDictionary();

            // Build encrypted index
            using (var indexMs = new MemoryStream())
            {
                var bits = new MsbBitStreamWriter (indexMs);
                bits.WriteBit (1); // Obfuscation bit
                WriteInt32 (bits, entries.Count);

                long offset = 0;
                foreach (var entry in entries)
                {
                    bits.WriteBits (0, 2); // Entry marker
                    WriteEncryptedEntry (bits, entry, encoding, offset);
                    offset += entry.Size;
                }

                bits.Flush();
                var uncompressed = indexMs.ToArray();

                var compressed = new MemoryStream();
                using (var zstream = new ZLibStream (compressed, CompressionMode.Compress,
                                                   (CompressionLevel)compressionLevel))
                {
                    zstream.Write (uncompressed, 0, uncompressed.Length);
                }

                using (var output = new MemoryStream())
                {
                    var outputBits = new MsbBitStreamWriter (output);
                    outputBits.WriteBit (1); // Obfuscation bit

                    WriteDictionary (outputBits);
                    WriteInt32 (outputBits, (int)compressed.Length);
                    WriteInt32 (outputBits, uncompressed.Length);

                    compressed.Position = 0;
                    var compressedData = compressed.ToArray();
                    foreach (byte b in compressedData)
                    {
                        outputBits.WriteBits (b, 8);
                    }

                    outputBits.Flush();
                    return output.ToArray();
                }
            }
        }

        private void WriteEncryptedEntry (MsbBitStreamWriter bits, Entry entry, Encoding encoding, long offset)
        {
            var nameBytes = encoding.GetBytes (entry.Name);
            var encrypted = EncryptString (nameBytes);

            WriteEncryptedChars (bits, encrypted);
            WriteInt32 (bits, 0); // unknown0
            WriteInt32 (bits, 0); // unknown1
            WriteInt32 (bits, (int)offset);
            WriteInt32 (bits, (int)entry.Size);
        }

        private byte[] GenerateDictionary()
        {
            // Simplified version
            var dict = new byte[256];
            for (int i = 0; i < 256; i++)
                dict[i] = (byte)i;
            return dict;
        }

        private void WriteDictionary (MsbBitStreamWriter bits)
        {
            WriteInt32 (bits, _dictionary.Length);
            _rng.Init ((uint)_dictionary.Length);

            for (int i = 0; i < _dictionary.Length; i++)
            {
                int skipBits = (int)(_rng.GetNext() & 3) + 1;
                bits.WriteBits (0, skipBits);
                _rng.GetNext();
                bits.WriteBits (_dictionary[i], 8);
            }
        }

        private ushort[] EncryptString (byte[] input)
        {
            var result = new ushort[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                // Find the dictionary index that when XORed with 0xA4 gives our byte
                byte target = (byte)(input[i] ^ 0xA4);
                result[i] = target; // Since our simple dictionary is identity mapping
            }
            return result;
        }

        private void WriteEncryptedChars (MsbBitStreamWriter bits, ushort[] chars)
        {
            WriteInt32 (bits, chars.Length);
            _rng.Init ((uint)chars.Length);

            foreach (var ch in chars)
            {
                int skipBits = (int)(_rng.GetNext() & 3) + 1;
                bits.WriteBits (0, skipBits);
                _rng.GetNext();

                bits.WriteBits (ch & 0xFF, 8);        // Low byte
                bits.WriteBits ((ch >> 8) & 0xFF, 8); // High byte
            }
        }

        private void WriteInt32 (MsbBitStreamWriter bits, int value)
        {
            bits.WriteBits( value        & 0xFF, 8);
            bits.WriteBits ((value >>  8) & 0xFF, 8);
            bits.WriteBits ((value >> 16) & 0xFF, 8);
            bits.WriteBits ((value >> 24) & 0xFF, 8);
        }
    }

    /// <summary>
    /// MSB-first bit stream writer for AFA v3 format.
    /// </summary>
    internal sealed class MsbBitStreamWriter : IDisposable
    {
        private readonly Stream _output;
        private uint _cache;
        private  int _cached;

        public MsbBitStreamWriter (Stream output)
        {
            _output = output;
            _cache = 0;
            _cached = 0;
        }

        public void WriteBit (int bit)
        {
            WriteBits (bit, 1);
        }

        public void WriteBits (int value, int count)
        {
            if (count <= 0 || count > 32)
                throw new ArgumentException ("Invalid bit count", nameof (count));

            uint mask = (1u << count) - 1;
            _cache = (_cache << count) | ((uint)value & mask);
            _cached += count;

            while (_cached >= 8)
            {
                _cached -= 8;
                byte b = (byte)(_cache >> _cached);
                _output.WriteByte (b);
                _cache &= (1u << _cached) - 1;
            }
        }

        public void Flush()
        {
            if (_cached > 0)
            {
                byte b = (byte)(_cache << (8 - _cached));
                _output.WriteByte (b);
                _cached = 0;
                _cache = 0;
            }
        }

        public void Dispose()
        {
            Flush();
        }
    }

    /// <summary>
    /// Optimized Mersenne Twister-like PRNG used for AFA v3 decryption.
    /// </summary>
    internal sealed class MersenneTwisterLike
    {
        private const  int  StateSize = 521;
        private const uint Multiplier = 1566083941u;

        private readonly uint[] _state = new uint[StateSize];
        private int _index;

        public void Init (uint seed)
        {
            // Initialize state array
            uint val = 0;
            for (int i = 0; i < 17; i++)
            {
                for (int j = 0; j < 32; j++)
                {
                    seed = Multiplier * seed + 1;
                    val = (seed & 0x80000000) | (val >> 1);
                }
                _state[i] = val;
            }

            _state[16] = _state[15] ^ (_state[0] >> 9) ^ (_state[16] << 23);

            for (int i = 17; i < StateSize; i++)
            {
                _state[i] = _state[i - 1] ^ (_state[i - 16] >> 9) ^ (_state[i - 17] << 23);
            }

            // Initial shuffles
            for (int i = 0; i < 4; i++)
                Shuffle();

            _index = -1;
        }

        [MethodImpl (MethodImplOptions.AggressiveInlining)]
        public uint GetNext()
        {
            if (++_index >= StateSize)
            {
                Shuffle();
                _index = 0;
            }
            return _state[_index];
        }

        private void Shuffle()
        {
            // First part
            for (int i = 0; i < 32; i += 4)
            {
                _state[i    ] ^= _state[i + 489];
                _state[i + 1] ^= _state[i + 490];
                _state[i + 2] ^= _state[i + 491];
                _state[i + 3] ^= _state[i + 492];
            }

            // Second part
            for (int i = 32; i < StateSize; i += 3)
            {
                _state[i] ^= _state[i - 32];
                if (i + 1 < StateSize) _state[i + 1] ^= _state[i - 31];
                if (i + 2 < StateSize) _state[i + 2] ^= _state[i - 30];
            }
        }
    }
}
