using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Compression;

namespace GameRes.Formats.LiveMaker
{
    [Export(typeof(ArchiveFormat))]
    public class VffOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/vf"; } }
        public override string Description { get { return "LiveMaker resource archive"; } }
        public override uint     Signature { get { return  0x666676; } } // 'vff'
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  true; } }

        public VffOpener()
        {
            Extensions = new string[] { "dat", "exe" };
            Signatures = new uint[] { 0x666676, 0x00905A4D, 0 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint base_offset = 0;
            ArcView index_file = file;
            try
            {
                // possible filesystem structure:
                //   game.dat  -- main archive body
                //   game.ext  -- [optional] separate index (could be included into the main body)
                //   game.001  -- [optional] extra parts
                //   game.002
                //   ...

                uint signature = index_file.View.ReadUInt32 (0);
                if (file.Name.HasExtension (".exe")
                    && (0x5A4D == (signature & 0xFFFF))) // 'MZ'
                {
                    base_offset = SkipExeData (index_file);
                    if (base_offset >= file.MaxOffset)
                        return null;
                    signature = index_file.View.ReadUInt32 (base_offset);
                }
                else if (!file.Name.HasExtension (".dat"))
                {
                    return null;
                }
                else if (0x666676 != signature)
                {
                    var ext_filename = Path.ChangeExtension (file.Name, ".ext");
                    if (!VFS.FileExists (ext_filename))
                        return null;
                    index_file = VFS.OpenView (ext_filename);
                    signature = index_file.View.ReadUInt32 (0);
                }
                if (0x666676 != signature)
                    return null;
                int count = index_file.View.ReadInt32 (base_offset + 6);
                if (!IsSaneCount (count))
                    return null;

                var dir = ReadIndex (index_file, base_offset, count);
                if (null == dir)
                    return null;
                long max_offset = file.MaxOffset;
                var parts = new List<ArcView>();
                try
                {
                    for (int i = 1; i < 100; ++i)
                    {
                        var ext = string.Format (".{0:D3}", i);
                        var part_filename = Path.ChangeExtension (file.Name, ext);
                        if (!VFS.FileExists (part_filename))
                            break;
                        var arc_file = VFS.OpenView (part_filename);
                        max_offset += arc_file.MaxOffset;
                        parts.Add (arc_file);
                    }
                }
                catch
                {
                    foreach (var part in parts)
                        part.Dispose();
                    throw;
                }
                if (0 == parts.Count)
                    return new ArcFile (file, this, dir);
                return new MultiFileArchive (file, this, dir, parts);
            }
            finally
            {
                if (index_file != file)
                    index_file.Dispose();
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var vff = arc as MultiFileArchive;
            Stream input = null;
            if (vff != null)
                input = vff.OpenStream (entry);
            else
                input = arc.File.CreateStream (entry.Offset, entry.Size);

            var pent = entry as VfEntry;
            if (null == pent)
                return input;
            if (pent.IsEncrypted)
            {
                byte[] data;
                using (input)
                {
                    if (entry.Size <= 8)
                        return Stream.Null;
                    data = ReshuffleStream (input);
                }
                input = new BinMemoryStream (data, entry.Name);
            }
            if (pent.IsPacked)
                input = new ZLibStream (input, CompressionMode.Decompress);
            return input;
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                             EntryCallback callback)
        {
            var vff_options = GetOptions<VffOptions> (options);

            bool isExeOutput = false;
            string outputPath = null;
            var fileStream = output as FileStream;
            if (fileStream != null)
            {
                outputPath = fileStream.Name;
                isExeOutput = outputPath.EndsWith (".exe", StringComparison.OrdinalIgnoreCase);
            }

            if (isExeOutput && vff_options.SourceExePath != null && File.Exists (vff_options.SourceExePath))
                CreateExeArchive (output, list, vff_options, callback, outputPath);
            else
                CreateStandaloneArchive (output, list, vff_options, callback);
        }

        List<Entry> ReadIndex (ArcView file, uint base_offset, int count)
        {
            uint index_offset = base_offset + 0xA;
            var name_buffer = new byte[0x100];
            var rnd = new TpRandom (0x75D6EE39u);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint name_length = file.View.ReadUInt32 (index_offset);
                index_offset += 4;
                if (0 == name_length || name_length > name_buffer.Length)
                    return null;
                if (name_length != file.View.Read (index_offset, name_buffer, 0, name_length))
                    return null;
                index_offset += name_length;

                var name = DecryptName (name_buffer, (int)name_length, rnd);
                dir.Add (Create<VfEntry> (name));
            }
            rnd.Reset();
            long offset = base_offset + (file.View.ReadInt64 (index_offset) ^ (int)rnd.GetRand32());
            foreach (var entry in dir)
            {
                index_offset += 8;
                long next_offset = base_offset + (file.View.ReadInt64 (index_offset) ^ (int)rnd.GetRand32());
                entry.Offset = offset;
                entry.Size = (uint)(next_offset - offset);
                offset = next_offset;
            }
            index_offset += 8;
            foreach (VfEntry entry in dir)
                entry.Flags = file.View.ReadByte (index_offset++);
            foreach (VfEntry entry in dir)
            {
                entry.Unknown1 = file.View.ReadUInt32 (index_offset);
                index_offset += 4;
            }
            /*foreach (VfEntry entry in dir)
            {
                entry.Checksum = file.View.ReadUInt32 (index_offset);
                index_offset += 4;
            }*/
            return dir;
        }

        string DecryptName (byte[] name_buf, int name_length, TpRandom key)
        {
            for (int i = 0; i < name_length; ++i)
            {
                name_buf[i] ^= (byte)key.GetRand32();
            }
            return Encodings.cp932.GetString (name_buf, 0, name_length);
        }

        uint SkipExeData (ArcView file)
        {
            var exe = new ExeFile (file);
            return (uint)exe.Overlay.Offset;
        }

        byte[] ReshuffleStream (Stream input)
        {
            var header = new byte[8];
            input.Read (header, 0, 8);
            int chunk_size = header.ToInt32 (0);
            uint seed = header.ToUInt32 (4) ^ 0xF8EAu;
            int input_length = (int)input.Length - 8;
            var output = new byte[input_length];
            int count = (input_length - 1) / chunk_size + 1;
            int dst = 0;
            foreach (int i in RandomSequence (count, seed))
            {
                int position = i * chunk_size;
                input.Position = 8 + position;
                int length = Math.Min (chunk_size, input_length - position);
                input.Read (output, dst, length);
                dst += length;
            }
            return output;
        }

        static IEnumerable<int> RandomSequence (int count, uint seed)
        {
            var tp = new TpScramble (seed);
            var order = Enumerable.Range (0, count).ToList<int>();
            var seq = new int[order.Count];
            for (int i = 0; order.Count > 1; ++i)
            {
                int n = tp.GetInt32 (0, order.Count - 2);
                seq[order[n]] = i;
                order.RemoveAt (n);
            }
            seq[order[0]] = count - 1;
            return seq;
        }

        private void CreateExeArchive (Stream output, IEnumerable<Entry> list, VffOptions options,
                                       EntryCallback callback, string outputPath)
        {
            byte[] archiveData;
            using (var ms = new MemoryStream())
            {
                CreateStandaloneArchive (ms, list, options, callback);
                archiveData = ms.ToArray();
            }

            using (var exeStream = File.OpenRead (options.SourceExePath))
            {
                var exe = new ExeFile (new ArcView (exeStream, options.SourceExePath, exeStream.Length));
                uint originalOffset = (uint)exe.Overlay.Offset;

                exeStream.Position = 0;
                var buffer = new byte[originalOffset];
                exeStream.Read (buffer, 0, buffer.Length);
                output.Write (buffer, 0, buffer.Length);
            }

            long archiveOffset = output.Position;

            output.Write (archiveData, 0, archiveData.Length);

            using (var writer = new BinaryWriter (output, Encodings.cp932, true))
            {
                writer.Write ((uint)archiveOffset);
                writer.Write ((byte)0x6C); // 'l'
                writer.Write ((byte)0x76); // 'v'
            }
        }

        private void CreateStandaloneArchive (
            Stream output, IEnumerable<Entry> list, VffOptions options, 
            EntryCallback callback)
        {
            var encoding = Encodings.cp932;
            var entries = new List<VffEntry>();
            var cwd = Directory.GetCurrentDirectory();
            var isExeOutput = options.PreserveOriginalLayout && !string.IsNullOrEmpty (options.SourceExePath) && File.Exists (options.SourceExePath);

            if (isExeOutput)
            {
                var originalLayout = ReadOriginalLayout (options.SourceExePath);
                if (originalLayout.Count > 0)
                {
                    var listLookup = list.ToDictionary (e => VFS.NormalizePath (e.Name), StringComparer.OrdinalIgnoreCase);

                    foreach (var original in originalLayout)
                    {
                        var normalizedName = VFS.NormalizePath (original.Name);
                        if (listLookup.TryGetValue (normalizedName, out var entry))
                        {
                            string source_path = VFS.IsPathRooted (entry.Name)
                                ? entry.Name
                                : Path.Combine (cwd, entry.Name);

                            if (File.Exists (source_path))
                            {
                                entries.Add (new VffEntry
                                {
                                    Name = original.Name,
                                    SourcePath = source_path,
                                    Flags = original.Flags,
                                    Unknown1 = original.Unknown1,
                                    OriginalScrambleSeed = original.ScrambleSeed,
                                    OriginalScrambleChunkSize = original.ScrambleChunkSize
                                });
                                listLookup.Remove (normalizedName);
                            }
                        }
                    }

                    foreach (var entry in listLookup.Values)
                    {
                        string source_path = Path.IsPathRooted (entry.Name)
                            ? entry.Name
                            : Path.Combine (cwd, entry.Name);

                        if (File.Exists (source_path))
                        {
                            var vff_entry = new VffEntry
                            {
                                Name = entry.Name.Replace (cwd, "").TrimStart ('\\'),
                                SourcePath = source_path,
                                Unknown1 = 0,
                                OriginalScrambleSeed = options.ScrambleSeed,
                                OriginalScrambleChunkSize = 0x100
                            };
                            vff_entry.SetFlags (options.UseCompression, options.UseScrambling);
                            entries.Add (vff_entry);
                        }
                    }
                }
            }

            if (entries.Count == 0)
            {
                foreach (var entry in list)
                {
                    string source_path = Path.IsPathRooted (entry.Name)
                        ? entry.Name
                        : Path.Combine (cwd, entry.Name);

                    if (!File.Exists (source_path))
                        throw new FileNotFoundException ($"File not found: {source_path}");

                    var vff_entry = new VffEntry
                    {
                        Name = entry.Name.Replace (cwd, "").TrimStart ('\\'),
                        SourcePath = source_path,
                        Unknown1 = 0,
                        OriginalScrambleSeed = options.ScrambleSeed,
                        OriginalScrambleChunkSize = 0x100
                    };
                    vff_entry.SetFlags (options.UseCompression, options.UseScrambling);
                    entries.Add (vff_entry);
                }
            }

            using (var writer = new BinaryWriter (output, encoding, true))
            {
                // Header
                writer.Write (Signature);
                writer.Write ((ushort)0);
                writer.Write (entries.Count);

                // Encrypted names
                var name_rnd = new TpRandom (0x75D6EE39u);
                foreach (var entry in entries)
                {
                    var name_bytes = encoding.GetBytes (entry.Name);
                    writer.Write ((uint)name_bytes.Length);

                    for (int j = 0; j < name_bytes.Length; ++j)
                        name_bytes[j] ^= (byte)name_rnd.GetRand32();

                    writer.Write (name_bytes);
                }

                long offsets_position = output.Position;
                name_rnd.Reset();
                for (int i = 0; i <= entries.Count; ++i)
                    writer.Write ((long)0);

                foreach (var entry in entries)
                    writer.Write (entry.Flags);

                foreach (var entry in entries)
                    writer.Write (entry.Unknown1);

                long checksums_position = output.Position;
                foreach (var entry in entries)
                    writer.Write ((uint)0);

                foreach (var entry in entries)
                    writer.Write ((byte)0); // Encrypt flags

                int file_num = 0;

                foreach (var entry in entries)
                {
                    entry.Offset = output.Position;

                    if (callback != null)
                        callback (file_num++, entry, Localization._T ("MsgAddingFile"));

                    using (var input = File.OpenRead (entry.SourcePath))
                    {
                        if (entry.IsEncrypted)
                        {
                            WriteScrambledFile (writer, input, entry, options.ScrambleSeed);

                            long currentPos = output.Position;
                            long dataLength = currentPos - entry.Offset;
                            output.Position = entry.Offset;
                            byte[] scrambledData = new byte[dataLength];
                            output.Read (scrambledData, 0, (int)dataLength);
                            entry.Checksum = ComputeVfChecksum (scrambledData);
                            output.Position = currentPos;
                        }
                        else
                        {
                            byte[] data;

                            if (entry.IsPacked)
                            {
                                using (var ms = new MemoryStream())
                                {
                                    using (var zstream = new ZLibStream (
                                        ms, CompressionMode.Compress, CompressionLevel.Level1, true))
                                    {
                                        input.CopyTo (zstream);
                                    }
                                    data = ms.ToArray();
                                    if (data.Length >= 2 && data[0] == 0x78 && data[1] == 0x5E)
                                        data[1] = 0x01;
                                }
                            }
                            else
                            {
                                data = new byte[input.Length];
                                input.Read (data, 0, data.Length);
                            }

                            entry.Checksum = ComputeVfChecksum (data);
                            output.Write (data, 0, data.Length);
                        }
                    }
                }

                // Go back and write real offsets
                long end_position = output.Position;
                output.Position = offsets_position;
                name_rnd.Reset();
                foreach (var entry in entries)
                    writer.Write (entry.Offset ^ (int)name_rnd.GetRand32());
                writer.Write (end_position ^ (int)name_rnd.GetRand32());

                // Write checksums
                output.Position = checksums_position;
                foreach (var entry in entries)
                    writer.Write (entry.Checksum);

                output.Position = end_position;

                if (!isExeOutput)
                {
                    writer.Write ((uint)0);
                    writer.Write ((byte)0x6C); // 'l'
                    writer.Write ((byte)0x76); // 'v'
                }
            }
        }

        private static readonly uint[] VF_CHECKSUM_KEYS = new uint[] {
            0x00000000, 0x77073096, 0xEE0E612C, 0x990951BA, 0x076DC419, 0x706AF48F, 0xE963A535, 0x9E6495A3,
            0x0EDB8832, 0x79DCB8A4, 0xE0D5E91E, 0x97D2D988, 0x09B64C2B, 0x7EB17CBD, 0xE7B82D07, 0x90BF1D91,
            0x1DB71064, 0x6AB020F2, 0xF3B97148, 0x84BE41DE, 0x1ADAD47D, 0x6DDDE4EB, 0xF4D4B551, 0x83D385C7,
            0x136C9856, 0x646BA8C0, 0xFD62F97A, 0x8A65C9EC, 0x14015C4F, 0x63066CD9, 0xFA0F3D63, 0x8D080DF5,
            0x3B6E20C8, 0x4C69105E, 0xD56041E4, 0xA2677172, 0x3C03E4D1, 0x4B04D447, 0xD20D85FD, 0xA50AB56B,
            0x35B5A8FA, 0x42B2986C, 0xDBBBC9D6, 0xACBCF940, 0x32D86CE3, 0x45DF5C75, 0xDCD60DCF, 0xABD13D59,
            0x26D930AC, 0x51DE003A, 0xC8D75180, 0xBFD06116, 0x21B4F4B5, 0x56B3C423, 0xCFBA9599, 0xB8BDA50F,
            0x2802B89E, 0x5F058808, 0xC60CD9B2, 0xB10BE924, 0x2F6F7C87, 0x58684C11, 0xC1611DAB, 0xB6662D3D,
            0x76DC4190, 0x01DB7106, 0x98D220BC, 0xEFD5102A, 0x71B18589, 0x06B6B51F, 0x9FBFE4A5, 0xE8B8D433,
            0x7807C9A2, 0x0F00F934, 0x9609A88E, 0xE10E9818, 0x7F6A0DBB, 0x086D3D2D, 0x91646C97, 0xE6635C01,
            0x6B6B51F4, 0x1C6C6162, 0x856530D8, 0xF262004E, 0x6C0695ED, 0x1B01A57B, 0x8208F4C1, 0xF50FC457,
            0x65B0D9C6, 0x12B7E950, 0x8BBEB8EA, 0xFCB9887C, 0x62DD1DDF, 0x15DA2D49, 0x8CD37CF3, 0xFBD44C65,
            0x4DB26158, 0x3AB551CE, 0xA3BC0074, 0xD4BB30E2, 0x4ADFA541, 0x3DD895D7, 0xA4D1C46D, 0xD3D6F4FB,
            0x4369E96A, 0x346ED9FC, 0xAD678846, 0xDA60B8D0, 0x44042D73, 0x33031DE5, 0xAA0A4C5F, 0xDD0D7CC9,
            0x5005713C, 0x270241AA, 0xBE0B1010, 0xC90C2086, 0x5768B525, 0x206F85B3, 0xB966D409, 0xCE61E49F,
            0x5EDEF90E, 0x29D9C998, 0xB0D09822, 0xC7D7A8B4, 0x59B33D17, 0x2EB40D81, 0xB7BD5C3B, 0xC0BA6CAD,
            0xEDB88320, 0x9ABFB3B6, 0x03B6E20C, 0x74B1D29A, 0xEAD54739, 0x9DD277AF, 0x04DB2615, 0x73DC1683,
            0xE3630B12, 0x94643B84, 0x0D6D6A3E, 0x7A6A5AA8, 0xE40ECF0B, 0x9309FF9D, 0x0A00AE27, 0x7D079EB1,
            0xF00F9344, 0x8708A3D2, 0x1E01F268, 0x6906C2FE, 0xF762575D, 0x806567CB, 0x196C3671, 0x6E6B06E7,
            0xFED41B76, 0x89D32BE0, 0x10DA7A5A, 0x67DD4ACC, 0xF9B9DF6F, 0x8EBEEFF9, 0x17B7BE43, 0x60B08ED5,
            0xD6D6A3E8, 0xA1D1937E, 0x38D8C2C4, 0x4FDFF252, 0xD1BB67F1, 0xA6BC5767, 0x3FB506DD, 0x48B2364B,
            0xD80D2BDA, 0xAF0A1B4C, 0x36034AF6, 0x41047A60, 0xDF60EFC3, 0xA867DF55, 0x316E8EEF, 0x4669BE79,
            0xCB61B38C, 0xBC66831A, 0x256FD2A0, 0x5268E236, 0xCC0C7795, 0xBB0B4703, 0x220216B9, 0x5505262F,
            0xC5BA3BBE, 0xB2BD0B28, 0x2BB45A92, 0x5CB36A04, 0xC2D7FFA7, 0xB5D0CF31, 0x2CD99E8B, 0x5BDEAE1D,
            0x9B64C2B0, 0xEC63F226, 0x756AA39C, 0x026D930A, 0x9C0906A9, 0xEB0E363F, 0x72076785, 0x05005713,
            0x95BF4A82, 0xE2B87A14, 0x7BB12BAE, 0x0CB61B38, 0x92D28E9B, 0xE5D5BE0D, 0x7CDCEFB7, 0x0BDBDF21,
            0x86D3D2D4, 0xF1D4E242, 0x68DDB3F8, 0x1FDA836E, 0x81BE16CD, 0xF6B9265B, 0x6FB077E1, 0x18B74777,
            0x88085AE6, 0xFF0F6A70, 0x66063BCA, 0x11010B5C, 0x8F659EFF, 0xF862AE69, 0x616BFFD3, 0x166CCF45,
            0xA00AE278, 0xD70DD2EE, 0x4E048354, 0x3903B3C2, 0xA7672661, 0xD06016F7, 0x4969474D, 0x3E6E77DB,
            0xAED16A4A, 0xD9D65ADC, 0x40DF0B66, 0x37D83BF0, 0xA9BCAE53, 0xDEBB9EC5, 0x47B2CF7F, 0x30B5FFE9,
            0xBDBDF21C, 0xCABAC28A, 0x53B39330, 0x24B4A3A6, 0xBAD03605, 0xCDD70693, 0x54DE5729, 0x23D967BF,
            0xB3667A2E, 0xC4614AB8, 0x5D681B02, 0x2A6F2B94, 0xB40BBE37, 0xC30C8EA1, 0x5A05DF1B, 0x2D02EF8D,
        };

        public static uint ComputeVfChecksum (byte[] data)
        {
            uint csum = 0xFFFFFFFF;
            foreach (byte c in data)
            {
                uint x = (csum & 0xFF) ^ c;
                x = VF_CHECKSUM_KEYS[x];
                csum = (csum >> 8) ^ x;
            }
            return csum ^ 0xFFFFFFFF;
        }

        void WriteScrambledFile (BinaryWriter writer, Stream input, VffEntry entry, uint defaultSeed)
        {
            int chunk_size = entry.OriginalScrambleChunkSize ?? 0x100;
            uint seed = entry.OriginalScrambleSeed ?? defaultSeed;

            var data = new byte[input.Length];
            input.Read (data, 0, data.Length);

            if (entry.IsPacked)
            {
                using (var ms = new MemoryStream())
                using (var zstream = new ZLibStream (ms, CompressionMode.Compress, CompressionLevel.Level1, true))
                {
                    zstream.Write (data, 0, data.Length);
                    zstream.Flush();
                    data = ms.ToArray();
                    if (data.Length >= 2 && data[0] == 0x78 && data[1] == 0x5E)
                        data[1] = 0x01;
                }
            }

            // Write scramble header
            writer.Write (chunk_size);
            writer.Write (seed ^ 0xF8EAu);

            // Scramble and write data
            int count = (data.Length - 1) / chunk_size + 1;
            var scrambled = new byte[data.Length];
            int src = 0;

            foreach (int i in RandomSequence (count, seed))
            {
                int position = i * chunk_size;
                int length = Math.Min (chunk_size, data.Length - position);
                Buffer.BlockCopy (data, src, scrambled, position, length);
                src += length;
            }

            writer.Write (scrambled);
        }

        private class OriginalFileInfo
        {
            public           string Name { get; set; }
            public            byte Flags { get; set; }
            public         uint Unknown1 { get; set; }
            public         uint Checksum { get; set; }
            public     uint ScrambleSeed { get; set; }
            public int ScrambleChunkSize { get; set; }
        }

        private List<OriginalFileInfo> ReadOriginalLayout (string exePath)
        {
            var layout = new List<OriginalFileInfo>();

            using (var view = VFS.OpenView (exePath))
            using (var arc = TryOpen (view))
            {
                if (arc == null)
                    return layout;

                foreach (var entry in arc.Dir)
                {
                    var vfEntry = entry as VfEntry;
                    if (vfEntry != null)
                    {
                        var fileInfo = new OriginalFileInfo {
                            Name = vfEntry.Name,
                            Flags = vfEntry.Flags,
                            Unknown1 = vfEntry.Unknown1,
                            Checksum = vfEntry.Checksum,
                            ScrambleSeed = 0xF8EA,
                            ScrambleChunkSize = 0x100
                        };

                        if (vfEntry.IsEncrypted && vfEntry.Size > 8)
                        {
                            using (var input = arc.File.CreateStream (vfEntry.Offset, Math.Min (8, vfEntry.Size)))
                            {
                                var header = new byte[8];
                                if (8 == input.Read (header, 0, 8))
                                {
                                    fileInfo.ScrambleChunkSize = header.ToInt32 (0);
                                    fileInfo.ScrambleSeed = header.ToUInt32 (4) ^ 0xF8EAu;
                                }
                            }
                        }

                        layout.Add (fileInfo);
                    }
                }
            }

            return layout;
        }

        public override ResourceOptions GetDefaultOptions()
        {
            return new VffOptions {
                UseCompression         = true,
                UseScrambling          = false,
                ScrambleSeed           = 0xF8EA,
                SourceExePath          = "",
                PreserveOriginalLayout = false
            };
        }

        uint ParseSeed (string text)
        {
            uint seed;
            if (uint.TryParse (text, System.Globalization.NumberStyles.HexNumber,
                               System.Globalization.CultureInfo.InvariantCulture, out seed))
                return seed;
            else if (uint.TryParse (text, out seed))
                return seed;
            else
                return 0xF8EA;
        }

        public override ResourceOptions GetOptions (object widget)
        {
            var w = widget as GUI.CreateVFFWidget;
            if (w != null)
            {
                return new VffOptions {
                    UseCompression = w.UseCompression,
                    UseScrambling = w.UseScrambling,
                    ScrambleSeed = w.ScrambleSeed,
                    SourceExePath = w.SourceExePath,
                    PreserveOriginalLayout = w.PreserveOriginalLayout
                };
            }
            return GetDefaultOptions();
        }

        public override object GetCreationWidget()
        {
            return new GUI.CreateVFFWidget();
        }
    }

    internal class VfEntry : PackedEntry
    {
        public byte Flags;
        public uint Unknown1;
        public uint Checksum;

        public override bool IsEncrypted { get => Flags == 2 || Flags == 3; }
        public override    bool IsPacked { get => Flags == 0 || Flags == 3; }

        public void SetFlags (bool packed, bool scrambled)
        {
            if (packed && scrambled) Flags = 3;
            else if (scrambled)      Flags = 2;
            else if (packed)         Flags = 0;
            else                     Flags = 1;
        }
    }

    internal class VffEntry : VfEntry
    {
        public              string SourcePath { get; set; }
        public     uint? OriginalScrambleSeed { get; set; }
        public int? OriginalScrambleChunkSize { get; set; }
    }

    public class VffOptions : ResourceOptions
    {
        public        bool  UseCompression { get; set; }
        public          bool UseScrambling { get; set; }
        public           uint ScrambleSeed { get; set; }
        public        string SourceExePath { get; set; }
        public bool PreserveOriginalLayout { get; set; }
    }

    internal class TpRandom
    {
        uint   m_seed;
        uint   m_current;

        public TpRandom (uint seed)
        {
            m_seed = seed;
            m_current = 0;
        }

        public uint GetRand32()
        {
            unchecked
            {
                m_current = ((m_current << 2) + m_current + m_seed);
            }
            return m_current;
        }

        public void Reset()
        {
            m_current = 0;
        }
    }

    internal class TpScramble
    {
        uint[] m_state = new uint[5];

        const uint FactorA = 2111111111;
        const uint FactorB = 1492;
        const uint FactorC = 1776;
        const uint FactorD = 5115;

        public TpScramble (uint seed)
        {
            Init (seed);
        }

        public void Init (uint seed)
        {
            uint hash = seed != 0 ? seed : 0xFFFFFFFFu;
            for (int i = 0; i < 5; ++i)
            {
                hash ^= hash << 13;
                hash ^= hash >> 17;
                hash ^= hash << 5;
                m_state[i] = hash;
            }
            for (int i = 0; i < 19; ++i)
                GetUInt32();
        }

        public int GetInt32 (int first, int last)
        {
            var num = GetDouble();
            return (int)(first + (long)(num * (last - first + 1)));
        }

        double GetDouble ()
        {
            return (double)GetUInt32() / 0x100000000L;
        }

        uint GetUInt32 ()
        {
            ulong v = FactorA * (ulong)m_state[3]
                    + FactorB * (ulong)m_state[2]
                    + FactorC * (ulong)m_state[1]
                    + FactorD * (ulong)m_state[0] + m_state[4];
            m_state[3] = m_state[2];
            m_state[2] = m_state[1];
            m_state[1] = m_state[0];
            m_state[4] = (uint)(v >> 32);
            return m_state[0] = (uint)v;
        }
    }
}
