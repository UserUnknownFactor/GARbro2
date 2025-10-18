using System.ComponentModel.Composition;
using System.IO;
using System.Text;

namespace GameRes.Formats.NScripter
{
    [Export(typeof(ScriptFormat))]
    public class NSOpener : GenericScriptFormat
    {
        public override string          Tag { get => "NScripter"; }
        public override string  Description { get => "NScripter engine encrypted script"; }
        public override  uint     Signature { get =>  0; }
        public override ScriptType DataType { get =>  ScriptType.PlainText; }

        public override bool IsScript (IBinaryStream file)
        {
            return file.Name.EndsWith ("nscript.dat");
        }

        private static byte XOR_KEY = 0x84;

        public override Stream ConvertFrom (IBinaryStream file)
        {
            var xoredStream = new XoredStream (file.AsStream, XOR_KEY);
            byte[] decryptedBytes;
            using (var ms = new MemoryStream())
            {
                xoredStream.CopyTo (ms);
                decryptedBytes = ms.ToArray();
            }
            var sourceEncoding = Encodings.cp932;
            string text = sourceEncoding.GetString (decryptedBytes);
            byte[] utf8Bytes = Encoding.UTF8.GetBytes (text);
            
            return new MemoryStream (utf8Bytes);
        }
        
        public override Stream ConvertBack (IBinaryStream file)
        {
            byte[] utf8Bytes;
            using (var ms = new MemoryStream())
            {
                file.AsStream.CopyTo (ms);
                utf8Bytes = ms.ToArray();
            }
            string text = Encoding.UTF8.GetString (utf8Bytes);
            var targetEncoding = Encodings.cp932;
            byte[] targetBytes = targetEncoding.GetBytes (text);

            var memStream = new MemoryStream (targetBytes);
            return new XoredStream (memStream, XOR_KEY);
        }
    }
}