using System;
using System.IO;
using System.Text;
using System.Linq;
using System.ComponentModel.Composition;
using System.Security.Cryptography;

namespace GameRes.Formats.Crowd
{
    [Export(typeof(ScriptFormat))]
    public class AnimScriptFormat : ScriptFormat
    {
        public override string          Tag { get { return "DAT/ANIM"; } }
        public override string  Description { get { return "ANIM encrypted scenario file"; } }
        public override uint      Signature { get { return  MAGIC; } }
        public override ScriptType DataType { get { return  ScriptType.TextData; } }

        private const int HEADER_SIZE = 0x14;
        private const int KEY_SIZE = 0x10;
        private const int KEY_OFFSET = 4;
        private const int MAGIC = 0x01000000;

        public AnimScriptFormat ()
        {
            Extensions = new[] { "dat" };
        }

        public override bool IsScript (IBinaryStream file)
        {
            if (file.Length < HEADER_SIZE)
                return false;

            return file.Signature == MAGIC;
        }

        public override Stream ConvertFrom (IBinaryStream file)
        {
            if (!file.Name.EndsWith (".dat", StringComparison.OrdinalIgnoreCase))
                return null;

            var data      = ReadFileData (file);
            var decrypted = DecryptData (data);
            var textData  = ExtractTextData (decrypted, data.Length);

            return textData != null ? new MemoryStream (textData) : null;
        }

        public override Stream ConvertBack (IBinaryStream file)
        {
            var textData = ReadFileData (file);
            return CreateEncryptedStream (textData);
        }

        public override ScriptData Read (string name, Stream file)
        {
            return Read (name, file, Encoding.GetEncoding (932)); // Shift-JIS
        }

        public override ScriptData Read (string name, Stream file, Encoding encoding)
        {
            if (!name.EndsWith (".dat", StringComparison.OrdinalIgnoreCase))
                return null;

            var data = ReadStreamData (file);
            var decrypted = DecryptData (data);

            // Build full decrypted file (header + decrypted content)
            var fullDecrypted = new byte[HEADER_SIZE + decrypted.Length];
            Array.Copy (data, 0, fullDecrypted, 0, HEADER_SIZE);
            Array.Copy (decrypted, 0, fullDecrypted, HEADER_SIZE, decrypted.Length);

            int textOffset = HEADER_SIZE;
            if (fullDecrypted.Length > 0x1C)
                textOffset = BitConverter.ToInt32 (fullDecrypted, 0x18) + HEADER_SIZE;

            if (textOffset >= Math.Min (fullDecrypted.Length, file.Length))
                return null;

            int textLength = fullDecrypted.Length - textOffset;
            var textBytes = new byte[textLength];
            Array.Copy (fullDecrypted, textOffset, textBytes, 0, textLength);

            var text = encoding.GetString (textBytes);
            return new ScriptData (text, DataType) { Encoding = encoding };
        }

        public override void Write (Stream file, ScriptData script)
        {
            var textBytes = script.Encoding.GetBytes (script.RawText);
            var fullData = new byte[HEADER_SIZE + textBytes.Length];
            Array.Copy (textBytes, 0, fullData, HEADER_SIZE, textBytes.Length);

            WriteEncryptedData (file, fullData);
        }

        private byte[] ReadFileData (IBinaryStream file)
        {
            var data = new byte[file.Length];
            file.Position = 0;
            file.Read (data, 0, data.Length);
            return data;
        }

        private byte[] ReadStreamData (Stream stream)
        {
            using (var ms = new MemoryStream())
            {
                stream.CopyTo (ms);
                return ms.ToArray();
            }
        }

        private byte[] ExtractKey (byte[] data)
        {
            var key = new byte[KEY_SIZE];
            Array.Copy (data, KEY_OFFSET, key, 0, KEY_SIZE);
            return key;
        }

        private byte[] DecryptData (byte[] data)
        {
            var key = ExtractKey (data);

            using (var input  = new MemoryStream (data, HEADER_SIZE, data.Length - HEADER_SIZE))
            using (var crypto = new InputCryptoStream (input, new GaxTransform (key, false)))
            using (var output = new MemoryStream())
            {
                crypto.CopyTo (output);
                return output.ToArray();
            }
        }

        private byte[] ExtractTextData (byte[] decrypted, long fileLength)
        {
            int textOffset = 0;
            if (decrypted.Length > 4)
            {
                textOffset  = BitConverter.ToInt32 (decrypted, 0x04) + HEADER_SIZE;
                textOffset -= HEADER_SIZE;
            }

            if (decrypted.Length - textOffset > 0 && decrypted.Length - textOffset < fileLength)
            {
                var textData = new byte[decrypted.Length - textOffset];
                Array.Copy (decrypted, textOffset, textData, 0, textData.Length);
                return textData;
            }
            return null;
        }

        private Stream CreateEncryptedStream (byte[] textData)
        {
            var output = new MemoryStream();
            WriteEncryptedData (output, textData);
            output.Position = 0;
            return output;
        }

        private void WriteEncryptedData (Stream output, byte[] data)
        {
            var key = GenerateRandomKey();
            var header = CreateHeader (key);

            output.Write (header, 0, header.Length);

            using (var input = new MemoryStream (data, HEADER_SIZE, data.Length - HEADER_SIZE))
            using (var crypto = new CryptoStream (output, new GaxTransform (key, true), CryptoStreamMode.Write))
            {
                input.CopyTo (crypto);
                crypto.FlushFinalBlock();
            }
        }

        private byte[] CreateHeader (byte[] key)
        {
            var header = new byte[HEADER_SIZE];
            header[0] = 0x00; header[1] = 0x00; header[2] = 0x00; header[3] = 0x01;
            Array.Copy (key, 0, header, KEY_OFFSET, KEY_SIZE);
            return header;
        }

        private byte[] GenerateRandomKey()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                var key = new byte[KEY_SIZE];
                //rng.GetBytes (key);
                return key;
            }
        }
    }
}