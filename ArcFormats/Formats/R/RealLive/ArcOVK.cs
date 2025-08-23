using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.RealLive
{
    [Export(typeof(ArchiveFormat))]
    public class OvkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "OVK"; } }
        public override string Description { get { return "RealLive engine audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public OvkOpener ()
        {
            Extensions = new string[] { "ovk", "nwk" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint entry_size;
            string entry_ext;
            if (file.Name.HasExtension (".ovk"))
            {
                entry_size = 0x10;
                entry_ext = "ogg";
            }
            else if (file.Name.HasExtension (".nwk"))
            {
                entry_size = 0xC;
                entry_ext = "nwa";
            }
            else
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint data_offset = 4 + (uint)count * entry_size;
            if (data_offset >= file.MaxOffset)
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint size   = file.View.ReadUInt32 (index_offset);
                uint offset = file.View.ReadUInt32 (index_offset+4);
                uint id     = file.View.ReadUInt32 (index_offset+8);
                if (offset < data_offset)
                    return null;
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D5}.{2}", base_name, id, entry_ext),
                    Type = "audio",
                    Offset = offset,
                    Size   = size,
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += entry_size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
