using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.DigitalWorks
{
    [Serializable]
    public class IndexEntry
    {
        public  uint    Offset;
        public  uint    Size;
        public  bool    IsPacked;
        public  ushort  Id;

        public IndexEntry (uint offset, uint size, bool is_packed, ushort id)
        {
            Offset = offset;
            Size = size;
            IsPacked = is_packed;
            Id = id;
        }
    }

    [Serializable]
    public class BinScheme
    {
        public string   Extension;
        public long     Size;
        public IList<IndexEntry>    Index;
    }

    [Serializable]
    public class PacScheme : ResourceScheme
    {
        public IDictionary<string, IDictionary<string, BinScheme>>   KnownSchemes;
    }

    [Export(typeof(ArchiveFormat))]
    public class BinOpener : PacOpener
    {
        public override string         Tag { get { return "BIN/PAC"; } }
        public override string Description { get { return "Digital Works resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public BinOpener ()
        {
            ContainedFormats = new[] { "TX", "OGG", "SCR" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasAnyOfExtensions ("bin", "pac"))
                return null;
            var scheme = FindScheme (file);
            if (null == scheme)
                return null;
            var pac_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = scheme.Index.Select (e => new PackedEntry {
                Name = string.Format ("{0}{1:D5}.{2}", pac_name, e.Id, scheme.Extension),
                Offset = e.Offset,
                Size = e.Size,
            } as Entry).ToList();
            dir.ForEach (e => e.Type = FormatCatalog.Instance.GetTypeFromName (e.Name, ContainedFormats));
            return new ArcFile (file, this, dir);
        }

        BinScheme FindScheme (ArcView bin_file)
        {
            var bin_name = Path.GetFileName (bin_file.Name).ToUpperInvariant();
            foreach (var game in KnownSchemes.Values)
            {
                BinScheme scheme;
                if (game.TryGetValue (bin_name, out scheme) && bin_file.MaxOffset == scheme.Size)
                    return scheme;
            }
            if (bin_file.MaxOffset >= uint.MaxValue)
                return null;
            var bin_dir = VFS.GetDirectoryName (bin_file.Name);
            var game_dir = Directory.GetParent (bin_dir).FullName;
            var exe_files = VFS.GetFiles (VFS.CombinePath (game_dir, "*.exe"));
            if (!exe_files.Any())
                return null;
            var last_idx = new byte[12];
            LittleEndian.Pack ((uint)bin_file.MaxOffset, last_idx, 0);
            LittleEndian.Pack ((uint)bin_file.MaxOffset, last_idx, 4);
            foreach (var exe_entry in exe_files)
            {
                using (var exe_file = VFS.OpenView (exe_entry))
                {
                    var exe = new ExeFile (exe_file);
                    if (!exe.ContainsSection (".data"))
                        continue;
                    var data_section = exe.Sections[".data"];
                    var idx_pos = exe.FindString (data_section, last_idx, 4);
                    if (idx_pos > 0)
                        return ParseIndexTable (exe_file, data_section, idx_pos, bin_name);
                }
            }
            return null;
        }

        BinScheme ParseIndexTable (ArcView exe_file, ExeFile.Section data, long pos, string bin_name)
        {
            uint pac_size = exe_file.View.ReadUInt32 (pos);
            long last_offset = pac_size;
            var dir = new List<IndexEntry>();
            for (pos -= 12; pos >= data.Offset && last_offset != 0; pos -= 12)
            {
                long offset = exe_file.View.ReadUInt32 (pos);
                uint size   = exe_file.View.ReadUInt32 (pos+4);
                ushort is_packed = exe_file.View.ReadUInt16 (pos+8);
                ushort id   = exe_file.View.ReadUInt16 (pos+10);
                if (0 == size || offset + size > last_offset || is_packed != 0 && is_packed != 1)
                    return null;
                var entry = new IndexEntry ((uint)offset, size, is_packed != 0, id);
                dir.Add (entry);
                last_offset = offset;
            }
            bin_name = Path.GetFileNameWithoutExtension (bin_name).ToUpperInvariant();
            string ext;
            if (!PacExtensionMap.TryGetValue (bin_name, out ext))
                ext = "";
            return new BinScheme {
                Extension = ext,
                Size  = pac_size,
                Index = dir,
            };
        }

        static readonly Dictionary<string, string> PacExtensionMap = new Dictionary<string, string> {
            { "ANM", "BIN" },
            { "MOV", "MPG" },
            { "STR", "OGG" },
            { "TAK", "BIN" },
            { "VCE", "OGG" },
            { "VIS", "TMX" },
            { "_SE", "OGG" },
        };

        PacScheme DefaultScheme = new PacScheme {
            KnownSchemes = new Dictionary<string, IDictionary<string, BinScheme>>()
        };

        public IDictionary<string, IDictionary<string, BinScheme>> KnownSchemes
        {
            get { return DefaultScheme.KnownSchemes; }
        }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (PacScheme)value; }
        }
    }
}
