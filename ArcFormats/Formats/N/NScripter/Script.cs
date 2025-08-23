using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.NScripter
{
    [Export(typeof(ScriptFormat))]
    public class NSOpener : GenericScriptFormat
    {
        public override string         Tag { get => "NScripter"; }
        public override string Description { get => "NScripter engine script file"; }
        public override uint     Signature { get => 0; }

        public override bool IsScript (IBinaryStream file)
        {
            return VFS.IsPathEqualsToFileName (file.Name, "nscript.dat");
        }

        public override Stream ConvertFrom (IBinaryStream file)
        {
            return new XoredStream (file.AsStream, 0x84);
        }
        
        public override Stream ConvertBack (IBinaryStream file)
        {
            return new XoredStream (file.AsStream, 0x84);
        }
    }
}
