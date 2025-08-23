using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;

// [981030][Logg] Physical Lesson

namespace GameRes.Formats.Logg
{
    [Export(typeof(ArchiveFormat))]
    public class MbmOpener : ArchiveFormat
    {
        public override string         Tag => "MBM";
        public override string Description => "Logg Adv engine resource archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension ("MBM"))
                return null;
            var index = GetArchiveIndex (file);
            if (null == index)
                return null;
            var dir = index.Take (index.Count - 1)
                .Select (e => new Entry {
                    Name = e.Value,
                    Type = FormatCatalog.Instance.GetTypeFromName (e.Value),
                    Offset = e.Key
                }).ToList();
            for (int i = 1; i < dir.Count; ++i)
                dir[i-1].Size = (uint)(dir[i].Offset - dir[i-1].Offset);
            dir[dir.Count-1].Size = (uint)(file.MaxOffset - dir[dir.Count-1].Offset);
            return new ArcFile (file, this, dir);
        }

        IDictionary<uint, string> GetArchiveIndex (ArcView file)
        {
            string list_name;
            if (!ArcSizeToFileListMap.TryGetValue ((uint)file.MaxOffset, out list_name))
                return null;
            var file_map = ReadFileList (list_name);
            uint last_offset = file_map.Keys.Last();
            if (last_offset != file.MaxOffset)
                return null;
            return file_map;
        }

        static readonly Dictionary<uint, string> ArcSizeToFileListMap = new Dictionary<uint, string> {
            { 0x0AB0F5F4, "logg_pl.lst" },
            { 0x0BFFD3DA, "logg_ak.lst" },
            { 0x09809196, "logg_th.lst" },
        };

        static IDictionary<uint, string> ReadFileList (string list_name)
        {
            var file_map = new SortedDictionary<uint,string>();
            var comma = new char[] {','};
            FormatCatalog.Instance.ReadFileList (list_name, line => {
                var parts = line.Split (comma, 2);
                uint offset = uint.Parse (parts[0], NumberStyles.HexNumber);
                if (2 == parts.Length)
                {
                    file_map[offset] = parts[1];
                }
                else
                {
                    file_map[offset] = null;
                }
            });
            return file_map;
        }
    }
}
