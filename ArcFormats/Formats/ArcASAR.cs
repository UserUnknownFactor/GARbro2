using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using GameRes.Utility;

namespace GameRes.Formats.Electron
{
    internal class AsarEntry : Entry
    {
        public bool IsUnpacked { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class AsarOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ASAR"; } }
        public override string Description { get { return "Electron ASAR archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public AsarOpener ()
        {
            Extensions = new string[] { "asar" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset < 24)
                return null;

            int header_size = file.View.ReadInt32 (0);
            if (header_size != 4)
                return null;

            uint json_length = file.View.ReadUInt32(4) - 8;
            var json_data = file.View.ReadBytes (16, json_length);

            byte zeros = 0;
            while (json_length > zeros + 1 && json_data[json_length - zeros - 1] == 0)
                zeros++;

            string json_string;
            try
            {
                json_string = Encoding.UTF8.GetString (json_data, 0, (int)json_length - zeros);
            }
            catch
            {
                return null;
            }

            JObject root;
            try
            {
                root = JObject.Parse (json_string);
            }
            catch (JsonException)
            {
                return null;
            }

            if (root == null || !root.ContainsKey("files"))
                return null;

            var dir = new List<Entry>();
            long base_offset = 16 + json_length;
            var files_obj = root["files"] as JObject;
            if (files_obj == null)
                return null;

            ParseDirectory (files_obj, "", dir, base_offset);
            if (dir.Count == 0)
                return null;

            return new ArcFile (file, this, dir);
        }

        private void ParseDirectory(JObject files, string path, List<Entry> dir, long base_offset)
        {
            foreach (var item in files)
            {
                string name = item.Key;
                var value = item.Value as JObject;
                if (value == null) continue;

                string full_path = string.IsNullOrEmpty (path) ? name : path + "/" + name;

                if (value.ContainsKey ("files"))
                {
                    var subfiles = value["files"] as JObject;
                    if (subfiles != null)
                        ParseDirectory (subfiles, full_path, dir, base_offset);
                }
                else // it's a file
                {
                    var entry = new AsarEntry { Name = full_path };

                    if (value.ContainsKey ("size"))
                    {
                        entry.Size = value["size"].Value<uint>();
                    }

                    if (value.ContainsKey ("offset"))
                    {
                        var offset_token = value["offset"];
                        if (offset_token.Type == JTokenType.String)
                        {
                            entry.Offset = base_offset + long.Parse (offset_token.Value<string>());
                        }
                        else
                        {
                            entry.Offset = base_offset + offset_token.Value<long>();
                        }
                    }

                    if (value.ContainsKey ("unpacked"))
                    {
                        entry.IsUnpacked = value["unpacked"].Value<bool>();
                    }

                    entry.Type = FormatCatalog.Instance.GetTypeFromName (full_path);

                    dir.Add (entry);
                }
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var asar_entry = entry as AsarEntry;
            if (asar_entry != null && asar_entry.IsUnpacked)
            {
                string unpacked_path = arc.File.Name + ".unpacked";
                string file_path = Path.Combine (unpacked_path, entry.Name.Replace ('/', Path.DirectorySeparatorChar));
                if (File.Exists (file_path))
                {
                    return new FileStream (file_path, FileMode.Open, FileAccess.Read);
                }
                else
                {
                    throw new FileNotFoundException ($"Unpacked file not found: {file_path}");
                }
            }

            return base.OpenEntry (arc, entry);
        }
    }
}
