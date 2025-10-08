using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Bruns
{
    [Export(typeof(ScriptFormat))]
    public class EencScriptFormat : ScriptFormat
    {
        public override string          Tag { get { return "EENZ/TEXT"; } }
        public override string  Description { get { return "Bruns encrypted script file"; } }
        public override uint      Signature { get { return  0x5A4E4545; } } // 'EENZ'
        public override ScriptType DataType { get { return  ScriptType.TextData; } }
        public override bool       CanWrite { get { return  true; } }

        public EencScriptFormat()
        {
            Extensions = new[] { "bso", "txt" };
            Signatures = new uint[] { 0x5A4E4545 }; // 'EENZ'
        }

        public override bool IsScript(IBinaryStream file)
        {
            if (file.Length < 8)
                return false;
            
            var header = file.ReadHeader(4);
            return header[0] == 'E' && header[1] == 'E' && header[2] == 'N' && 
                   (header[3] == 'C' || header[3] == 'Z');
        }

        public override Stream ConvertFrom(IBinaryStream file)
        {
            var header = file.ReadHeader(8);
            if (header[0] != 'E' || header[1] != 'E' || header[2] != 'N')
                return null;

            bool compressed = header[3] == 'Z';
            uint originalSize = header.ToUInt32(4);
            uint key = originalSize ^ EencFormat.EencKey;

            Stream input = new StreamRegion(file.AsStream, 8, true);
            input = new EencStream(input, key);
            
            if (compressed)
            {
                input = new ZLibStream(input, CompressionMode.Decompress);
            }

            var output = new MemoryStream();
            input.CopyTo(output);
            input.Dispose();
            
            output.Position = 0;
            return output;
        }

        public override Stream ConvertBack(IBinaryStream file)
        {
            var textData = new byte[file.Length];
            file.Position = 0;
            file.Read(textData, 0, textData.Length);
            
            var output = new MemoryStream();
            EencFormat.WriteEncrypted(output, textData, true); // true = compress
            output.Position = 0;
            return output;
        }

        public override ScriptData Read(string name, Stream file)
        {
            return Read(name, file, Encoding.UTF8);
        }

        public override ScriptData Read(string name, Stream file, Encoding encoding)
        {
            using (var input = new BinaryStream(file, name))
            {
                var decrypted = ConvertFrom(input);
                if (decrypted == null)
                    return null;

                using (var reader = new StreamReader(decrypted, encoding))
                {
                    var text = reader.ReadToEnd();
                    return new ScriptData(text, DataType) { Encoding = encoding };
                }
            }
        }

        public override void Write(Stream file, ScriptData script)
        {
            var textBytes = script.Encoding.GetBytes(script.RawText);
            EencFormat.WriteEncrypted(file, textBytes, true); // true = compress
        }
    }
}