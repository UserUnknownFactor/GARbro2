using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.RedZone
{
    [Export(typeof(ScriptFormat))]
    public class QdoOpener : GenericScriptFormat
    {
        public override string         Tag => "QDO";
        public override string Description => "Red-Zone script file";
        public override uint     Signature => 0x5F4F4451; // 'QDO_SHO'

        public override bool IsScript (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            return header.AsciiEqual ("QDO_SHO");
        }

        const int ScriptDataPos = 0x0E;

        public override Stream ConvertFrom (IBinaryStream file)
        {
            var data = file.ReadBytes ((int)file.Length);
            if (data[0xC] != 0)
            {
                for (int i = ScriptDataPos; i < data.Length; ++i)
                {
                    data[i] = (byte)~(data[i] - 13);
                }
                data[0xC] = 0;
            }
            return new BinMemoryStream (data, file.Name);
        }
        
        public override Stream ConvertBack (IBinaryStream file)
        {
            var data = file.ReadBytes ((int)file.Length);
            if (data[0xC] == 0)
            {
                for (int i = ScriptDataPos; i < data.Length; ++i)
                {
                    data[i] = (byte)(~data[i] + 13);
                }
                data[0xC] = 1;
            }
            return new BinMemoryStream (data, file.Name);
        }
    }
}
