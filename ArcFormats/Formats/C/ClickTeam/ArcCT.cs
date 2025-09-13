using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Compression;

namespace GameRes.Formats.Clickteam
{
    [Export(typeof(ArchiveFormat))]
    public class CcnOpener : ArchiveFormat
    {
        // Constants
        private const uint     SIGNATURE_PAMU = 0x554D4150;
        private const uint     SIGNATURE_PAME = 0x454D4150;
        private const uint     SIGNATURE_CRUF = 0x46555243;
        private const uint      SIGNATURE_MFA = 0x3255464D;
        private const uint SIGNATURE_UNPACKED = 0x77777777;

        #region Chunks
        private const short           CHUNK_LAST = 0x7F7F;
        private const short        CHUNK_APPNAME = 0x2224;
        private const short         CHUNK_AUTHOR = 0x2225;
        private const short      CHUNK_COPYRIGHT = 0x223B;
        private const short          CHUNK_ABOUT = 0x223A;
        private const short CHUNK_EDITORFILENAME = 0x222E;
        private const short       CHUNK_HELPFILE = 0x2230;
        private const short    CHUNK_BINARYFILES = 0x2238;
        private const short          CHUNK_FRAME = 0x3333;
        private const short      CHUNK_FRAMENAME = 0x3335;
        private const short      CHUNK_IMAGEBANK = 0x6666;
        private const short      CHUNK_SOUNDBANK = 0x6668;
        private const short      CHUNK_MUSICBANK = 0x6669;
        #endregion

        private const int MAX_ENTRIES = 10000;
        private const int BUILD_284 = 284;
        private const int BUILD_285 = 285;
        private const int RUNTIME_VERSION_MMF15 = 769;
        private const int BUILD_MMF25_THRESHOLD = 280;

        public override string         Tag { get { return "CCN"; } }
        public override string Description { get { return "Clickteam Fusion Game Archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        private static readonly uint[] GameDataSignatures = { SIGNATURE_PAMU, SIGNATURE_PAME, SIGNATURE_CRUF };

        public static int       BuildNumber { get; internal set; }
        public static float   FusionVersion { get; internal set; }
        public static bool        IsAndroid { get; internal set; }
        public static bool            IsIOS { get; internal set; }
        public static bool           IsPlus { get; internal set; }
        public static bool         IsSeeded { get; internal set; }
        public static string RuntimeVersion { get; internal set; }

        public CcnOpener()
        {
            Extensions = new string[] { "exe", "ccn", "dat", "mfa", "" };
            Signatures = new uint[] {
                SIGNATURE_PAMU, SIGNATURE_PAME, SIGNATURE_CRUF,
                SIGNATURE_MFA,
                SIGNATURE_UNPACKED,
                0
            };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint sig = file.View.ReadUInt32 (0);

            if (sig == SIGNATURE_MFA)
                return TryOpenMFA (file);

            if (file.View.AsciiEqual (0, "MZ"))
                return TryOpenEXE (file);

            if (IsGameDataSignature (sig))
                return ReadGameData (file, 0);

            if (sig == SIGNATURE_UNPACKED)
                return TryOpenUnpacked (file, 0);

            return null;
        }

        private bool IsGameDataSignature (uint sig)
        {
            return GameDataSignatures.Contains (sig);
        }

        private ArcFile TryOpenEXE (ArcView file)
        {
            long offset = GetOverlayOffset (file);
            if (offset == 0) return null;

            uint sig = file.View.ReadUInt32 (offset);

            if (sig == SIGNATURE_UNPACKED)
                return TryOpenUnpacked (file, offset);

            if (IsGameDataSignature (sig))
                return ReadGameData (file, offset);

            return null;
        }

        private ArcFile TryOpenUnpacked (ArcView file, long startOffset)
        {
            var entries = new List<Entry>();
            long pos = startOffset + 4; // Skip 'wwww'

            uint marker = file.View.ReadUInt32 (pos);
            if (marker == 2004318071 || (marker == 32639 && file.View.ReadUInt32 (pos + 4) == 0x12478749))
                pos += 24;
            else
                pos += 24;

            pos = ReadPackData (file, pos, entries);

            if (pos < file.MaxOffset - 16)
            {
                uint sig = file.View.ReadUInt32 (pos);
                if (IsGameDataSignature (sig))
                {
                    var reader = new CcnReader (file, pos);
                    entries.AddRange (reader.ReadIndex());
                }
                else
                {
                    for (long search = pos; search < Math.Min (pos + 0x1000, file.MaxOffset - 16); search++)
                    {
                        if (IsGameDataSignature (file.View.ReadUInt32 (search)))
                        {
                            var reader = new CcnReader (file, search);
                            entries.AddRange (reader.ReadIndex());
                            break;
                        }
                    }
                }
            }

            if (!entries.Any (e => e.Name.StartsWith ("Images/") || e.Name.StartsWith ("Sounds/")))
                TryReadExternalDat (file, entries);

            return entries.Count > 0 ? new ArcFile (file, this, entries) : null;
        }

        private long ReadPackData (ArcView file, long pos, List<Entry> entries)
        {
            uint count = file.View.ReadUInt32 (pos);
            pos += 4;

            if (count > MAX_ENTRIES) return pos;

            for (uint i = 0; i < count && pos < file.MaxOffset; i++)
            {
                if (pos + 2 > file.MaxOffset) break;

                ushort nameLen = file.View.ReadUInt16 (pos);
                pos += 2;

                if (pos + nameLen * 2 + 8 > file.MaxOffset) break;

                string name = Encoding.Unicode.GetString (file.View.ReadBytes (pos, (uint)(nameLen * 2)));
                pos += nameLen * 2 + 4; // Skip name and bingo

                uint size = file.View.ReadUInt32 (pos);
                pos += 4;

                if (pos + size > file.MaxOffset) break;

                if (!string.IsNullOrEmpty (name))
                {
                    entries.Add (new PackedEntry
                    {
                        Name         = "Extensions/" + VFS.GetFileName (name),
                        Type         = FormatCatalog.Instance.GetTypeFromName (name),
                        Offset       = pos,
                        Size         = size,
                        IsPacked     = size > 2 && file.View.ReadInt16 (pos) == -9608,
                        UnpackedSize = size
                    });
                }

                pos += size;
            }

            return pos;
        }

        private void TryReadExternalDat (ArcView file, List<Entry> entries)
        {
            string datPath = Path.ChangeExtension (file.Name, ".dat");
            if (!File.Exists (datPath)) return;

            try
            {
                using (var datFile = new ArcView (datPath))
                {
                    uint sig = datFile.View.ReadUInt32 (0);
                    long offset = sig == SIGNATURE_UNPACKED ? 28 : 0;

                    if (offset > 0)
                        sig = datFile.View.ReadUInt32 (offset);

                    if (IsGameDataSignature (sig))
                    {
                        var reader = new CcnReader (datFile, offset);
                        entries.AddRange (reader.ReadIndex());
                    }
                }
            }
            catch { }
        }

        private ArcFile ReadGameData (ArcView file, long offset)
        {
            var reader = new CcnReader (file, offset);
            var entries = reader.ReadIndex();
            return entries?.Count > 0 ? new ArcFile (file, this, entries) : null;
        }

        private ArcFile TryOpenMFA (ArcView file)
        {
            var reader = new MfaReader (file);
            var entries = reader.ReadIndex();
            return entries?.Count > 0 ? new ArcFile (file, this, entries) : null;
        }

        private long GetOverlayOffset (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "MZ")) return 0;

            uint peOffset = file.View.ReadUInt16 (0x3C);
            if (!file.View.AsciiEqual (peOffset, "PE")) return 0;

            ushort numSections = file.View.ReadUInt16 (peOffset + 6);
            ushort optHeaderSize = file.View.ReadUInt16 (peOffset + 20);
            uint sectionOffset = peOffset + 24 + optHeaderSize;
            uint lastSectionEnd = 0;

            for (int i = 0; i < numSections; i++, sectionOffset += 40)
            {
                uint size = file.View.ReadUInt32 (sectionOffset + 16);
                uint addr = file.View.ReadUInt32 (sectionOffset + 20);
                lastSectionEnd = Math.Max (lastSectionEnd, addr + size);
            }

            lastSectionEnd = (lastSectionEnd + 511) & ~511u; // Align to 512
            return lastSectionEnd < file.MaxOffset ? lastSectionEnd : 0;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry is CcnEntry ccn)
            {
                if (ccn.IsCompressed && ccn.ImageOffset > 0)
                {
                    var reader = new CcnReader (arc.File, ccn.Offset - 16); // Need reader for decryption
                    byte[] bankData = reader.ReadChunkData (ccn.Offset, (int)arc.File.View.ReadUInt32 (ccn.Offset - 4), ccn.BankFlag, 0x6666);

                    // Extract the image data
                    byte[] imageData = new byte[ccn.Size];
                    Array.Copy (bankData, ccn.ImageOffset, imageData, 0, Math.Min (imageData.Length, bankData.Length - ccn.ImageOffset));

                    return new BinMemoryStream (imageData, entry.Name);
                }

                if (ccn.TextContent != null)
                    return new BinMemoryStream (Encoding.UTF8.GetBytes (ccn.TextContent), entry.Name);

                if (ccn.RawData != null)
                    return new BinMemoryStream (ccn.RawData, entry.Name);
            }

            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);

            if (entry is CcnEntry ccnEntry && ccnEntry.IsCompressed)
                data = Utils.Decompress (data);
            else if (entry is PackedEntry packed && packed.IsPacked)
                data = Utils.Decompress (data);

            return new BinMemoryStream (data, entry.Name);
        }

    }



    internal class CcnReader
    {
        private readonly     ArcView m_file;
        private readonly        long m_offset;
        private readonly List<Entry> m_entries = new List<Entry>();
        private                 bool m_unicode;
        private                  int m_build;
        private                float m_fusion = 2.5f;
        private readonly      byte[] m_decryptionKey = new byte[256];
        private               string m_appName = "";
        private               string m_copyright = "";
        private               string m_editorFilename = "";
        private                 long m_currentChunkOffset;
        private            List<int> m_imageOffsets = new List<int>();

        private const short           CHUNK_LAST = 0x7F7F;
        private const short        CHUNK_APPNAME = 0x2224;
        private const short         CHUNK_AUTHOR = 0x2225;
        private const short      CHUNK_COPYRIGHT = 0x223B;
        private const short          CHUNK_ABOUT = 0x223A;
        private const short CHUNK_EDITORFILENAME = 0x222E;
        private const short       CHUNK_HELPFILE = 0x2230;
        private const short    CHUNK_BINARYFILES = 0x2238;
        private const short          CHUNK_FRAME = 0x3333;
        private const short      CHUNK_FRAMENAME = 0x3335;
        private const short      CHUNK_IMAGEBANK = 0x6666;
        private const short      CHUNK_SOUNDBANK = 0x6668;
        private const short      CHUNK_MUSICBANK = 0x6669;
        private const short           CHUNK_PLUS = 0x2233;
        private const short           CHUNK_SEED = 0x7EEE;
        private const short     CHUNK_FRAME_HEAD = 0x3334;
        private const short   CHUNK_IMAGEOFFSETS = 0x5555;

        private const int BUILD_284 = 284;
        private const int BUILD_285 = 285;
        private const int BUILD_MMF25_THRESHOLD = 280;
        private const int RUNTIME_VERSION_MMF15 = 769;

        private const string PAMU = "PAMU";
        private const string PAME = "PAME";
        private const string CRUF = "CRUF";

        internal byte[] CurrentDecryptionKey => m_decryptionKey;

        private static readonly Dictionary<short, Action<CcnReader, byte[]>> ChunkHandlers = new Dictionary<short, Action<CcnReader, byte[]>>
        {
            { CHUNK_APPNAME,        (r, d) => { r.m_appName = r.ReadCTString (d); r.UpdateDecryptionKey(); r.AddTextEntry ("AppName.txt", r.m_appName); } },
            { CHUNK_AUTHOR,         (r, d) => r.AddTextEntry ("Author.txt", r.ReadCTString (d)) },
            { CHUNK_ABOUT,          (r, d) => r.AddTextEntry ("About.txt", r.ReadCTString (d)) },
            { CHUNK_COPYRIGHT,      (r, d) => { r.m_copyright = r.ReadCTString (d); r.UpdateDecryptionKey(); r.AddTextEntry ("Copyright.txt", r.m_copyright); } },
            { CHUNK_EDITORFILENAME, (r, d) => { r.m_editorFilename = r.ReadCTString (d); r.UpdateDecryptionKey(); } },
            { CHUNK_HELPFILE,       (r, d) => r.AddTextEntry ("HelpFile.txt", r.ReadCTString (d)) },
            { CHUNK_PLUS,           (r, d) => CcnOpener.IsPlus = true },
            { CHUNK_SEED,           (r, d) => CcnOpener.IsSeeded = true },
            { CHUNK_IMAGEOFFSETS,   (r, d) => r.ReadImageOffsets (d) },
            { CHUNK_SOUNDBANK,      (r, d) => r.ReadBank (d, "Sounds", "sound", r.ReadSoundEntry) },
            { CHUNK_MUSICBANK,      (r, d) => r.ReadBank (d, "Music", "music", r.ReadMusicEntry) },
            { CHUNK_BINARYFILES,    (r, d) => r.ReadBinaryFiles (d) },
            { CHUNK_FRAME,          (r, d) => r.ReadFrame (d) },
        };

        public CcnReader (ArcView file, long offset)
        {
            m_file = file;
            m_offset = offset;
            for (int i = 0; i < 256; i++)
                m_decryptionKey[i] = (byte)i;
        }

        public List<Entry> ReadIndex()
        {
            try
            {
                if (!ReadHeader()) return m_entries;
                ReadChunks();
            }
            catch { }
            return m_entries;
        }

        private bool ReadHeader()
        {
            if (m_offset + 16 > m_file.MaxOffset) return false;

            string magic = m_file.View.ReadString (m_offset, 4);
            if (magic != PAMU && magic != PAME && magic != CRUF) return false;

            m_unicode = magic == PAMU;

            short runtimeVer = m_file.View.ReadInt16 (m_offset + 4);
            m_build = m_file.View.ReadInt32 (m_offset + 12);
            int productVersion = m_file.View.ReadInt32 (m_offset + 8);

            if (runtimeVer != RUNTIME_VERSION_MMF15)
            {
                if (m_build < BUILD_MMF25_THRESHOLD)
                    m_fusion = 2f + (productVersion == 1 ? 0.1f : 0);
                else
                    m_fusion = 2.5f;
            }
            else
                m_fusion = 1.5f;

            if (magic == CRUF) m_fusion = 3.0f;

            System.Diagnostics.Debug.WriteLine ($"fusion ver: {m_fusion}, build: {m_build}, product ver: {productVersion}");

            CcnOpener.BuildNumber = m_build;
            CcnOpener.FusionVersion = m_fusion;

            return true;
        }

        private void ReadChunks()
        {
            long pos = m_offset + 16;

            while (pos < m_file.MaxOffset - 8)
            {
                short chunkId = m_file.View.ReadInt16 (pos);
                short chunkFlag = m_file.View.ReadInt16 (pos + 2);
                int chunkSize = m_file.View.ReadInt32 (pos + 4);

                System.Diagnostics.Debug.Write ($"chunk: 0x{chunkId:X} flag:{chunkFlag} size:{chunkSize / 1024}K; ");

                if (chunkId == CHUNK_LAST || chunkSize < 0 || chunkSize > m_file.MaxOffset) break;

                pos += 8;
                m_currentChunkOffset = pos;  // Track where we are

                if (pos + chunkSize > m_file.MaxOffset) break;

                try
                {
                    if (chunkId == CHUNK_IMAGEBANK)
                         ReadImageBankLazy (pos, chunkSize, chunkFlag); // need pos data
                    else if (ChunkHandlers.TryGetValue (chunkId, out var handler)){
                        var data = ReadChunkData (pos, chunkSize, chunkFlag, chunkId);
                        handler (this, data);
                    }
                }
                catch { }

                pos += chunkSize;
            }
            System.Diagnostics.Debug.Write ("\n");
        }

        private void ReadImageOffsets (byte[] data)
        {
            using (var stream = new BinMemoryStream (data))
            using (var reader = new BinaryReader (stream))
            {
                while (stream.Position < stream.Length)
                    m_imageOffsets.Add (reader.ReadInt32());
                m_imageOffsets = m_imageOffsets.Where (x => x != 0).OrderBy (x => x).ToList();
            }
        }

        private void ReadImageBankLazy (long offset, int size, short flag)
        {
            if (m_fusion == 1.5f && m_imageOffsets.Count > 0)
            {
                for (int i = 0; i < m_imageOffsets.Count; i++)
                {
                    if (m_imageOffsets[i] == 0) continue;

                    int nextOffset = (i + 1 < m_imageOffsets.Count) ? m_imageOffsets[i + 1] : size + 4;
                    int imageSize = nextOffset - m_imageOffsets[i];

                    if (imageSize <= 0) continue;

                    m_entries.Add (new CcnEntry
                    {
                        Name = $"Images/img_{i:D5}.ccnimg",
                        Type = "image",
                        BankFlag = flag,
                        ImageOffset = offset + m_imageOffsets[i] - 4,
                        Size = (uint)imageSize,
                        IsCompressed = true
                    });
                }
                return;
            }
            var headerData = m_file.View.ReadBytes (offset, 4);
            int count = BitConverter.ToInt32 (headerData, 0);

            if (!ArchiveFormat.IsSaneCount (count, 20000)) return;

            long pos = offset + 4;

            for (int i = 0; i < count && pos < offset + size; i++)
            {
                try
                {
                    uint handle = m_file.View.ReadUInt32 (pos);
                    if (m_build >= BUILD_284) handle--;
                    pos += 4;

                    int decompSize = m_file.View.ReadInt32 (pos);
                    pos += 4;

                    int compSize = m_file.View.ReadInt32 (pos);
                    pos += 4;

                    if (compSize <= 0 || compSize > size - (pos - offset))
                        break;

                    m_entries.Add (new CcnEntry
                    {
                        Name         = $"Images/img_{handle:D5}.ccnimg",
                        Type         = "image",
                        Offset       = pos,
                        Size         = (uint)compSize,
                        IsCompressed = true
                    });

                    pos += compSize;
                }
                catch
                {
                    break;
                }
            }
        }

        internal  byte[] ReadChunkData (long offset, int size, short flag, short chunkId)
        {
            var data = m_file.View.ReadBytes (offset, (uint)size);

            switch (flag)
            {
                case 1: return Utils.Decompress (data);     // Compressed
                case 2: TransformChunk (data); break;       // Encrypted
                case 3: return DecodeMode3 (data, chunkId); // Both
            }

            return data;
        }

        private byte[] DecodeMode3 (byte[] data, short chunkId)
        {
            if (data.Length < 4) return data;

            var rawData = new byte[data.Length - 4];
            Array.Copy (data, 4, rawData, 0, rawData.Length);

            if ((chunkId & 1) == 1 && m_build > 285 && rawData.Length > 0)
                rawData[0] ^= (byte)((chunkId & 0xFF) ^ (chunkId >> 8));

            TransformChunk (rawData);

            if (rawData.Length < 4) return rawData;

            uint compSize = BitConverter.ToUInt32 (rawData, 0);
            if (compSize > rawData.Length - 4) return data;

            return Utils.Decompress (rawData, 4, (int)compSize);
        }

        private void TransformChunk (byte[] chunk)
        {
            byte i = 0, i2 = 0;
            var tempBuf = (byte[])m_decryptionKey.Clone();

            for (int j = 0; j < chunk.Length; j++)
            {
                i++;
                i2 += tempBuf[i];
                (tempBuf[i2], tempBuf[i]) = (tempBuf[i], tempBuf[i2]);
                chunk[j] ^= tempBuf[(byte)(tempBuf[i] + tempBuf[i2])];
            }
        }

        private void UpdateDecryptionKey()
        {
            var keyData = new List<byte>();

            if (m_build > 285 || m_offset > 0)
            {
                AddKeyString (keyData, m_appName);
                AddKeyString (keyData, m_copyright);
                AddKeyString (keyData, m_editorFilename);
            }
            else
            {
                AddKeyString (keyData, m_editorFilename);
                AddKeyString (keyData, m_appName);
                AddKeyString (keyData, m_copyright);
            }

            if (string.IsNullOrEmpty (m_appName) && !string.IsNullOrEmpty (m_editorFilename))
                m_appName = Path.GetFileNameWithoutExtension (m_editorFilename);

            MakeKey (keyData.ToArray());
        }

        private void AddKeyString (List<byte> keyData, string str)
        {
            if (string.IsNullOrEmpty (str)) return;

            foreach (char c in str)
            {
                if ((c & 0xFF) != 0) keyData.Add ((byte)(c & 0xFF));
                if (((c >> 8) & 0xFF) != 0) keyData.Add ((byte)((c >> 8) & 0xFF));
            }
        }

        private void MakeKey (byte[] data)
        {
            if (data.Length == 0) return;

            byte lastKeyByte = 0, v34 = 0;

            for (int i = 0; i < data.Length; i++)
            {
                v34 = (byte)((v34 << 7) + (v34 >> 1));
                data[i] ^= v34;
                lastKeyByte += (byte)(data[i] * ((v34 & 1) + 2));
            }

            Array.Resize (ref data, 256);
            if (data.Length < 255) data[data.Length + 1] = lastKeyByte;

            for (int i = 0; i < 256; i++) m_decryptionKey[i] = (byte)i;

            byte accum = 0, hash = 0, i2 = 0, key = 0;
            bool never_reset = true;

            for (uint i = 0; i < 256; i++, key++)
            {
                hash = (byte)((hash << 7) | (hash >> 1));

                if (never_reset)
                    accum += (byte)(((hash & 1) == 0 ? 2 : 3) * data[key]);

                if (hash == data[key])
                {
                    hash = key = 0;
                    never_reset = false;
                }

                i2 += (byte)((hash ^ data[key]) + m_decryptionKey[i]);
                (m_decryptionKey[i2], m_decryptionKey[i]) = (m_decryptionKey[i], m_decryptionKey[i2]);
            }
        }

        private string ReadCTString (byte[] data)
        {
            return ReadCTStringI (data, m_unicode);
        }

        internal static string ReadCTStringI (byte[] data, bool unicode)
        {
            if (data == null || data.Length == 0) return "";
            try
            {
                int length = data.Length;
                if (unicode)
                {
                    for (int i = 0; i < data.Length - 1; i += 2)
                        if (data[i] == 0 && data[i + 1] == 0) { length = i; break; }
                    return Encoding.Unicode.GetString (data, 0, length);
                }
                else
                {
                    for (int i = 0; i < data.Length; i++)
                        if (data[i] == 0) { length = i; break; }
                    return Encoding.ASCII.GetString (data, 0, length);
                }
            }
            catch { return ""; }
        }

        private void AddTextEntry (string filename, string text)
        {
            if (string.IsNullOrEmpty (text)) return;

            m_entries.Add (new CcnEntry
            {
                Name        = "Text/" + filename,
                Type        = "script",
                Offset      = 0,
                Size        = (uint)Encoding.UTF8.GetByteCount (text),
                TextContent = text
            });
        }

        private void ReadFrame (byte[] data)
        {
            using (var stream = new BinMemoryStream (data))
            using (var reader = new BinaryReader (stream))
            {
                int frameIndex = 0;
                while (stream.Position < stream.Length - 8)
                {
                    short subChunkId   = reader.ReadInt16();
                    short subChunkFlag = reader.ReadInt16();
                    int subChunkSize   = reader.ReadInt32();

                    if (subChunkSize < 0 || subChunkSize > stream.Length - stream.Position)
                        break;

                    try
                    {
                        var subData = reader.ReadBytes (subChunkSize);

                        // Process sub-chunk data
                        if (subChunkFlag == 1)
                            subData = Utils.Decompress (subData);
                        else if (subChunkFlag == 2)
                            TransformChunk (subData);
                        else if (subChunkFlag == 3)
                            subData = DecodeMode3 (subData, subChunkId);

                        switch (subChunkId)
                        {
                            case CHUNK_FRAMENAME: // 0x3335
                                var frameName = ReadCTString (subData);
                                if (!string.IsNullOrEmpty (frameName))
                                {
                                    AddTextEntry ($"Frames/Frame_{frameIndex:D3}_{Utils.CleanFileName (frameName)}.txt",
                                                $"Frame {frameIndex}: {frameName}");
                                }
                                break;

                            case CHUNK_FRAME_HEAD: // 0x3334 - frame header
                                frameIndex++;
                                break;

                            case CHUNK_LAST: // 0x7F7F - end of this frame, not all frames
                                if (subChunkSize == 0) // Only if it's a real end marker
                                    frameIndex++;
                                break;
                        }
                    }
                    catch { }
                }
            }
        }

        private delegate void BankEntryReader (BinaryReader reader, uint handle, ref string name, ref byte[] data);

        private void ReadBank (byte[] data, string folder, string prefix, BankEntryReader entryReader)
        {
            if (data == null || data.Length < 4) return;

            using (var stream = new BinMemoryStream (data))
            using (var reader = new BinaryReader (stream))
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > 10000) return;

                for (int i = 0; i < count && stream.Position < stream.Length - 12; i++)
                {
                    try
                    {
                        uint handle = reader.ReadUInt32();
                        if (m_fusion >= 2.5f && folder != "Images") handle--;

                        string name = null;
                        byte[] entryData = null;
                        entryReader (reader, handle, ref name, ref entryData);

                        if (entryData != null && entryData.Length > 0)
                        {
                            string ext = Utils.DetectSoundFormat (entryData);
                            string fileName = string.IsNullOrEmpty (name) ? $"{prefix}_{handle:D5}" : Utils.CleanFileName (name);

                            m_entries.Add (new CcnEntry
                            {
                                Name = $"{folder}/{fileName}.{ext}",
                                Type = "audio",
                                Offset = 0,
                                Size = (uint)entryData.Length,
                                RawData = entryData
                            });
                        }
                    }
                    catch { break; }
                }
            }
        }

        private void ReadSoundEntry (BinaryReader reader, uint handle, ref string name, ref byte[] data)
        {
            int checksum = m_fusion == 1.5f ? reader.ReadInt16() : reader.ReadInt32();
            if (m_fusion == 1.5f) reader.ReadInt16();

            reader.ReadUInt32(); // references
            int decompSize = reader.ReadInt32();
            uint flags = reader.ReadUInt32();
            reader.ReadInt32(); // frequency
            int nameLength = reader.ReadInt32();

            if ((flags & 0x20) == 0) // Compressed
            {
                int compSize = reader.ReadInt32();
                if (compSize > 0 && compSize <= reader.BaseStream.Length - reader.BaseStream.Position)
                {
                    var compData = reader.ReadBytes (compSize);
                    var unpacked = Utils.Decompress (compData);
                    ExtractNameAndData (unpacked, nameLength, ref name, ref data);
                }
            }
            else // Not compressed
            {
                if (decompSize > 0 && decompSize <= reader.BaseStream.Length - reader.BaseStream.Position)
                {
                    var rawData = reader.ReadBytes (decompSize);
                    ExtractNameAndData (rawData, nameLength, ref name, ref data);
                }
            }
        }

        private void ReadMusicEntry (BinaryReader reader, uint handle, ref string name, ref byte[] data)
        {
            int decompSize = reader.ReadInt32();
            int compSize = reader.ReadInt32();

            if (compSize > 0 && compSize <= reader.BaseStream.Length - reader.BaseStream.Position)
            {
                var musicData = reader.ReadBytes (compSize);

                try
                {
                    var unpacked = Utils.Decompress (musicData);
                    using (var musicStream = new BinMemoryStream (unpacked))
                    using (var musicReader = new BinaryReader (musicStream))
                    {
                        musicReader.ReadInt32(); // checksum
                        if (m_fusion == 1.5f) musicReader.ReadInt16();
                        musicReader.ReadUInt32(); // references
                        musicReader.ReadInt32(); // dataSize
                        musicReader.ReadUInt32(); // flags
                        musicReader.ReadInt32(); // frequency
                        int nameLength = musicReader.ReadInt32();

                        if (nameLength > 0 && nameLength * 2 <= unpacked.Length - musicStream.Position)
                        {
                            byte[] nameBytes = musicReader.ReadBytes (nameLength * 2);
                            name = ReadCTStringI (nameBytes, true);
                        }

                        data = musicReader.ReadBytes ((int)(musicStream.Length - musicStream.Position));
                    }
                }
                catch
                {
                    data = musicData;
                }
            }
        }

        private void ExtractNameAndData (byte[] unpacked, int nameLength, ref string name, ref byte[] data)
        {
            if (nameLength > 0 && nameLength * 2 <= unpacked.Length)
            {
                int actualLength = Math.Min (nameLength * 2, unpacked.Length);
                for (int i = 0; i < actualLength - 1; i += 2)
                    if (unpacked[i] == 0 && unpacked[i + 1] == 0) { actualLength = i; break; }
                name = Encoding.Unicode.GetString (unpacked, 0, actualLength);
                data = new byte[unpacked.Length - nameLength * 2];
                Array.Copy (unpacked, nameLength * 2, data, 0, data.Length);
            }
            else
                data = unpacked;
        }

        private void ReadBinaryFiles (byte[] data)
        {
            if (data == null || data.Length < 4) return;

            using (var stream = new BinMemoryStream (data))
            using (var reader = new BinaryReader (stream))
            {
                int count = reader.ReadInt32();
                if (!ArchiveFormat.IsSaneCount (count, 10000)) return;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        short nameLen = reader.ReadInt16();
                        byte[] nameBytes = reader.ReadBytes (nameLen * (m_unicode ? 2 : 1));
                        string name = ReadCTStringI (nameBytes, true);
                        int dataSize = reader.ReadInt32();
                        if (dataSize > 0 && dataSize <= stream.Length - stream.Position)
                        {
                            m_entries.Add (new CcnEntry
                            {
                                Name = $"BinaryFiles/{Utils.CleanFileName (name)}",
                                Type = FormatCatalog.Instance.GetTypeFromName (name),
                                Offset = 0,
                                Size = (uint)dataSize,
                                RawData = reader.ReadBytes (dataSize)
                            });
                        }
                    }
                    catch { break; }
                }
            }
        }
    }

    internal class MfaReader
    {
        private readonly ArcView m_file;
        private readonly List<Entry> m_entries = new List<Entry>();

        public MfaReader (ArcView file) { m_file = file; }

        public List<Entry> ReadIndex()
        {
            long pos = 32;

            while (pos < m_file.MaxOffset - 8)
            {
                Entry entry = null;

                if (m_file.View.ReadUInt32 (pos) == 0x474E5089) // PNG
                    entry = ExtractImage (pos, 0x444E4549, 0x826042AE, "png");
                else if (m_file.View.ReadUInt16 (pos) == 0xD8FF) // JPEG
                    entry = ExtractImage (pos, 0xD9FF, 0, "jpg");
                else if (m_file.View.ReadUInt32 (pos) == 0x46464952) // RIFF/WAV
                {
                    uint size = m_file.View.ReadUInt32 (pos + 4) + 8;
                    if (size < 0x1000000)
                    {
                        entry = new Entry
                        {
                            Name = $"MFA_Sounds/sound_{m_entries.Count:D5}.wav",
                            Type = "audio",
                            Offset = pos,
                            Size = size
                        };
                    }
                }

                if (entry != null)
                {
                    m_entries.Add (entry);
                    pos = entry.Offset + entry.Size;
                }
                else
                    pos++;
            }

            return m_entries;
        }

        private Entry ExtractImage (long pos, uint endMarker, uint crc, string ext)
        {
            long searchEnd = Math.Min (pos + 0x100000, m_file.MaxOffset - 8);
            int markerSize = ext == "png" ? 8 : 2;

            for (long i = pos + 8; i < searchEnd; i++)
            {
                if ((ext == "png" && m_file.View.ReadUInt32 (i) == endMarker && m_file.View.ReadUInt32 (i + 4) == crc) ||
                    (ext == "jpg" && m_file.View.ReadUInt16 (i) == endMarker))
                {
                    return new Entry
                    {
                        Name = $"MFA_Images/image_{m_entries.Count:D5}.{ext}",
                        Type = "image",
                        Offset = pos,
                        Size = (uint)(i + markerSize - pos)
                    };
                }
            }
            return null;
        }
    }

    internal static class Utils
    {
        public static byte[] Decompress (byte[] data, int offset = 0, int size = -1)
        {
            try
            {
                if (size == -1)
                {
                    if (data.Length > 8)
                    {
                        int compSize = BitConverter.ToInt32 (data, 4);
                        if (compSize > 0 && compSize + 8 <= data.Length)
                        {
                            offset = 8;
                            size = compSize;
                        }
                    }

                    if (size == -1)
                    {
                        offset = 0;
                        size = data.Length;
                    }
                }

                using (var input = new BinMemoryStream (data, offset, size))
                using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    zstream.CopyTo (output);
                    return output.ToArray();
                }
            }
            catch (Exception ex) 
            {
                System.Diagnostics.Debug.WriteLine ($"Failed ZLibStream decompression: {ex.Message}");
                try
                {
                    using (var input = new BinMemoryStream (data, offset, size))
                    using (var deflate = new System.IO.Compression.DeflateStream (input, System.IO.Compression.CompressionMode.Decompress))
                    using (var output = new MemoryStream())
                    {
                        deflate.CopyTo (output);
                        return output.ToArray();
                    }
                }
                catch (Exception iex) { 
                    System.Diagnostics.Debug.WriteLine ($"Failed DeflateStream decompression: {iex.Message}");
                    return data; 
                }
            }
        }

        public static byte[] Compress (byte[] data)
        {
            using (var output = new MemoryStream())
            using (var zstream = new ZLibStream (output, CompressionMode.Compress))
            {
                zstream.Write (data, 0, data.Length);
                zstream.Close();
                return output.ToArray();
            }
        }

        public static string DetectSoundFormat (byte[] data)
        {
            if (data.Length < 4) return "bin";

            uint sig = BitConverter.ToUInt32 (data, 0);

            if (sig == 0x46464952) return "wav"; // RIFF
            if (sig == 0x5367674F) return "ogg"; // OggS
            if (sig == 0x43614C66) return "flac"; // fLaC
            if ((data[0] == 0xFF && (data[1] & 0xE0) == 0xE0) || sig == 0x334449) return "mp3";

            return "bin";
        }

        public static string CleanFileName (string name)
        {
            if (string.IsNullOrEmpty (name)) return "unnamed";

            // Remove null terminators and clean
            name = name.TrimEnd('\0');

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = string.Join ("_", name.Split (invalid, StringSplitOptions.RemoveEmptyEntries));

            // Limit length
            if (cleaned.Length > 100)
                cleaned = cleaned.Substring (0, 100);

            return string.IsNullOrEmpty (cleaned) ? "unnamed" : cleaned;
        }
    }

    internal class CcnEntry : Entry
    {
        public  bool IsCompressed { get; set; }
        public string TextContent { get; set; }
        public     byte[] RawData { get; set; }
        public     short BankFlag { get; set; }
        public   long ImageOffset { get; set; }
        public   CcnReader Reader { get; set; } = null;
    }
}