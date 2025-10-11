using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Qlie
{
    public class QliePackWriter : IDisposable
    {
        private static readonly string HashVersion = "HashVer1.4";

        private Stream m_output;
        private List<QlieEntry> m_entries;
        private Version m_version;
        private IEncryption m_encryption;
        private byte[] m_key_data;
        private byte[] m_pack_key_file;
        private Encoding m_encoding;
        private Dictionary<QlieEntry, byte[]> m_entryData;
        private long m_indexOffset;
        private long m_hashDataOffset;

        public byte[] GameKey { get; set; }

        public QliePackWriter (Stream output, Version version)
        {
            m_output = output;
            m_version = version;
            m_entries = new List<QlieEntry>();
            m_entryData = new Dictionary<QlieEntry, byte[]>();
            m_encoding = version.Major >= 3 && version.Minor >= 1 ? Encoding.Unicode : Encodings.cp932;
        }

        public void AddEntry (string name, byte[] data, bool compress = false)
        {
            // Strip any leading directory separators
            string entryName = name.Replace('/', '\\');
            if (entryName.StartsWith ("\\"))
                entryName = entryName.Substring (1);

            var entry = new QlieEntry
            {
                Name = entryName,  // Use relative path
                Size = (uint)data.Length,
                UnpackedSize = (uint)data.Length,
                IsPacked = compress,
                Offset = 0,
                EncryptionMethod = 0
            };

            entry.RawName = m_encoding.GetBytes (entryName);  // Use relative path
            m_entries.Add (entry);
            m_entryData[entry] = data;
        }

        public void Write (byte[] arc_key = null)
        {
            // Generate key data first
            if (m_key_data == null)
                m_key_data = GenerateKeyData();

            // Create encryption object BEFORE using it
            m_encryption = QlieEncryption.CreateForWriting (m_version, m_key_data, arc_key ?? GameKey);

            // Handle pack_keyfile for version 3
            if (m_version.Major >= 3)
            {
                var existingKeyEntry = m_entries.FirstOrDefault (e =>
                    e.Name == "pack_keyfile_kfueheish15538fa9or.key" ||
                    (e.Name.StartsWith ("pack_keyfile") && e.Name.EndsWith (".key")));

                if (existingKeyEntry != null)
                {
                    m_pack_key_file = m_entryData[existingKeyEntry];
                    // Move pack_keyfile to the beginning if it's not already there
                    if (m_entries.IndexOf (existingKeyEntry) != 0)
                    {
                        m_entries.Remove (existingKeyEntry);
                        m_entries.Insert (0, existingKeyEntry);
                    }
                }
                else
                {
                    // Create new pack_keyfile
                    m_pack_key_file = CreatePackKeyFile();
                    var keyEntry = new QlieEntry
                    {
                        Name = "pack_keyfile_kfueheish15538fa9or.key",
                        RawName = m_encoding.GetBytes ("pack_keyfile_kfueheish15538fa9or.key"),
                        Size = (uint)m_pack_key_file.Length,
                        UnpackedSize = (uint)m_pack_key_file.Length,
                        IsPacked = false,
                        EncryptionMethod = 1,
                        Offset = 0
                    };
                    m_entries.Insert (0, keyEntry);
                    m_entryData[keyEntry] = m_pack_key_file;
                }

                // NOW set the PackKeyFile after encryption object is created
                m_encryption.PackKeyFile = m_pack_key_file;
            }

            WriteData();
            WriteIndex();
            if (m_version.Major >= 3)
                WriteHashData();
            WriteKey();
            WriteHeader();

            m_output.Flush();
        }

        private void WriteData ()
        {
            for (int i = 0; i < m_entries.Count; i++)
            {
                var entry = m_entries[i];
                entry.Offset = m_output.Position;
                byte[] originalData = m_entryData[entry];
                byte[] data = (byte[])originalData.Clone ();

                entry.UnpackedSize = (uint)originalData.Length;

                bool isPackKeyfile = entry.Name.Contains ("pack_keyfile") &&
                                   (entry.Name.EndsWith (".key") || entry.Name == "pack_keyfile");

                if (m_version.Major >= 3)
                {
                    if (i == 0 && isPackKeyfile)
                    {
                        entry.EncryptionMethod = 1;
                        entry.IsPacked = false;  // no compression for pack_keyfile
                    }
                    else
                        entry.EncryptionMethod = 2;
                }
                else if (m_version.Major >= 2)
                    entry.EncryptionMethod = 1;
                else
                    entry.EncryptionMethod = 0;

                //if (!isPackKeyfile) {  PackOpener.DebugBPE (data, "bpe_test_debug.txt"); throw new Exception (); }

                if (entry.IsPacked && !(i == 0 && isPackKeyfile))
                    data = PackOpener.Compress (data);

                if (entry.EncryptionMethod > 0)
                {
                    if (entry.RawName == null || entry.RawName.Length == 0)
                        entry.RawName = m_encoding.GetBytes (entry.Name);

                    m_encryption.EncryptEntry (data, 0, data.Length, entry);
                }

                entry.Size = (uint)data.Length;
                entry.Hash = m_encryption.CalculateHash (data, data.Length);

                m_output.Write (data, 0, data.Length);
            }
        }

        private void WriteIndex ()
        {
            m_indexOffset = m_output.Position;
            var writer = new BinaryWriter (m_output, Encoding.ASCII, true);

            foreach (var entry in m_entries)
            {
                byte[] encName = (byte[])entry.RawName.Clone ();
                int nameLen = encName.Length;

                if (m_encryption.IsUnicode)
                    writer.Write ((ushort)(nameLen / 2));
                else
                    writer.Write ((ushort)nameLen);

                m_encryption.EncryptName (encName, nameLen);
                writer.Write (encName, 0, nameLen);

                writer.Write ((uint)entry.Offset);
                writer.Write ((uint)0);
                writer.Write ((uint)entry.Size); 
                writer.Write ((uint)entry.UnpackedSize);
                writer.Write (entry.IsPacked ? 1u : 0u);
                writer.Write ((uint)entry.EncryptionMethod);

                if (m_encryption.IndexLayout == IndexLayout.WithHash)
                    writer.Write ((uint)entry.Hash);
            }

            writer.Flush ();
        }

        private void WriteHashData ()
        {
            m_hashDataOffset = m_output.Position;

            const int tableSize = 256;
            using (var ms = new MemoryStream())
            {
                var msWriter = new BinaryWriter (ms);

                var hashTable = new List<HashEntry>[tableSize];
                for (int i = 0; i < tableSize; i++)
                    hashTable[i] = new List<HashEntry>();

                // Build hash table
                for (int i = 0; i < m_entries.Count; i++)
                {
                    var entry = m_entries[i];
                    uint hash = 0;
                    string name = entry.Name;

                    for (int j = 0; j < name.Length; j++)
                    {
                        hash = ((uint)(name[j] << ((j + 1) & 7)) + hash) & 0x3FFFFFFF;
                    }

                    int pos = (ushort)hash + ((int)(hash >> 8)) + ((int)(hash >> 16));
                    pos = pos % tableSize;

                    hashTable[pos].Add (new HashEntry { Name = name, Hash = hash, Index = i });
                }

                // Write hash table
                for (int i = 0; i < tableSize; i++)
                {
                    msWriter.Write (hashTable[i].Count);
                    foreach (var hashEntry in hashTable[i])
                    {
                        if (m_encoding == Encoding.Unicode)
                        {
                            msWriter.Write((ushort)hashEntry.Name.Length);
                            msWriter.Write (Encoding.Unicode.GetBytes (hashEntry.Name));
                        }
                        else
                        {
                            var nameBytes = Encodings.cp932.GetBytes (hashEntry.Name);
                            msWriter.Write((ushort)nameBytes.Length);
                            msWriter.Write (nameBytes);
                        }

                        msWriter.Write((ulong)(hashEntry.Index * sizeof (int)));
                        msWriter.Write (hashEntry.Hash);
                    }
                }

                // Write index array
                for (int i = 0; i < m_entries.Count; i++)
                    msWriter.Write (i);

                msWriter.Flush();
                byte[] hashData = ms.ToArray();

                EncryptHashData (hashData);

                var writer = new BinaryWriter (m_output, Encoding.ASCII, true);

                byte[] hashSignature = new byte[16];
                Array.Copy (Encoding.ASCII.GetBytes (HashVersion), hashSignature, HashVersion.Length);

                writer.Write (hashSignature);
                writer.Write((uint)tableSize);
                writer.Write((uint)m_entries.Count);
                writer.Write((uint)(m_entries.Count * 4));
                writer.Write((uint)hashData.Length);
                writer.Write((uint)0);
                writer.Write (new byte[32]);
                writer.Write (hashData);
                writer.Flush();
            }
        }

        private class HashEntry
        {
            public string Name;
            public uint Hash;
            public int Index;
        }

        private void EncryptHashData (byte[] data)
        {
            if (data.Length < 8)
                return;

            unsafe
            {
                fixed (byte* pData = data)
                {
                    ulong* data64 = (ulong*)pData;
                    int qwords = data.Length / 8;
                    ulong hash = 0xA73C5F9DA73C5F9Dul;
                    ulong xor = ((uint)data.Length + 0x428u) ^ 0xFEC9753Eu;
                    xor |= xor << 32;
                    ulong prev = xor;

                    for (int i = 0; i < qwords; i++)
                    {
                        hash = MMX.PAddD (hash, 0xCE24F523CE24F523ul) ^ prev;
                        prev = data64[i];
                        data64[i] = prev ^ hash;
                    }
                }
            }
        }

        private void WriteKey ()
        {
            var writer = new BinaryWriter (m_output, Encoding.ASCII, true);

            byte[] signature = Encoding.ASCII.GetBytes ("8hr48uky,8ugi8ewra4g8d5vbf5hb5s6");
            uint hashSize = m_version.Major >= 3 ? (uint)(m_output.Position - m_hashDataOffset) : 0;

            uint key = m_encryption.CalculateHash (m_key_data, Math.Min (m_key_data.Length, 256)) & 0x0FFFFFFF;

            unsafe
            {
                fixed (byte* pData = signature)
                {
                    ulong* data64 = (ulong*)pData;
                    int qwords = signature.Length / 8;
                    ulong hash = 0xA73C5F9DA73C5F9Dul;
                    ulong xor = (key + (uint)signature.Length) ^ 0xFEC9753Eu;
                    xor |= xor << 32;
                    ulong prev = xor;

                    for (int i = 0; i < qwords; i++)
                    {
                        hash = MMX.PAddD (hash, 0xCE24F523CE24F523ul) ^ prev;
                        prev = data64[i];
                        data64[i] = prev ^ hash;
                    }
                }
            }

            writer.Write (signature);
            writer.Write (hashSize);
            writer.Write (m_key_data);
            writer.Write (new byte[1024 - m_key_data.Length]);
            writer.Flush();
        }

        private void WriteHeader ()
        {
            var writer = new BinaryWriter (m_output, Encoding.ASCII, true);

            // Write header at the end of file
            byte[] signature = new byte[16];
            string sig = $"FilePackVer{m_version.Major}.{m_version.Minor}";
            Array.Copy (Encoding.ASCII.GetBytes (sig), signature, Math.Min (sig.Length, 16));

            writer.Write (signature); // 16 bytes
            writer.Write((uint)m_entries.Count);
            writer.Write((uint)m_indexOffset);
            writer.Write((uint)0);

            writer.Flush();
        }

        private byte[] GenerateKeyData ()
        {
            var rng = new Random();
            var key = new byte[256];
            rng.NextBytes (key);
            return key;
        }

        private byte[] CreatePackKeyFile ()
        {
            var rng = new Random();
            var keyFile = new byte[256];
            rng.NextBytes (keyFile);
            return keyFile;
        }

        public void Dispose ()
        {
            m_output?.Dispose();
        }
    }
}