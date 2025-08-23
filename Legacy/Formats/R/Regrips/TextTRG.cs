using System;
using System.IO;
using System.Text;
using System.Linq;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Regrips
{   
    [Export(typeof(ScriptFormat))]
    public class TrgScriptFormat : ScriptFormat
    {
        public override string          Tag { get { return "TRG/TEXT"; } }
        public override string  Description { get { return "Regrips encrypted scenario file"; } }
        public override uint      Signature { get { return  0; } }
        public override ScriptType DataType { get { return  ScriptType.TextData; } }

        public void BinScriptFormat()
        {
            Extensions = new[] { "trg" };
        }

        public override bool IsScript(IBinaryStream file)
        {
            var ext = Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant();
            return Extensions != null && Extensions.Contains(ext);
        }

        public override Stream ConvertFrom(IBinaryStream file)
        {
            throw new NotSupportedException("Binary script conversion not implemented");
        }

        public override Stream ConvertBack(IBinaryStream file)
        {
            throw new NotSupportedException("Binary script conversion not implemented");
        }

        public override ScriptData Read(string name, Stream file)
        {
            return Read (name, file, Encoding.GetEncoding(932));
        }

        public override ScriptData Read(string name, Stream file, Encoding e)
        {
            byte[] data;
            
            using (var input = new XoredStream (file, 0xFF))
            using (var ms = new MemoryStream())
            {
                input.CopyTo(ms);
                data = ms.ToArray();
            }

            var text = e.GetString(data);
            var scriptData = new ScriptData(text, ScriptType.BinaryScript) { Encoding = e };
            return scriptData;
        }

        public override void Write(Stream file, ScriptData script)
        {
            throw new NotSupportedException("Binary script writing not implemented");
        }
    }
}