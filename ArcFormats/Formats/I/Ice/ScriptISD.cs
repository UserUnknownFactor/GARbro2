using GameRes.Formats.Ankh;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Ice
{
    [Export(typeof(ScriptFormat))]
    public class IsdScript : GenericScriptFormat
    {
        public override string         Tag { get => "ISD"; }
        public override string Description { get => "Ice Soft binary script"; }
        public override uint     Signature { get => 0x01575054; } // 'TPW'

        public override bool IsScript (IBinaryStream file)
        {
            return file.Signature == Signature;
        }

        public override Stream ConvertFrom (IBinaryStream file)
        {
            file.Position = 4;
            int unpacked_size = file.ReadInt32();
            var data = new byte[unpacked_size];
            GrpOpener.UnpackTpw (file, data);
            return new BinMemoryStream (data, file.Name);
        }

        public override Stream ConvertBack (IBinaryStream file)
        {
            throw new System.NotImplementedException();
        }
    }
}
