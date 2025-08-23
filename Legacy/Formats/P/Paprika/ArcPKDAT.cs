using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Paprika
{
    [Export(typeof(ArchiveFormat))]
    public class PkDatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/PAPRIKA"; } }
        public override string Description { get { return "Paprika resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        string[] KnownPkList = new[] { null, null, null, "PICPK", "AVIPK", "MUSPK", "WAVPK" };

        public override ArcFile TryOpen (ArcView file)
        {
            var scn_name = VFS.ChangeFileName (file.Name, "SCNPK.DAT");
            if (!VFS.FileExists (scn_name))
                return null;
            var arc_name = Path.GetFileName (file.Name).ToUpperInvariant();
            var base_name = Path.GetFileNameWithoutExtension (arc_name);
            if (!char.IsDigit (base_name, base_name.Length - 1))
                return null;
            int arcId;
            for (arcId = 3; arcId < KnownPkList.Length; ++arcId)
            {
                if (KnownPkList[arcId] != null && arc_name.StartsWith (KnownPkList[arcId]))
                    break;
            }
            if (arcId == KnownPkList.Length)
                return null;
            int arc_num = base_name[base_name.Length-1] - '0';
            var base_ext = arc_name.Substring (0, 3);
            using (var scn = VFS.OpenBinaryStream (scn_name))
            {
                scn.Position = arcId * 4;
                uint index_pos = scn.ReadUInt32();
                if (0 == index_pos)
                    return null;
                scn.Position = index_pos;
                var dir = new List<Entry>();
                long last_offset = -1;
                int i = 0;
                while (scn.PeekByte() != -1)
                {
                    ++i;
                    byte num = scn.ReadUInt8();
                    long offset = scn.ReadUInt32();
                    uint size = scn.ReadUInt32();
                    if (num != arc_num)
                        continue;
                    if (offset < last_offset)
                        break;
                    var name = string.Format ("{0:D4}.{1}", i - 1,  base_ext);
                    var entry = Create<Entry> (name);
                    entry.Offset = offset;
                    entry.Size = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    last_offset = offset;
                }
                if (dir.Count == 0)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }
    }
}
