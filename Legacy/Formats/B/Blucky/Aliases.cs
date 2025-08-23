using System.ComponentModel.Composition;

// [970627][Blucky] Rekiai

namespace GameRes.Formats.Blucky
{
    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "OSA")]
    [ExportMetadata("Target", "BMP")]
    public class OsaFormat : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "WF")]
    [ExportMetadata("Target", "WAV")]
    public class WfFormat : ResourceAlias { }
}
