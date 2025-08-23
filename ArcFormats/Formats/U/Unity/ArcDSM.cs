using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GameRes.Formats.Unity
{
    [Export(typeof(ArchiveFormat))]
    public class DsmOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DSM/UNITY"; } }
        public override string Description { get { return "Unity engine encrypted script file"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        const string DefaultPassword = "pass";

        public override ArcFile TryOpen (ArcView file)
        {
            if (!VFS.IsPathEqualsToFileName (file.Name, "data.dsm")
                || (file.View.ReadUInt32 (0) & 0xFFFFFF) != 0xBFBBEF) // UTF-8 BOM
                return null;

            var dir = new List<Entry> {
                new Entry {
                    Name = "data.txt",
                    Type = "script",
                    Offset = 0,
                    Size = (uint)file.MaxOffset / 4 * 3,
                }
            };
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            using (var input = arc.File.CreateStream())
            using (var reader = new StreamReader (input))
            {
                var sourceString = reader.ReadToEnd();
                var text = DecryptString (sourceString, DefaultPassword);
                return new BinMemoryStream (text, entry.Name);
            }
        }

        static byte[] DecryptString (string sourceString, string password)
        {
            var rijndaelManaged = new RijndaelManaged();
            byte[] key, iv;
            GenerateKeyFromPassword (password, rijndaelManaged.KeySize, out key, rijndaelManaged.BlockSize, out iv);
            rijndaelManaged.Key = key;
            rijndaelManaged.IV = iv;
            var array = Convert.FromBase64String (sourceString);
            using (var cryptoTransform = rijndaelManaged.CreateDecryptor())
            {
                return cryptoTransform.TransformFinalBlock (array, 0, array.Length);
            }
        }

        static readonly byte[] DefaultSalt = Encoding.UTF8.GetBytes("saltは必ず8バイト以上");

        static void GenerateKeyFromPassword (string password, int keySize, out byte[] key, int blockSize, out byte[] iv)
        {
            var rfc2898DeriveBytes = new Rfc2898DeriveBytes (password, DefaultSalt);
            rfc2898DeriveBytes.IterationCount = 1000;
            key = rfc2898DeriveBytes.GetBytes (keySize / 8);
            iv = rfc2898DeriveBytes.GetBytes (blockSize / 8);
        }
    }
}
