using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace GameRes.Formats.Unity
{
    [Export(typeof(ArchiveFormat))]
    public class BytesOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BYTES/UNITY"; } }
        public override string Description { get { return "Unity engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith ("DAT.bytes", StringComparison.OrdinalIgnoreCase))
                return null;
            var inf_name = file.Name.Substring (0, file.Name.Length - "DAT.bytes".Length);
            inf_name += "INF.bytes";
            if (!VFS.FileExists (inf_name))
                return null;
            using (var inf = VFS.OpenStream (inf_name))
            {
                var bin = new BinaryFormatter { Binder = new SpTypeBinder() };
                var list = bin.Deserialize (inf) as List<LinkerInfo>;
                if (null == list || 0 == list.Count)
                    return null;
                var base_name = Path.GetFileNameWithoutExtension (file.Name);
                string type = "";
                if (base_name.StartsWith ("WAVE", StringComparison.OrdinalIgnoreCase))
                    type = "audio";
                else if (base_name.StartsWith ("CG", StringComparison.OrdinalIgnoreCase))
                    type = "image";
                var dir = list.Select (e => new Entry {
                    Name = e.name, Type = type, Offset = e.offset, Size = (uint)e.size
                }).ToList();
                return new ArcFile (file, this, dir);
            }
        }
    }

    [Serializable]
    public class LinkerInfo
    {
        public string   name { get; set; }
        public int    offset { get; set; }
        public int      size { get; set; }
    }

    internal class SpTypeBinder : SerializationBinder
    {
        public override Type BindToType (string assemblyName, string typeName)
        {
            if ("Assembly-CSharp" == assemblyName && "SpVM.Library.LinkerInfo" == typeName)
            {
                return typeof(LinkerInfo);
            }
            if (assemblyName.StartsWith ("mscorlib,") && typeName.StartsWith ("System.Collections.Generic.List`1[[SpVM.Library.LinkerInfo"))
            {
                return typeof(List<LinkerInfo>);
            }
            return null;
        }
    }
}
