using System.Collections.Generic;
using System.Linq;
using System.IO;
using static GameRes.Formats.Unity.Asset;
using static System.Net.Mime.MediaTypeNames;

namespace GameRes.Formats.Unity
{
    internal class ResourcesAssetsDeserializer
    {
        string m_res_name;
        Dictionary<string, BundleEntry> m_bundles;
        int[] m_version;
        bool m_is_little_endian;

        public int Format { get; set; }
        public long DataOffset { get; set; }

        public ResourcesAssetsDeserializer (string arc_name, int[] version = null, bool is_little_endian = false)
        {
            m_res_name = arc_name;
            m_version = version ?? new int[] { 2017, 0, 0, 0};
            m_is_little_endian = is_little_endian;
        }

        public List<Entry> Parse (AssetReader input, long base_offset = 0)
        {
            var asset = new Asset();
            asset.Load (input);
            var dir = new List<Entry>();
            m_bundles = new Dictionary<string, BundleEntry>();
            var used_names = new HashSet<string>();
            var path_id_map = new Dictionary<long, string>();

            foreach (var obj in asset.Objects.Where (o => o.TypeName == "AssetBundle"))
            {
                input.Position = obj.Offset + base_offset;
                try
                {
                    var bundle_name = input.ReadString(); // m_Name
                    input.Align();
                    int preload_count = input.ReadInt32(); // m_PreloadTable
                    for (int i = 0; i < preload_count; ++i)
                    {
                        input.ReadInt32(); // m_FileID
                        input.ReadInt64(); // m_PathID
                    }
                    int container_count = input.ReadInt32(); // m_Container
                    for (int i = 0; i < container_count; ++i)
                    {
                        string asset_name = input.ReadString();
                        input.Align();
                        input.ReadInt32(); // preloadIndex
                        input.ReadInt32(); // preloadSize
                        input.ReadInt32(); // m_FileID
                        long path_id = input.ReadInt64();
                        path_id_map[path_id] = asset_name;
                    }
                }
                catch
                {
                }
            }

            foreach (var obj in asset.Objects.Where (o => o.TypeId > 0))
            {
                input.Position = obj.Offset + base_offset;
                AssetEntry entry = null;
                int id = obj.TypeId > 0 ? obj.TypeId : obj.ClassId;

                string name = null;
                if (path_id_map.TryGetValue (obj.PathId, out name))
                {
                    int lastSlash = name.LastIndexOfAny (new[] { '/', '\\' });
                    if (lastSlash >= 0)
                        name = name.Substring (lastSlash + 1);
                }

                switch (id)
                {
                    case 48: // Shader
                        entry = new AssetEntry
                        {
                            Name = name ?? GetObjectName (input, obj),
                            Type = "shader",
                            Offset = obj.Offset,
                            Size = obj.Size,
                        };
                        break;

                    case 114: // MonoBehaviour
                        entry = new AssetEntry
                        {
                            Name = name ?? GetObjectName (input, obj),
                            Type = "script",
                            Offset = obj.Offset,
                            Size = obj.Size,
                        };
                        break;

                    case 28: // Texture2D
                        {
                            var tex = new Texture2D();
                            tex.Load (input, asset.Tree);

                            // Check for streaming data
                            if (tex.m_StreamData != null && tex.m_StreamData.Size > 0 && !string.IsNullOrEmpty (tex.m_StreamData.Path))
                            {
                                entry = new AssetEntry
                                {
                                    Name = name ?? $"{tex.m_Name}-{obj.PathId}",
                                    Type = "image",
                                    Offset = tex.m_StreamData.Offset,
                                    Size = tex.m_StreamData.Size,
                                    Bundle = GetBundle (tex.m_StreamData.Path),
                                };
                            }
                            else if (0 != tex.m_DataLength)
                            {
                                entry = new AssetEntry
                                {
                                    Name = name ?? $"{tex.m_Name}-{obj.PathId}",
                                    Type = "image",
                                    Offset = 0,
                                    Size = (uint)tex.m_DataLength
                                };
                            }
                            else
                            {
                                entry = new AssetEntry
                                {
                                    Name = name ?? $"{tex.m_Name}-{obj.PathId}",
                                    Type = "image",
                                    Offset = obj.Offset,
                                    Size = obj.Size,
                                };
                            }
                            break;
                        }
                    case 83: // AudioClip
                        {
                            var clip = new AudioClip();
                            clip.Load (input);
                            if (!string.IsNullOrEmpty (clip.m_Source))
                            {
                                entry = new AssetEntry
                                {
                                    Name = name ?? $"{clip.m_Name}-{obj.PathId}",
                                    Type = "audio",
                                    Offset = clip.m_Offset,
                                    Size = (uint)clip.m_Size,
                                    Bundle = GetBundle (clip.m_Source),
                                };
                            }
                            else if (clip.m_Size != 0)
                            {
                                entry = new AssetEntry
                                {
                                    Name = name ?? $"{clip.m_Name}-{obj.PathId}",
                                    Type = "audio",
                                    Offset = input.Position,
                                    Size = (uint)clip.m_Size,
                                };
                            }
                            break;
                        }
                    case 49:  // TextAsset
                        {
                            var asset_name = input.ReadString();
                            input.Align();
                            uint size = input.ReadUInt32();
                            entry = new AssetEntry
                            {
                                Name = name ?? asset_name,
                                Offset = input.Position,
                                Size = size,
                            };

                            string ext = Path.GetExtension (entry.Name).ToLowerInvariant();
                            if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".tga" || ext == ".bmp")
                                entry.Type = "image";
                            else if (ext == ".txt" || ext == ".json" || ext == ".xml" || ext == ".html" || ext == ".css" || ext == ".js")
                                entry.Type = "text";
                            else if (ext == ".wav" || ext == ".mp3" || ext == ".ogg")
                                entry.Type = "audio";
                            else
                                entry.Type = "text";
                            break;
                        }
                    case 128: // Font
                        {
                            entry = new AssetEntry
                            {
                                Name = name ?? GetObjectName (input, obj),
                                Type = "font",
                                Offset = obj.Offset,
                                Size = obj.Size,
                            };
                            break;
                        }
                    case 21: // Material
                        {
                            entry = new AssetEntry
                            {
                                Name = name ?? GetObjectName (input, obj),
                                Type = "material",
                                Offset = obj.Offset,
                                Size = obj.Size,
                            };
                            break;
                        }
                    case 43: // Mesh
                        {
                            entry = new AssetEntry
                            {
                                Name = name ?? GetObjectName (input, obj),
                                Type = "mesh",
                                Offset = obj.Offset,
                                Size = obj.Size,
                            };
                            break;
                        }
                    case 74: // AnimationClip
                        {
                            entry = new AssetEntry
                            {
                                Name = name ?? GetObjectName (input, obj),
                                Type = "animation",
                                Offset = obj.Offset,
                                Size = obj.Size,
                            };
                            break;
                        }
                    case 213: // Sprite
                        {
                            entry = new AssetEntry
                            {
                                Name = name ?? GetObjectName (input, obj),
                                Type = "sprite",
                                Offset = obj.Offset,
                                Size = obj.Size,
                            };
                            break;
                        }
                    default:
                        // For other types, create a generic entry
                        /*entry = new AssetEntry
                        {
                            Name = name ?? GetObjectName (input, obj),
                            Type = obj.TypeName.ToLowerInvariant(),
                            Offset = obj.Offset,
                            Size = obj.Size,
                        };*/
                        break;
                }

                if (entry != null)
                {
                    entry.AssetObject = obj;

                    if (string.IsNullOrEmpty (entry.Name))
                        entry.Name = GetObjectName (input, obj, obj.TypeName);

                    AddAppropriateExtension (entry);

                    dir.Add (entry);
                }
            }
            return dir;
        }

        private string GetObjectName (AssetReader input, UnityObject obj, string prefix = null)
        {
            string name = NamedObject.PeekName(input, obj.Offset);
            if (string.IsNullOrEmpty(name))
            {
                if (string.IsNullOrEmpty(obj.ContainerName))
                    return $"{obj.PathId}";

                return $"{obj.ContainerName}-{obj.PathId}";
            }

            if (!string.IsNullOrEmpty(name))
                name = prefix + name;

            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return $"{name}-{obj.PathId}";
        }

        private void AddAppropriateExtension (AssetEntry entry)
        {
            if (Path.HasExtension (entry.Name))
                return;

            switch (entry.Type)
            {
                case "image":
                    entry.Name += ".png";
                    break;
                case "audio":
                    entry.Name += ".wav";
                    break;
                case "text":
                    entry.Name += ".txt";
                    break;
                case "shader":
                    entry.Name += ".shader";
                    break;
                case "font":
                    entry.Name += ".ttf";
                    break;
                case "mesh":
                    entry.Name += ".mesh";
                    break;
                case "material":
                    entry.Name += ".mat";
                    break;
                case "animation":
                    entry.Name += ".anim";
                    break;
                case "sprite":
                    entry.Name += ".sprite";
                    break;
                case "script":
                    entry.Name += ".dat";
                    break;
            }
        }

        public Dictionary<string, ArcView> GenerateResourceMap (List<Entry> dir)
        {
            var res_map = new Dictionary<string, ArcView>();
            var asset_dir = VFS.GetDirectoryName (m_res_name);

            var resS_files = VFS.GetFiles();
            foreach (var resS_file in resS_files)
            {
                if (!resS_file.Name.EndsWith(".resS")) continue;
                string fileName = Path.GetFileName (resS_file.Name);
                if (!res_map.ContainsKey (fileName))
                {
                    res_map[fileName] = VFS.OpenView (resS_file);
                }
            }

            foreach (AssetEntry entry in dir)
            {
                if (null == entry.Bundle)
                    continue;
                if (res_map.ContainsKey (entry.Bundle.Name))
                    continue;

                var bundle_name = VFS.CombinePath (asset_dir, entry.Bundle.Name);
                if (!VFS.FileExists (bundle_name))
                {
                    var parent_dir = VFS.GetDirectoryName (asset_dir);
                    bundle_name = VFS.CombinePath (parent_dir, entry.Bundle.Name);

                    if (!VFS.FileExists (bundle_name))
                    {
                        entry.Bundle = null;
                        entry.Offset = entry.AssetObject.Offset;
                        entry.Size = entry.AssetObject.Size;
                        continue;
                    }
                }

                res_map[entry.Bundle.Name] = VFS.OpenView (bundle_name);
            }

            return res_map;
        }

        BundleEntry GetBundle (string path)
        {
            BundleEntry bundle;
            if (!m_bundles.TryGetValue (path, out bundle))
            {
                bundle = new BundleEntry { Name = path };
                m_bundles[path] = bundle;
            }
            return bundle;
        }
    }
}