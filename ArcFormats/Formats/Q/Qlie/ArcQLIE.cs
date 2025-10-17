using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;

using GameRes.Utility;
using GameRes.Formats.Borland;

namespace GameRes.Formats.Qlie
{
    internal class QlieEntry : PackedEntry
    {
        public int  EncryptionMethod;
        public uint Hash;
        public byte[] RawName;

        public new bool IsEncrypted { get { return EncryptionMethod != 0; } }
    }

    internal class QlieArchive : ArcFile
    {
        public readonly IEncryption Encryption;

        public QlieArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IEncryption enc)
            : base (arc, impl, dir)
        {
            Encryption = enc;
        }
    }

    internal class QlieOptions : ResourceOptions
    {
        public byte[] GameKeyData;
    }

    internal class QlieCreateOptions : QlieOptions
    {
        public Version PackVersion { get; set; } = new Version (3, 1);
        public bool CompressFiles { get; set; } = false;
    }

    [Serializable]
    public class QlieScheme : ResourceScheme
    {
        public Dictionary<string, byte[]> KnownKeys;
    }

    [Export(typeof(ArchiveFormat))]
    public class PackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PACK/QLIE"; } }
        public override string Description { get { return "QLIE engine resource archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        public PackOpener ()
        {
            Extensions = new string [] { "pack" };
            ContainedFormats = new[] { "ABMP/QLIE", "DPNG", "ARGB", "PNG", "JPEG", "OGG", "WAV" };
        }

        /// <summary>
        /// Possible locations of the 'key.fkey' file relative to an archive being accessed.
        /// </summary>
        static readonly string[] KeyLocations = { ".", "..", @"..\DLL", "DLL" };

        static QlieScheme DefaultScheme = new QlieScheme { KnownKeys = new Dictionary<string, byte[]>() };

        public static Dictionary<string, byte[]> KnownKeys { get { return DefaultScheme.KnownKeys; } }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (QlieScheme)value; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset <= 0x1c)
                return null;
            long index_offset = file.MaxOffset - 0x1c;
            if (!file.View.AsciiEqual (index_offset, "FilePackVer") || 
                    '.' != file.View.ReadByte (index_offset + 0xC))
                return null;
            using (var index = new PackIndexReader (this, file, index_offset))
            {
                byte[] arc_key = null;
                byte[] key_file = null;
                bool use_pack_keyfile = false;
                if (index == null)
                    return null;

                if (index.PackVersion.Major >= 3)
                {
                    key_file = FindKeyFile (file);
                    use_pack_keyfile = key_file != null;
                    if (index.PackVersion.Minor == 0)
                    {
                        if (use_pack_keyfile)
                            arc_key = QueryEncryption (file);
                    }
                    else
                    {
                        use_pack_keyfile = true;
                        key_file = null;
                    }
                }

                var enc = QlieEncryption.Create (file, index.PackVersion, arc_key);
                List<Entry> dir = null;

                if (index.PackVersion.Major > 1)
                {
                    dir = index.Read (enc, key_file, use_pack_keyfile);
                }
                else
                {
                    // PackVer1.0 is a total mess - it could either of
                    //  • V1 index layout and V1 encryption
                    //  • V1 index layout and V2 encryption
                    //  • V2 index layout and V2 encryption
                    // all with the same 'FilePackVer1.0' signature
                    var possibleEncs = new IEncryption[] { 
                        enc, new EncryptionV2 (IndexLayout.WithoutHash),
                        new EncryptionV2() 
                    };
                    foreach (var v1enc in possibleEncs)
                    {
                        try
                        {
                            dir = index.Read (v1enc, key_file, use_pack_keyfile);
                            if (dir != null)
                            {
                                enc = v1enc;
                                break;
                            }
                        }
                        catch { }
                    }
                }
                if (null == dir)
                    return null;

                Comment = $"V{index.PackVersion.Major}.{index.PackVersion.Minor}";
                return new QlieArchive (file, this, dir, enc);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var qent = entry as QlieEntry;
            var qarc = arc as QlieArchive;
            if (null == qent || null == qarc || (!qent.IsEncrypted && !qent.IsPacked))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var data = ReadEntryBytes (arc.File, qent, qarc.Encryption);
            return new BinMemoryStream (data, entry.Name);
        }

        public override void Create (
            Stream output, IEnumerable<Entry> list, ResourceOptions options,
            EntryCallback callback)
        {
            var qlie_options = GetOptions<QlieCreateOptions> (options);
            if (qlie_options == null)
                qlie_options = this.GetDefaultCreateOptions();

            using (var writer = new QliePackWriter (output, qlie_options.PackVersion))
            {
                writer.GameKey = qlie_options.GameKeyData;

                int file_count = 0;
                int total_count = list.Count();

                foreach (var entry in list)
                {
                    if (null != callback)
                        callback (file_count++, entry, Localization._T ("MsgAddingFile"));

                    using (var file = File.OpenRead (entry.Name))
                    {
                        var file_data = new byte[file.Length];
                        file.Read (file_data, 0, file_data.Length);

                        bool should_compress = qlie_options.CompressFiles && ShouldCompress (entry.Name);
                        writer.AddEntry (entry.Name, file_data, should_compress);
                    }
                }

                writer.Write (qlie_options.GameKeyData);
            }
        }

        bool ShouldCompress (string filename)
        {
            string ext = Path.GetExtension (filename).ToLowerInvariant();
            return ext != ".b" && ext != ".png" && ext != ".jpg" && ext != ".jpeg" && 
                   ext != ".ogg" && ext != ".mp3" && ext != ".mpg";
        }

        QlieCreateOptions GetDefaultCreateOptions ()
        {
            return new QlieCreateOptions {
                PackVersion = new Version (3, 1),
                GameKeyData = null,
                CompressFiles = false
            };
        }

        #region Binary Parsing
        internal byte[] ReadEntryBytes (ArcView file, QlieEntry entry, IEncryption enc)
        {
            var data = file.View.ReadBytes (entry.Offset, entry.Size);
            if (entry.IsEncrypted)
                enc.DecryptEntry (data, 0, data.Length, entry);
            if (entry.IsPacked)
                data = Decompress (data) ?? data;
            return data;
        }

        internal static byte[] Decompress (byte[] input)
        {
            if (LittleEndian.ToUInt32 (input, 0) != 0xFF435031) // '1PC\xFF'
                return null;

            bool is_16bit = 0 != (input[4] & 1);

            var node = new byte[2,256];
            var child_node = new byte[256];

            int output_length = LittleEndian.ToInt32 (input, 8);
            var output = new byte[output_length];

            int src = 12;
            int dst = 0;
            while (src < input.Length)
            {
                int i, k, count, index;

                for (i = 0; i < 256; i++)
                    node[0,i] = (byte)i;

                for (i = 0; i < 256; )
                {
                    count = input[src++];

                    if (count > 127)
                    {
                        int step = count - 127;
                        i += step;
                        count = 0;
                    }

                    if (i > 255)
                        break;

                    count++;
                    for (k = 0; k < count; k++)
                    {
                        node[0,i] = input[src++];
                        if (node[0,i] != i)
                            node[1,i] = input[src++];
                        i++;
                    }
                }

                if (is_16bit)
                {
                    count = LittleEndian.ToUInt16 (input, src);
                    src += 2;
                }
                else
                {
                    count = LittleEndian.ToInt32 (input, src);
                    src += 4;
                }

                k = 0;
                for (;;)
                {
                    if (k > 0)
                        index = child_node[--k];
                    else
                    {
                        if (0 == count)
                            break;
                        count--;
                        index = input[src++];
                    }

                    if (node[0,index] == index)
                        output[dst++] = (byte)index;
                    else
                    {
                        child_node[k++] = node[1,index];
                        child_node[k++] = node[0,index];
                    }
                }
            }
            if (dst != output.Length)
                return null;

            return output;
        }

        internal static byte[] Compress (byte[] input)
        {
            if (input == null || input.Length == 0)
                return input;

            const int BlockSize = 8192;
            const int MaxSubstitutions = 100;

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter (ms))
            {
                bw.Write (0xFF435031u);
                bw.Write (1u);
                bw.Write (input.Length);

                for (int offset = 0; offset < input.Length; offset += BlockSize)
                {
                    int len = Math.Min (BlockSize, input.Length - offset);
                    var data = new List<byte>();
                    for (int i = 0; i < len; i++)
                        data.Add (input[offset + i]);

                    var substitutions = new Dictionary<byte, (byte left, byte right)>();
                    var used = new HashSet<byte>(data);

                    var freq = new int[256, 256];
                    for (int i = 0; i < data.Count - 1; i++)
                        freq[data[i], data[i + 1]]++;

                    int nextCode = 255;
                    int substitutionCount = 0;

                    while (nextCode >= 0 && data.Count > 1 && substitutionCount < MaxSubstitutions)
                    {
                        int max = 0;
                        byte la = 0, ra = 0;
                        for (int a = 0; a < 256; a++)
                            for (int b = 0; b < 256; b++)
                            {
                                int v = freq[a, b];
                                if (v > max) { max = v; la = (byte)a; ra = (byte)b; }
                            }

                        if (max < 3) break;  

                        // Skip if this would create deep nesting
                        if (nextCode < 250 && (la > 200 || ra > 200))
                        {
                            freq[la, ra] = 0;
                            continue;
                        }

                        while (nextCode >= 0 && (used.Contains((byte)nextCode) || substitutions.ContainsKey((byte)nextCode)))
                            nextCode--;

                        if (nextCode < 0) break;

                        byte code = (byte)nextCode;
                        nextCode--;
                        substitutionCount++;

                        substitutions[code] = (la, ra);
                        freq[la, ra] = 0;

                        var next = new List<byte>();
                        for (int i = 0; i < data.Count; i++)
                        {
                            if (i < data.Count - 1 && data[i] == la && data[i + 1] == ra)
                            {
                                if (i > 0)
                                {
                                    byte ln = data[i - 1];
                                    if (freq[ln, la] > 0) freq[ln, la]--;
                                    freq[ln, code]++;
                                }
                                if (i + 2 < data.Count)
                                {
                                    byte rn = data[i + 2];
                                    if (freq[ra, rn] > 0) freq[ra, rn]--;
                                    freq[code, rn]++;
                                }

                                next.Add (code);
                                i++;
                            }
                            else
                                next.Add (data[i]);
                        }
                        data = next;
                    }

                    WriteBlockData (bw, data, substitutions);
                }

                return ms.ToArray();
            }
        }

        private static void WriteBlockData (
            BinaryWriter bw,
            IReadOnlyList<byte> compressed,
            Dictionary<byte, (byte left, byte right)> map)
        {
            // Write node table
            int i = 0;
            while (i < 256)
            {
                int run = Math.Min (128, 256 - i);
                bw.Write ((byte)(run - 1));

                for (int j = 0; j < run; j++)
                {
                    byte idx = (byte)(i + j);
                    if (map.ContainsKey (idx))
                    {
                        var pair = map[idx];
                        bw.Write (pair.left);
                        if (pair.left != idx)
                            bw.Write (pair.right);
                    }
                    else
                    {
                        bw.Write (idx);
                    }
                }
                i += run;
            }

            bw.Write ((ushort)compressed.Count);
            for (int k = 0; k < compressed.Count; k++)
                bw.Write (compressed[k]);
        }
        #endregion

        public override ResourceOptions GetDefaultOptions ()
        {
            return new QlieOptions {
                GameKeyData = GetKeyData (Properties.Settings.Default.QLIEScheme)
            };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            var w = widget as GUI.CreateQLIEWidget;
            if (w != null)
            {
                return new QlieCreateOptions {
                    PackVersion = w.GetVersion(),
                    GameKeyData = w.GetGameKey(),
                    CompressFiles = w.CompressFiles
                };
            }
            return GetDefaultOptions();
        }

        public override object GetCreationWidget ()
        {
            return new GUI.CreateQLIEWidget();
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetQLIE();
        }

        byte[] QueryEncryption (ArcView file)
        {
            var title = FormatCatalog.Instance.LookupGame (file.Name, @"..\*.exe");
            byte[] key = null;
            if (!string.IsNullOrEmpty (title) && KnownKeys.ContainsKey (title))
                return KnownKeys[title];

            if (null == key)
                key = GuessKeyData (file.Name);

            if (null == key)
            {
                var options = Query<QlieOptions> (Localization._T ("ArcEncryptedNotice"));
                key = options.GameKeyData;
            }
            return key;
        }

        static byte[] GetKeyData (string scheme)
        {
            byte[] key;
            if (KnownKeys.TryGetValue (scheme, out key))
                return key;
            return null;
        }

        /// <summary>
        /// Look for 'key.fkey' file within nearby directories specified by KeyLocations.
        /// </summary>
        static byte[] FindKeyFile (ArcView arc_file)
        {
            var dir_name = VFS.GetDirectoryName (arc_file.Name);

            foreach (var path in KeyLocations)
            {
                var name = VFS.CombinePath (dir_name, path, "key.fkey");
                var keyData = TryReadFile (name);
                if (keyData != null)
                {
                    Trace.WriteLine ("reading key from " + name, "[QLIE]");
                    return keyData;
                }
            }

            var pattern = VFS.CombinePath (dir_name, @"..\*.exe");
            try
            {
                foreach (var exe_file in VFS.GetFiles (pattern))
                {
                    var reskey = WithExeFile (exe_file, exe => exe.GetResource ("RESKEY", "#10"));
                    if (reskey != null)
                        return reskey;
                }
            }
            catch { /* ignore errors */ }

            return null;
        }

        /// <summary>
        /// Try to extract key data from EXE files (looks for TFORM1 resource).
        /// </summary>
        byte[] GuessKeyData (string arc_name)
        {
            var dir_name = VFS.GetDirectoryName (arc_name);
            var pattern = VFS.CombinePath (dir_name, @"..\*.exe");

            try
            {
                foreach (var file in VFS.GetFiles (pattern))
                {
                    try
                    {
                        var key = GetKeyDataFromExe (file);
                        if (key != null)
                            return key;
                    }
                    catch { /* ignore errors */ }
                }
            }
            catch { /* ignore errors */ }

            return null;
        }

        #region Helper Methods
        public static byte[] GetKeyDataFromExe (Entry exe_entry)
        {
            return WithExeFile (exe_entry, exe => {
                var tform = exe.GetResource ("TFORM1", "#10");
                if (null == tform || !tform.AsciiEqual (0, "TPF0"))
                    return null;

                using (var input = new BinMemoryStream (tform))
                {
                    var deserializer = new DelphiDeserializer (input);
                    var form = deserializer.Deserialize ();
                    var image = form.Contents.FirstOrDefault (n => n.Name == "IconKeyImage");
                    if (null == image)
                        return null;

                    var icon = image.Props["Picture.Data"] as byte[];
                    if (null == icon || icon.Length < 0x106 || !icon.AsciiEqual (0, "\x05TIcon"))
                        return null;

                    return new CowArray<byte> (icon, 6, 0x100).ToArray ();
                }
            });
        }

        public static byte[] GetKeyDataFromExe (string filename)
        {
            var entry = VFS.IsVirtual ? VFS.FindFile (filename) : new Entry { Name = filename };
            return GetKeyDataFromExe (entry);
        }

        /// <summary>
        /// Execute a function with an ExeFile.ResourceAccessor, handling virtual filesystems transparently.
        /// </summary>
        static T WithExeFile<T> (Entry exe_entry, Func<ExeFile.ResourceAccessor, T> action)
        {
            if (!VFS.IsVirtual)
            {
                // Direct access on physical filesystem
                using (var exe = new ExeFile.ResourceAccessor (exe_entry.Name))
                {
                    return action (exe);
                }
            }

            // Extract to temp file for virtual filesystem
            string tempFile = Path.GetTempFileName () + ".exe";
            try
            {
                using (var input = VFS.OpenStream (exe_entry))
                using (var output = File.Create (tempFile))
                {
                    input.CopyTo (output);
                }

                using (var exe = new ExeFile.ResourceAccessor (tempFile))
                {
                    return action (exe);
                }
            }
            catch
            {
                return default (T);
            }
            finally
            {
                try { File.Delete (tempFile); } catch { }
            }
        }

        static byte[] TryReadFile (string filename)
        {
            try
            {
                if (VFS.FileExists (filename))
                {
                    using (var stream = VFS.OpenStream (filename))
                    using (var ms = new MemoryStream ())
                    {
                        stream.CopyTo (ms);
                        return ms.ToArray ();
                    }
                }
            }
            catch { /* ignore */ }

            if (VFS.IsVirtual)
            {
                try
                {
                    var entry = VFS.FindFileInHierarchy (filename);
                    if (entry != null)
                    {
                        using (var stream = VFS.OpenStreamInHierarchy (entry))
                        using (var ms = new MemoryStream ())
                        {
                            stream.CopyTo (ms);
                            return ms.ToArray ();
                        }
                    }
                }
                catch { /* ignore */ }
            }

            return null;
        }
        #endregion
    }

    internal sealed class PackIndexReader : IDisposable
    {
        PackOpener  m_fmt;
        ArcView     m_file;
        Version     m_pack_version;
        int         m_count;
        long        m_index_offset;
        IBinaryStream   m_index;
        List<Entry> m_dir;

        public Version PackVersion { get { return m_pack_version; } }

        public PackIndexReader (PackOpener fmt, ArcView file, long index_offset)
        {
            m_fmt = fmt;
            m_file = file;
            m_pack_version = new Version (m_file.View.ReadByte (index_offset+0xB) - '0',
                                          m_file.View.ReadByte (index_offset+0xD) - '0');
            m_count = m_file.View.ReadInt32 (index_offset+0x10);
            if (!ArchiveFormat.IsSaneCount (m_count))
                throw new InvalidFormatException();
            m_index_offset = m_file.View.ReadInt64 (index_offset+0x14);
            if (index_offset < 0 || index_offset >= m_file.MaxOffset)
                throw new InvalidFormatException();
            m_index = m_file.CreateStream (m_index_offset);
            m_dir = new List<Entry> (m_count);
        }

        byte[]  m_name_buffer = new byte[0x100];

        public List<Entry> Read (IEncryption enc, byte[] key_file, bool use_pack_keyfile)
        {
            m_dir.Clear();
            m_index.Position = 0;
            bool looking_for_pack_keyfile = 3 == m_pack_version.Major && use_pack_keyfile;

            enc.PackKeyFile = key_file;

            for (int i = 0; i < m_count; ++i)
            {
                int name_length = m_index.ReadUInt16();

                if (name_length > 0x100)
                {
                    Debug.WriteLine ($"[PackIndexReader] Name length too long: {name_length}");
                    return null;
                }

                if (enc.IsUnicode)
                    name_length *= 2;

                if (name_length > m_name_buffer.Length)
                    m_name_buffer = new byte[name_length];

                int bytesRead = m_index.Read (m_name_buffer, 0, name_length);
                if (name_length != bytesRead)
                {
                    Debug.WriteLine ($"[PackIndexReader] Failed to read name: expected {name_length}, got {bytesRead}");
                    return null;
                }

                var name = enc.DecryptName (m_name_buffer, name_length);
                //Debug.WriteLine ($"[PackIndexReader] Decrypted name: {name}");

                var entry = m_fmt.Create<QlieEntry> (name);
                if (use_pack_keyfile)
                    entry.RawName = m_name_buffer.Take (name_length).ToArray ();

                entry.Offset = m_index.ReadInt64 ();            // [+00]
                entry.Size = m_index.ReadUInt32 ();             // [+08]
                entry.Type = GetType (name);
                //Debug.WriteLine ($"[PackIndexReader] Entry: Offset=0x{entry.Offset:X8}, Size={entry.Size}");
                if (!entry.CheckPlacement (m_file.MaxOffset))
                {
                    Debug.WriteLine ($"[PackIndexReader] Entry placement check failed: Offset=0x{entry.Offset:X8}, Size={entry.Size}, MaxOffset=0x{m_file.MaxOffset:X8}");
                    return null;
                }

                entry.UnpackedSize     = m_index.ReadUInt32 (); // [+0C]
                entry.IsPacked    = 0 != m_index.ReadInt32  (); // [+10]
                entry.EncryptionMethod = m_index.ReadInt32  (); // [+14]
                //Debug.WriteLine ($"[PackIndexReader] UnpackedSize={entry.UnpackedSize}, IsPacked={entry.IsPacked}, EncMethod={entry.EncryptionMethod}");

                if (enc.IndexLayout == IndexLayout.WithHash)
                {
                    entry.Hash        = m_index.ReadUInt32 ();  // [+18]
                    //Debug.WriteLine ($"[PackIndexReader] Hash=0x{entry.Hash:X8}");
                }

                if (looking_for_pack_keyfile &&
                    (entry.Name.StartsWith ("pack_keyfile") && entry.Name.EndsWith (".key") ||
                     entry.Name == "pack_keyfile"))
                {
                    //Debug.WriteLine ($"[PackIndexReader] Found embedded pack_keyfile: {entry.Name}");
                    var pack_keyfile = m_fmt.ReadEntryBytes (m_file, entry, enc);
                    enc.PackKeyFile = pack_keyfile;
                    looking_for_pack_keyfile = false;
                }

                m_dir.Add (entry);
            }

            return m_dir;
        }

        internal string GetType (string name)
        {
           if (name.EndsWith (".s")) return "script";
           if (name.EndsWith (".b")) return "archive";
           return FormatCatalog.Instance.GetTypeFromName (name);
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_index.Dispose();
                m_disposed = true;
            }
        }
    }
}
