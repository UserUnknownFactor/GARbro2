using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GameRes.Utility;

namespace GameRes.Formats.Elf
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/HED"; } }
        public override string Description { get { return "elf AV King resource archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "bin", "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            ArcView pak_view, bin_view;
            bool should_dispose_pak = false;
            if (file.Name.HasExtension (".pak"))
            {
                pak_view = file;
                string bin_name = Path.ChangeExtension (file.Name, ".bin");
                if (!VFS.FileExists (bin_name))
                    return null;
                bin_view = VFS.OpenView (bin_name);
            }
            else if (file.Name.HasExtension (".bin"))
            {
                bin_view = file;
                string pak_name = Path.ChangeExtension (file.Name, ".pak");
                if (!VFS.FileExists (pak_name))
                    return null;
                pak_view = VFS.OpenView (pak_name);
                should_dispose_pak = true;
            }
            else
                return null;

            try
            {
                var file_map = GetFileMap (pak_view.Name);
                if (null == file_map)
                    return null;

                if (0x00646568 != pak_view.View.ReadUInt32 (0))
                    return null;
                int count = pak_view.View.ReadInt32 (4);
                if (count != file_map.Count)
                    return null;

                string base_name = Path.GetFileNameWithoutExtension (pak_view.Name);
                bool is_cg = ("cg" == base_name) ||
                             pak_view.Name.Contains ("\\cg\\") ||
                             pak_view.Name.Contains ("/cg/");

                List<Entry> dir;
                if (is_cg)
                    dir = ReadCgPak (pak_view, bin_view, file_map);
                else
                    dir = ReadVoicePak (pak_view, bin_view, file_map);

                if (null == dir)
                    return null;

                if (should_dispose_pak)
                    pak_view.Dispose ();

                return new ArcFile (bin_view, this, dir);
            }
            catch
            {
                if (should_dispose_pak)
                    pak_view.Dispose ();
                throw;
            }
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            var pak_options    = GetOptions<PakOptions> (options);
            var encoding       = Encoding.ASCII;
            var file_table     = list.ToArray();
            bool is_cg         = pak_options.IsCgArchive;
            string output_name = is_cg ? "cg.bin" : "voice.bin";

            long current_offset = 0;
            int n = 1;
            var offsets = new List<long>();
            foreach (var entry in file_table)
            {
                if (null != callback)
                    callback (n++, entry, Localization._T ("MsgAddingFile"));

                offsets.Add (current_offset);
                entry.Offset = current_offset;
                using (var input = File.OpenRead (entry.Name))
                {
                    var size = input.Length;
                    entry.Size = (uint)size;
                    current_offset += size;
                    input.CopyTo (output);
                }
            }

            // Write index file
            string pak_name = Path.ChangeExtension (output_name, ".pak");
            using (var pak = File.Create (pak_name))
            using (var pak_writer = new BinaryWriter (pak))
            {
                pak_writer.Write (0x00646568u); // 'hed\0'
                pak_writer.Write (file_table.Length);

                for (int i = 0; i < file_table.Length; ++i)
                {
                    pak_writer.Write ((uint)file_table[i].Offset);
                    pak_writer.Write (file_table[i].Size);
                    
                    if (!is_cg) // Voice format needs padding
                    {
                        pak_writer.Write (0L);
                        pak_writer.Write (0L);
                    }
                }
            }

            UpdateMapFile (output_name, file_table, is_cg);

            if (null != callback)
                callback (n++, null, Localization._T ("MsgWritingIndex"));
        }

        private void UpdateMapFile (string arc_name, Entry[] entries, bool is_cg)
        {
            return;
            /*string dir_name = VFS.GetDirectoryName (arc_name);
            string map_name = "avking.map";
            string map_path = null;

            // Look for existing map file in parent directories
            string search_dir = dir_name;
            while (!string.IsNullOrEmpty (search_dir))
            {
                string test_path = Path.Combine (search_dir, map_name);
                if (File.Exists (test_path))
                {
                    map_path = test_path;
                    break;
                }
                search_dir = Path.GetDirectoryName (search_dir);
            }

            if (null == map_path)
                map_path = Path.Combine (dir_name ?? ".", map_name);

            var existing_sections = new Dictionary<string, List<string>>();
            if (File.Exists (map_path))
            {
                using (var input = new StreamReader (map_path, Encoding.ASCII))
                {
                    string current_section = null;
                    var current_files = new List<string>();
                    string line;
                    
                    while ((line = input.ReadLine()) != null)
                    {
                        var match = FilesTypeRe.Match (line);
                        if (match.Success)
                        {
                            if (current_section != null && current_files.Count > 0)
                                existing_sections[current_section] = current_files;
                            
                            current_section = match.Groups[1].Value;
                            current_files = new List<string>();
                            int count = int.Parse (match.Groups[2].Value);
                            
                            for (int i = 0; i < count; ++i)
                            {
                                line = input.ReadLine();
                                if (line != null)
                                    current_files.Add (line.TrimEnd ('\0'));
                            }
                        }
                    }
                    
                    if (current_section != null && current_files.Count > 0)
                        existing_sections[current_section] = current_files;
                }
            }

            var new_files = entries.Select (e => e.Name).ToList();
            
            if (is_cg)
            {
                bool has_bg = new_files.Any (f => f.Contains ("/bg/") || f.Contains ("\\bg\\"));
                bool has_chr = new_files.Any (f => f.Contains ("/chr/") || f.Contains ("\\chr\\"));
                
                if (has_bg)
                    existing_sections["BG"] = new_files.Where (f => f.Contains ("/bg/") || f.Contains ("\\bg\\")).ToList();
                if (has_chr)
                    existing_sections["CHR"] = new_files.Where (f => f.Contains ("/chr/") || f.Contains ("\\chr\\")).ToList();
                if (!has_bg && !has_chr)
                    existing_sections["BG"] = new_files; // Default to BG
            }
            else
            {
                existing_sections["VOICE"] = new_files;
            }

            using (var output = new StreamWriter (map_path, false, Encoding.ASCII))
            {
                foreach (var section in existing_sections)
                {
                    output.WriteLine ("//{0} FILES = {1}", section.Key, section.Value.Count);
                    foreach (var file in section.Value)
                    {
                        output.WriteLine (file);
                    }
                }
            }*/
        }

        List<Entry> ReadCgPak (ArcView pak, ArcView bin, List<string> file_map)
        {
            uint index_offset = 8;
            uint index_size = (uint)file_map.Count * 8u;
            if (index_size > pak.View.Reserve (index_offset, index_size))
                return null;
            var dir = new List<Entry> (file_map.Count);
            for (int i = 0; i < file_map.Count; ++i)
            {
                var entry = FormatCatalog.Instance.Create<Entry> (file_map[i]);
                entry.Offset = pak.View.ReadUInt32 (index_offset);
                entry.Size   = pak.View.ReadUInt32 (index_offset + 4);
                if (!entry.CheckPlacement (bin.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return dir;
        }

        List<Entry> ReadVoicePak (ArcView pak, ArcView bin, List<string> file_map)
        {
            uint index_offset = 8;
            uint index_size = (uint)file_map.Count * 0x18u;
            if (index_size > pak.View.Reserve (index_offset, index_size))
                return null;
            var dir = new List<Entry> (file_map.Count);
            for (int i = 0; i < file_map.Count; ++i)
            {
                var entry = FormatCatalog.Instance.Create<Entry> (file_map[i]);
                entry.Offset = pak.View.ReadUInt32 (index_offset);
                entry.Size   = pak.View.ReadUInt32 (index_offset + 4);
                if (!entry.CheckPlacement (bin.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            return dir;
        }

        private List<string>    CgMap { get; set; }
        private List<string> VoiceMap { get; set; }
        private string CurrentMapName { get; set; }

        static readonly Regex FilesTypeRe = new Regex (@"^//([A-Z]+) FILES = (\d+)");

        private List<string> GetFileMap (string pak_name, string map_name = "avking.map")
        {
            string base_name = Path.GetFileNameWithoutExtension (pak_name);
            List<string> map;

            bool is_cg = false;
            bool is_voice = false;

            if ("cg" == base_name)
            {
                map = CgMap;
                is_cg = true;
            }
            else if ("voice" == base_name)
            {
                map = VoiceMap;
                is_voice = true;
            }
            else
            {
                string dir_path = VFS.GetDirectoryName (pak_name);
                if (!string.IsNullOrEmpty (dir_path) &&
                    (dir_path.Contains ("\\cg\\") || dir_path.Contains ("/cg/") ||
                     dir_path.EndsWith ("\\cg") || dir_path.EndsWith ("/cg")))
                {
                    map = CgMap;
                    is_cg = true;
                }
                else if (!string.IsNullOrEmpty (dir_path) &&
                         (dir_path.Contains ("\\voice\\") || dir_path.Contains ("/voice/") ||
                          dir_path.EndsWith ("\\voice") || dir_path.EndsWith ("/voice")))
                {
                    map = VoiceMap;
                    is_voice = true;
                }
                else
                    return null;
            }

            if (null != map && !string.IsNullOrEmpty (CurrentMapName))
            {
                try
                {
                    var cached_entry = VFS.FindFileInHierarchy (CurrentMapName);
                    if (cached_entry != null)
                        return map;
                }
                catch { }
            }

            CgMap = null;
            VoiceMap = null;
            CurrentMapName = null;

            var currentDir = VFS.GetDirectoryName (pak_name);
            bool doRoot = false;

            while (!string.IsNullOrEmpty (currentDir) || doRoot)
            {
                var map_path = VFS.CombinePath (currentDir, map_name);
                try
                {
                    var entry = VFS.FindFileInHierarchy (map_path);
                    if (entry != null)
                    {
                        using (var input = VFS.OpenStreamInHierarchy (entry))
                        {
                            if (ReadMap (input, map_path))
                            {
                                if (is_cg)
                                    return CgMap;
                                if (is_voice)
                                    return VoiceMap;
                            }
                        }
                    }
                }
                catch { }

                if (doRoot)
                    break;

                var newDir = VFS.GetDirectoryName (currentDir);
                if ((string.IsNullOrEmpty (newDir) && !string.IsNullOrEmpty (currentDir)) ||
                    newDir == currentDir)
                    doRoot = true;
                currentDir = newDir;
            }

            return null;
        }

        private bool ReadMap (Stream map_stream, string map_path)
        {
            try
            {
                using (var input = new StreamReader (map_stream, Encoding.ASCII))
                {
                    var cg = new List<string> ();
                    var voice = new List<string> ();
                    List<string> current_list = null;

                    for (; ; )
                    {
                        string line = input.ReadLine ();
                        if (null == line)
                            break;
                        var match = FilesTypeRe.Match (line);
                        if (!match.Success)
                            return false;
                        string type = match.Groups[1].Value;
                        if ("BG" == type || "CHR" == type)
                            current_list = cg;
                        else if ("VOICE" == type)
                            current_list = voice;
                        else
                            current_list = null;
                        int count = UInt16.Parse (match.Groups[2].Value);
                        for (int i = 0; i < count; ++i)
                        {
                            line = input.ReadLine ();
                            if (null == line)
                                break;
                            if (null != current_list)
                                current_list.Add (line.TrimEnd ('\0'));
                        }
                    }
                    CgMap = cg;
                    VoiceMap = voice;
                    CurrentMapName = map_path;
                    return cg.Count > 0 || voice.Count > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new PakOptions { IsCgArchive = true };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            if (widget is GUI.WidgetPAK)
                return new PakOptions { IsCgArchive = ((GUI.WidgetPAK)widget).IsCgArchive.IsChecked ?? true };
            return GetDefaultOptions();
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetPAK();
        }
    }

    public class PakOptions : ResourceOptions
    {
        public bool IsCgArchive { get; set; }
    }
}