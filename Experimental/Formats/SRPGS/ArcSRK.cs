using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.SRPGStudio
{
    [Export(typeof(ArchiveFormat))]
    public class SrkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SRK/SRPG"; } }
        public override string Description { get { return "SRPG Studio encrypted asset"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  false; } }

        internal static readonly string[] KnownKeys = { "keyset", "_dynamic" };

        internal static SrpgCrypto LastCrypto = null;

        public SrkOpener()
        {
            Extensions = new string[] { "srk" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".srk"))
                return null;

            if (file.MaxOffset < 8)
                return null;

            var crypto = DetectKey (file);
            if (crypto == null)
                crypto = new SrpgCrypto ("keyset");

            LastCrypto = crypto; // Update shared cache

            var header = file.View.ReadBytes (0, Math.Min (16, (uint)file.MaxOffset));
            var decryptedHeader = crypto.Decrypt (header);
            uint signature = 0;
            if (decryptedHeader.Length >= 4)
                signature = decryptedHeader.ToUInt32 (0);

            var res = AutoEntry.DetectFileType (signature);
            string ext = res.Extensions.FirstOrDefault() ?? "bin";
            string type = res.Type;

            var entry = new SrpgEntry {
                Name = Path.GetFileNameWithoutExtension (file.Name) + "." + ext,
                Type = type,
                Offset = 0,
                Size = (uint)file.MaxOffset,
                IsPacked = true,
                Crypto = crypto
            };

            var dir = new List<Entry> { entry };
            return new ArcFile (file, this, dir);
        }

        private SrpgCrypto DetectKey (ArcView file)
        {
            // Try cached key first
            if (LastCrypto != null)
            {
                var testHeader = file.View.ReadBytes (0, Math.Min (16, (uint)file.MaxOffset));
                var testDecrypt = LastCrypto.Decrypt (testHeader);
                if (testDecrypt.Length >= 4)
                {
                    uint testSig = testDecrypt.ToUInt32 (0);
                    var testRes = AutoEntry.DetectFileType (testSig);
                    if (testRes.Extensions.Any() && testRes.Extensions.First() != "exe")
                        return LastCrypto;
                }
            }

            // Detect new key
            using (var stream = file.CreateStream())
            {
                var crypto = DetectKeyFromStream (file.Name, stream);
                if (crypto != null)
                    return crypto;
            }

            var options = GetDefaultOptions() as SrpgOptions;
            if (!string.IsNullOrEmpty (options?.Password))
                return new SrpgCrypto (options.Password);

            return null;
        }

        internal static SrpgCrypto DetectKeyFromStream (string filename, IBinaryStream stream)
        {
            var customKey = FindKeyForSrk (filename);
            if (customKey != null)
                return customKey;

            foreach (var keyStr in KnownKeys)
            {
                var crypto = new SrpgCrypto (keyStr);
                stream.Position = 0;
                var testData = stream.ReadBytes((int)Math.Min (16, stream.Length));
                var decrypted = crypto.Decrypt (testData);

                if (decrypted.Length >= 4)
                {
                    uint sig = decrypted.ToUInt32 (0);
                    var res = AutoEntry.DetectFileType (sig);
                    if (res.Extensions.Any() && res.Extensions.First() != "exe")
                        return crypto;
                }
            }

            return null;
        }

        internal static SrpgCrypto FindKeyForSrk (string filename)
        {
            var currentDir = VFS.GetDirectoryName (filename);
            bool doRoot = false;

            while (!string.IsNullOrEmpty (currentDir) || doRoot)
            {
                var dtsPath = VFS.CombinePath (currentDir, "data.dts");
                try
                {
                    var entry = VFS.FindFile (dtsPath);
                    if (entry != null)
                    {
                        using (var dtsView = VFS.OpenView (entry))
                        {
                            if (dtsView.View.AsciiEqual (0, "SDTS"))
                            {
                                var key = ExtractKeyFromDts (dtsView, filename);
                                if (key != null)
                                    return key;
                            }
                        }
                    }
                }
                catch (Exception) { }

                if (doRoot) break;

                var newDir = VFS.GetDirectoryName (currentDir);
                if ((string.IsNullOrEmpty (newDir) && !string.IsNullOrEmpty (currentDir)) 
                    || newDir == currentDir)
                    doRoot = true;
                currentDir = newDir;
            }

            return null;
        }

        private static SrpgCrypto ExtractKeyFromDts (ArcView dtsView, string srkFilename)
        {
            var isEncrypted = dtsView.View.ReadUInt32 (4) == 1;
            if (!isEncrypted)
                return new SrpgCrypto ("keyset");

            var version = dtsView.View.ReadUInt32 (8);
            uint numSections = version < 0x474 ? 35u : 36u;
            uint headerSize = 24 + numSections * 4;
            uint projectOffset = dtsView.View.ReadUInt32 (20) + headerSize;

            if (projectOffset + 32 <= dtsView.MaxOffset)
            {
                var projectHeader = dtsView.View.ReadBytes (projectOffset, 32);
                if (IsEncryptedBuffer (projectHeader))
                {
                    foreach (var key in KnownKeys)
                    {
                        var projectCrypto = new SrpgCrypto (key);
                        var testDecrypt = projectCrypto.Decrypt (projectHeader);

                        if (!IsEncryptedBuffer (testDecrypt))
                        {
                            if (key == "_dynamic")
                            {
                                var assetKeyBytes = MD5.Create().ComputeHash (testDecrypt, 0, 16);
                                var assetCrypto = new SrpgCrypto (assetKeyBytes);
                                if (TestKeyForSrk (srkFilename, assetCrypto))
                                    return assetCrypto;
                            }
                            return projectCrypto;
                        }
                    }
                }
                else
                {
                    return new SrpgCrypto ("keyset");
                }
            }

            return null;
        }

        private static bool TestKeyForSrk (string filename, SrpgCrypto crypto)
        {
            try
            {
                var entry = VFS.FindFile (filename);
                if (entry != null)
                {
                    using (var stream = VFS.OpenBinaryStream (entry))
                    {
                        var testData = stream.ReadBytes((int)Math.Min (16, stream.Length));
                        var decrypted = crypto.Decrypt (testData);

                        if (decrypted.Length >= 4)
                        {
                            uint sig = decrypted.ToUInt32 (0);
                            var res = AutoEntry.DetectFileType (sig);
                            return res.Extensions.Any() && res.Extensions.First() != "exe";
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool IsEncryptedBuffer (byte[] buffer)
        {
            if (buffer.Length < 28) return false;
            try
            {              
                return BitConverter.ToUInt32 (buffer, 16) > 255 || 
                       BitConverter.ToUInt32 (buffer, 20) > 1023 || 
                       BitConverter.ToUInt32 (buffer, 24) > 1023;
            }
            catch
            {
                return true;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var srpgEntry = entry as SrpgEntry;
            if (srpgEntry == null || srpgEntry.Crypto == null)
                return base.OpenEntry (arc, entry);

            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            var decrypted = srpgEntry.Crypto.Decrypt (data);
            return new BinMemoryStream (decrypted, entry.Name);
        }

        public override ResourceOptions GetDefaultOptions()
        {
            return new SrpgOptions { Password = "keyset" };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            return GetDefaultOptions();
        }
    }
}