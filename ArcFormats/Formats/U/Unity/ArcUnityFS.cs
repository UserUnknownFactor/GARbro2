using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Compression;
using GameRes.Utility;
using LZMA = SevenZip.Compression.LZMA;

namespace GameRes.Formats.Unity
{
    [Export(typeof(ArchiveFormat))]
    public class UnityFSOpener : ArchiveFormat
    {
        public override string         Tag { get { return "UNITY/FS"; } }
        public override string Description { get { return "Unity game engine asset archive"; } }
        public override uint     Signature { get { return  0x74696E55; } } // 'UnityFS'
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        public UnityFSOpener ()
        {
            Extensions = new string[] { "", "unity3d", "asset", "bundle", "assets" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "UnityFS\0"))
                return null;

            int arc_version = Binary.BigEndian (file.View.ReadInt32 (8));
            if (arc_version < 6 || arc_version > 8)
                return null;

            long data_offset;
            byte[] index_data;
            Version version_engine;
            string version_player;

            using (var input = file.CreateStream())
            {
                input.Position = 0xC;
                version_player = input.ReadCString (Encoding.UTF8);
                version_engine = ParseUnityVersion (input.ReadCString (Encoding.UTF8));

                long file_size = Binary.BigEndian (input.ReadInt64());
                int packed_index_size = Binary.BigEndian (input.ReadInt32());
                int index_size = Binary.BigEndian (input.ReadInt32());
                int flags = Binary.BigEndian (input.ReadInt32());

                // First alignment: after reading flags
                if (arc_version >= 7)
                {
                    long pos = input.Position;
                    long aligned = (pos + 15) & ~15;
                    input.Position = aligned;
                }
                else if (version_engine >= new Version (2019, 4, 0))
                {
                    long pre_align = input.Position;
                    int padding = (int)((16 - pre_align % 16) % 16);
                    if (padding > 0)
                    {
                        var align_data = input.ReadBytes (padding);
                        bool all_zero = true;
                        foreach (byte b in align_data)
                        {
                            if (b != 0)
                            {
                                all_zero = false;
                                break;
                            }
                        }
                        if (!all_zero)
                            input.Position = pre_align;
                    }
                }

                long index_offset;
                if (0 == (flags & 0x80))
                {
                    // Index at current position
                    index_offset = input.Position;
                    data_offset = index_offset + packed_index_size;
                }
                else
                {
                    // Index at end of file
                    index_offset = file_size - packed_index_size;
                    data_offset = input.Position;
                }

                // Second alignment: padding before data blocks start
                if ((flags & 0x200) != 0)
                    data_offset = (data_offset + 15) & ~15;  // Align to 16 bytes

                input.Position = index_offset;
                var packed = input.ReadBytes (packed_index_size);

                // Decompress index based on compression type
                switch (flags & 0x3F)
                {
                case 0: // None
                    index_data = packed;
                    break;
                case 1: // LZMA
                    index_data = UnpackLzma (packed, index_size);
                    break;
                case 2: // LZ4
                case 3: // LZ4HC
                    index_data = UnpackLz4 (packed, index_size);
                    break;
                default:
                    return null;
                }
            }

            // Parse the decompressed index
            var index = new AssetDeserializer (file, data_offset);
            using (var input = new BinMemoryStream (index_data))
            {
                index.Parse (input);
            }

            // Load the directory from the bundles
            var dir = index.LoadObjects();
            if (dir.Count == 0)
                dir = index.Bundles.Cast<Entry>().ToList();

            UnityAssetHelper.OrganizeByType (dir);


            return new UnityBundle (file, this, dir, index.Segments, index.Bundles);
        }

        private static Version ParseUnityVersion (string versionString)
        {
            if (string.IsNullOrEmpty (versionString))
                return new Version (0, 0, 0);

            var match = System.Text.RegularExpressions.Regex.Match (versionString, @"(\d+)\.(\d+)\.(\d+)");
            if (match.Success)
            {
                int major = int.Parse (match.Groups[1].Value);
                int minor = int.Parse (match.Groups[2].Value);
                int patch = int.Parse (match.Groups[3].Value);
                return new Version (major, minor, patch);
            }

            return new Version (0, 0, 0);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var uarc = (UnityBundle)arc;
            var aent = entry as AssetEntry;
            Stream input = new BundleStream (uarc.File, uarc.Segments);
            if (aent?.ParentBundle != null)
            {
                long actualOffset = aent.ParentBundle.Offset + entry.Offset;
                input = new StreamRegion (input, actualOffset, entry.Size);
            }
            else
            input = new StreamRegion (input, entry.Offset, entry.Size);

            if (aent != null && aent.IsEncrypted)
            {
            using (input)
            {
                var data = new byte[entry.Size];
                input.Read (data, 0, data.Length);
                DecryptAsset (data);
                return new BinMemoryStream (data);
            }
        }

            return input;
        }
        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var aent = entry as AssetEntry;
            if (null == aent || aent.AssetObject?.TypeName != "Texture2D")
                return base.OpenImage (arc, entry);

            var uarc = (UnityBundle)arc;
            var obj = aent.AssetObject;
            Stream input;
            if (aent.ParentBundle != null)
            {
                input = new BundleStream (uarc.File, uarc.Segments);
                input = new StreamRegion (input, aent.ParentBundle.Offset + obj.Offset, obj.Size);
            }
            else
            {
                input = new BundleStream (uarc.File, uarc.Segments);
            input = new StreamRegion (input, obj.Offset, obj.Size);
            }
            var reader = new AssetReader (input, entry.Name);
            reader.SetupReaders (obj.Asset);

            Texture2D tex = new Texture2D();
            tex.Load (reader, obj.Asset.Tree);

            // Check if texture has streaming data in .resS
            if (tex.m_StreamData != null && tex.m_StreamData.Size > 0 && aent.Bundle != null)
            {
                // Find the .resS bundle in the same archive
                var resSEntry = arc.Dir.FirstOrDefault (e =>
                    e.Name == aent.Bundle.Name ||
                    e.Name.EndsWith (aent.Bundle.Name));

                if (resSEntry != null)
                {
                    // The texture data is in the .resS file at the specified offset

                    // Create a new BundleStream and read from the correct position
                    using (var resSStream = arc.OpenEntry (resSEntry))
                    {
                        // Position at: resS bundle offset + texture offset within resS
                        resSStream.Position = tex.m_StreamData.Offset;

                        // Read the texture data
                        tex.m_Data = new byte[tex.m_StreamData.Size];
                        resSStream.Read (tex.m_Data, 0, tex.m_Data.Length);
                        tex.m_DataLength = (int)tex.m_StreamData.Size;
                    }
                }
            }

            return new Texture2DDecoder (tex, reader);
        }

        internal static byte[] UnpackLz4 (byte[] input, int unpacked_size)
        {
            // Unity uses raw LZ4 block compression
            var output = new byte[unpacked_size];
            int result = Lz4Compressor.DecompressBlock (input, input.Length, output, output.Length);
            if (result != output.Length)
                throw new InvalidFormatException ("LZ4 decompression size mismatch");
            return output;
        }

        internal static byte[] UnpackLzma (byte[] input, int unpacked_size)
        {
            var decoder = new LZMA.Decoder();
            using (var inputStream = new MemoryStream (input))
            using (var outputStream = new MemoryStream (unpacked_size))
            {
                var props = new byte[5];
                inputStream.Read (props, 0, 5);
                decoder.SetDecoderProperties (props);
                decoder.Code (inputStream, outputStream, input.Length - 5, unpacked_size, null);
                return outputStream.ToArray();
            }
        }

        internal void DecryptAsset (byte[] data)
        {
            uint key = 0xBF8766F5u;
            for (int i = 0; i < data.Length; ++i)
            {
                key = ((0x343FD * key + 0x269EC3) >> 16) & 0x7FFF; // MSVC rand()
                data[i] ^= (byte)key;
            }
        }
    }

    internal class BundleEntry : Entry
    {
        public uint Flags;
    }

    internal class AssetEntry : Entry
    {
        public BundleEntry  Bundle;
        public UnityObject  AssetObject;
        public BundleEntry  ParentBundle;
    }

    internal class UnityBundle : ArcFile
    {
        public readonly List<BundleSegment> Segments;
        public readonly List<BundleEntry>   Bundles;

        public UnityBundle (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, List<BundleSegment> segments, List<BundleEntry> bundles)
            : base (arc, impl, dir)
        {
            Segments = segments;
            Bundles = bundles;
        }
    }

    internal class BundleSegment
    {
        public long Offset;
        public uint PackedSize;
        public long UnpackedOffset;
        public uint UnpackedSize;
        public int  Compression;

        public bool IsCompressed { get { return (Compression & 0x3F) != 0; } }
    }

    internal class AssetDeserializer
    {
        readonly ArcView    m_file;
        readonly long       m_data_offset;
        List<BundleSegment> m_segments;
        List<BundleEntry>   m_bundles;

        public List<BundleSegment> Segments { get { return m_segments; } }
        public List<BundleEntry>    Bundles { get { return m_bundles; } }

        public AssetDeserializer (ArcView file, long data_offset)
        {
            m_file = file;
            m_data_offset = data_offset;
        }

        public void Parse (IBinaryStream index)
        {
            index.Position = 16; // Skip GUID
            int segment_count = Binary.BigEndian (index.ReadInt32());
            m_segments = new List<BundleSegment> (segment_count);
            long packed_offset = m_data_offset;
            long unpacked_offset = 0;

            for (int i = 0; i < segment_count; ++i)
            {
                var segment = new BundleSegment();
                segment.Offset = packed_offset;
                segment.UnpackedOffset = unpacked_offset;
                segment.UnpackedSize = Binary.BigEndian (index.ReadUInt32());
                segment.PackedSize = Binary.BigEndian (index.ReadUInt32());
                segment.Compression = Binary.BigEndian (index.ReadUInt16());
                m_segments.Add (segment);
                packed_offset += segment.PackedSize;
                unpacked_offset += segment.UnpackedSize;
            }

            int count = Binary.BigEndian (index.ReadInt32());
            m_bundles = new List<BundleEntry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new BundleEntry();
                entry.Offset = Binary.BigEndian (index.ReadInt64());
                entry.Size = (uint)Binary.BigEndian (index.ReadInt64());
                entry.Flags = Binary.BigEndian (index.ReadUInt32());
                entry.Name = index.ReadCString (Encoding.UTF8);
                m_bundles.Add (entry);
            }
        }

        public List<Entry> LoadObjects ()
        {
            var dir = new List<Entry>();
            using (var stream = new BundleStream (m_file, m_segments))
            {
                foreach (BundleEntry bundle in m_bundles)
                {
                    if (bundle.Name.HasAnyOfExtensions (".resource", ".resS"))
                    {
                        dir.Add (bundle);
                        continue;
                    }

                    try
                    {
                        using (var asset_stream = new StreamRegion (stream, bundle.Offset, bundle.Size, true))
                        using (var reader = new AssetReader (asset_stream, bundle.Name))
                        {
                            var deserializer = new ResourcesAssetsDeserializer (bundle.Name);
                            var asset_dir = deserializer.Parse (reader);

                            foreach (var entry in asset_dir.OfType<AssetEntry>())
                            {
                                if (entry.Bundle == null)
                                    entry.Bundle = bundle;
                                entry.ParentBundle = bundle;
                            }

                            dir.AddRange (asset_dir);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine ($"Failed to parse bundle {bundle.Name}: {ex.Message}");
                        dir.Add (bundle);
                    }
                }
            }

            if (0 == dir.Count)
                dir.AddRange (m_bundles);

            return dir;
        }

        IEnumerable<Entry> ParseAsset (Stream file, BundleEntry bundle, Asset asset)
        {
            Dictionary<long, string> id_map = null;
            var bundle_types = asset.Tree.TypeTrees.Where (t => t.Value.Type == "AssetBundle").Select (t => t.Key);
            if (bundle_types.Any())
            {
                // try to read entry names from AssetBundle object
                int bundle_type_id = bundle_types.First();
                var asset_bundle = asset.Objects.FirstOrDefault (x => x.TypeId == bundle_type_id);
                if (asset_bundle != null)
                {
                    id_map = ReadAssetBundle (file, asset_bundle);
                }
            }
            if (null == id_map)
                id_map = new Dictionary<long, string>();

            foreach (var obj in asset.Objects)
            {
                var entry = ReadAsset (file, obj);
                if (null == entry)
                    continue;
                if (null == entry.Bundle)
                    entry.Bundle = bundle;

                entry.ParentBundle = bundle;
                string name;
                if (!id_map.TryGetValue (obj.PathId, out name))
                    name = GetObjectName (file, obj);
                else
                    name = ShortenPath (name);

                entry.Name = name;
                yield return entry;
            }
        }

        AssetEntry ReadAsset (Stream file, UnityObject obj)
        {
            string type = obj.TypeName;
            if ("AudioClip" == type)
                return ReadAudioClip (file, obj);
            else if ("TextAsset" == type)
                return ReadTextAsset (file, obj);
            else if ("Texture2D" == type)
            {
                return new AssetEntry {
                    Type = "image",
                    AssetObject = obj,
                    Offset = obj.Offset,
                    Size = obj.Size,
                };
            }
            else if ("AssetBundle" == type || "GameObject" == type)
                return null; // Skip metadata types

            return new AssetEntry {
                Type = type.ToLowerInvariant(),
                AssetObject = obj,
                Offset = obj.Offset,
                Size = obj.Size,
            };
        }

        Dictionary<long, string> ReadAssetBundle (Stream input, UnityObject bundle)
        {
            using (var reader = bundle.Open (input))
            {
                var name = reader.ReadString(); // m_Name
                reader.Align();
                int count = reader.ReadInt32(); // m_PreloadTable
                for (int i = 0; i < count; ++i)
                {
                    reader.ReadInt32(); // m_FileID
                    reader.ReadInt64(); // m_PathID
                }
                count = reader.ReadInt32(); // m_Container
                var id_map = new Dictionary<long, string> (count+1);
                id_map[bundle.PathId] = name;
                for (int i = 0; i < count; ++i)
                {
                    name = reader.ReadString();
                    reader.Align();
                    reader.ReadInt32(); // preloadIndex
                    reader.ReadInt32(); // preloadSize
                    reader.ReadInt32(); // m_FileID
                    long id = reader.ReadInt64();
                    id_map[id] = name;
                }
                return id_map;
            }
        }

        string GetObjectName (Stream input, UnityObject obj)
        {
            var type = obj.Type;
            if (type != null && type.Children.Count > 0)
            {
                var first_field = type.Children[0];
                if ("m_Name" == first_field.Name && "string" == first_field.Type)
                {
                    using (var reader = obj.Open (input))
                    {
                        var name = reader.ReadString();
                        if (!string.IsNullOrEmpty (name))
                            return name;
                    }
                }
            }
            return obj.PathId.ToString ("X16");
        }

        AssetEntry ReadTextAsset (Stream input, UnityObject obj)
        {
            var script = obj.Type?.Children.FirstOrDefault (f => f.Name == "m_Script");

            using (var reader = obj.Open (input))
            {
                var name = reader.ReadString();
                reader.Align();
                uint size = reader.ReadUInt32();

                var entry = new AssetEntry {
                    AssetObject = obj,
                    Offset = obj.Offset + reader.Position,
                    Size = size,
                    IsEncrypted = 0 != (script.Flags & 0x04000000),
                };
                if (entry.IsEncrypted)
                {
                    uint signature = reader.ReadUInt32();
                    if (0x0D15F641 == signature)
                        entry.Type = "image";
                    else if (0x474E5089 == signature)
                    {
                        entry.Type = "image";
                        entry.IsEncrypted = false;
                    }
                }
                return entry;
            }
        }

        AssetEntry ReadAudioClip (Stream input, UnityObject obj)
        {
            using (var reader = obj.Open (input))
            {
                var clip = new AudioClip();
                clip.Load (reader);
                var bundle_name = VFS.GetFileName (clip.m_Source);
                var bundle = m_bundles.FirstOrDefault (b => b.Name == bundle_name);
                if (null == bundle)
                    return null;
                return new AssetEntry {
                    Type = "audio",
                    Bundle = bundle,
                    AssetObject = obj,
                    Offset = bundle.Offset + clip.m_Offset,
                    Size = (uint)clip.m_Size,
                };
            }
        }

        /// <summary>
        /// Shorten asset path to contain only the bottom directory component.
        /// </summary>
        static string ShortenPath (string name)
        {
            int slash_pos = name.LastIndexOf ('/');
            if (-1 == slash_pos)
                return name;
            slash_pos = name.LastIndexOf ('/', slash_pos-1);
            if (-1 == slash_pos)
                return name;
            return name.Substring (slash_pos+1);
        }
    }
}