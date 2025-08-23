using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GameRes.Formats.NScripter
{
    [Export(typeof(ScriptFormat))]
    public class DsmConverter : GenericScriptFormat
    {
        public override string         Tag { get => "DSM/UTAGE"; }
        public override string Description { get => "UTAGE Unity engine script file"; }
        public override uint     Signature { get => 0; }

        public override bool IsScript (IBinaryStream file)
        {
            return VFS.IsPathEqualsToFileName (file.Name, "data.dsm");
        }

        public override Stream ConvertFrom (IBinaryStream file)
        {
            using (var reader = new StreamReader (file.AsStream))
            {
                var sourceString = reader.ReadToEnd();
                var text = DsmDecryptor.DecryptString (sourceString, "pass");
                return new BinMemoryStream (text, file.Name);
            }
        }
        
        public override Stream ConvertBack (IBinaryStream file)
        {
            throw new NotSupportedException();
        }
    }

    internal static class DsmDecryptor
    {
        internal static void GenerateKeyFromPassword (string password, int keySize, out byte[] key, int blockSize, out byte[] iv)
        {
            var bytes = Encoding.UTF8.GetBytes ("saltは必ず8バイト以上");
            var derive = new Rfc2898DeriveBytes (password, bytes);
            derive.IterationCount = 1000;
            key = derive.GetBytes (keySize / 8);
            iv = derive.GetBytes (blockSize / 8);
        }

        internal static byte[] DecryptString (string sourceString, string password)
        {
            var rij = new RijndaelManaged();
            byte[] key, iv;
            GenerateKeyFromPassword (password, rij.KeySize, out key, rij.BlockSize, out iv);
            rij.Key = key;
            rij.IV = iv;
            var array = Convert.FromBase64String (sourceString);
            using (var cryptoTransform = rij.CreateDecryptor())
            {
                return cryptoTransform.TransformFinalBlock (array, 0, array.Length);
            }
        }
    }
}
