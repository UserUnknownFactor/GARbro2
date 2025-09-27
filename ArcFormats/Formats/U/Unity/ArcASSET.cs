using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Unity
{
    [Export(typeof(ArchiveFormat))]
    public class UnityAssetOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ASSETS/UNITY"; } }
        public override string Description { get { return "Unity game engine assets archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint header_size = Binary.BigEndian (file.View.ReadUInt32 (0));
            long file_size = Binary.BigEndian (file.View.ReadUInt32 (4));
            int format = Binary.BigEndian (file.View.ReadInt32 (8));
            if (format <= 0 || format > 0x100)
                return null;
            long data_offset = Binary.BigEndian (file.View.ReadUInt32 (12));
            if (format >= 22)
            {
                header_size = Binary.BigEndian (file.View.ReadUInt32 (0x14));
                file_size = Binary.BigEndian (file.View.ReadInt64 (0x18));
                data_offset = Binary.BigEndian (file.View.ReadInt64 (0x20));
            }
            if (file_size != file.MaxOffset || header_size > file_size || 0 == header_size
                || data_offset >= file_size || data_offset < header_size)
                return null;
            using (var stream = file.CreateStream())
            using (var input = new AssetReader (stream))
            {
                var index = new ResourcesAssetsDeserializer (file.Name);
                var dir = index.Parse (input);
                if (null == dir || 0 == dir.Count)
                    return null;

                dir = UnityAssetHelper.OrganizeByType (dir);

                var res_map = index.GenerateResourceMap (dir);
                return new UnityResourcesAsset (file, this, dir, res_map);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var uarc = (UnityResourcesAsset)arc;
            var uent = (AssetEntry)entry;
            if (null == uent.Bundle || !uarc.ResourceMap.ContainsKey (uent.Bundle.Name))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var bundle = uarc.ResourceMap[uent.Bundle.Name];
            return bundle.CreateStream (entry.Offset, entry.Size);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var aent = entry as AssetEntry;
            if (null == aent)
                return base.OpenImage (arc, entry);

            var obj = aent.AssetObject;

            if (obj.TypeName != "Texture2D" && obj.ClassId != 28)
                return base.OpenImage (arc, entry);

            var uarc = (UnityResourcesAsset)arc;
            var stream = arc.File.CreateStream (obj.Offset, obj.Size);
            var reader = new AssetReader (stream, entry.Name);
            try
            {
                reader.SetupReaders (obj.Asset);
                var tex = new Texture2D();
                tex.Load (reader, obj.Asset.Tree);
                if (0 == tex.m_DataLength)
                {
                    reader.Dispose();
                    var input = OpenEntry (arc, entry);
                    reader = new AssetReader (input, entry.Name);
                    reader.SetupReaders (obj.Asset);
                    tex.m_DataLength = (int)entry.Size;
                }
                var decoder = new Texture2DDecoder (tex, reader);
                reader = null;
                return decoder;
            }
            finally
            {
                if (reader != null)
                    reader.Dispose();
            }
        }
    }

    internal static class UnityAssetHelper
    {
        public static List<Entry> OrganizeByType (List<Entry> entries)
        {
            var organized = new List<Entry>();
            var typeGroups = new Dictionary<string, List<Entry>>();

            // Group entries by type
            foreach (var entry in entries)
            {
                string typeName = GetCleanTypeName (entry);

                if (!typeGroups.ContainsKey (typeName))
                    typeGroups[typeName] = new List<Entry>();

                typeGroups[typeName].Add (entry);
            }

            // Create folder structure
            foreach (var group in typeGroups.OrderBy (g => g.Key))
            {
                foreach (var entry in group.Value.OrderBy (e => e.Name))
                {
                    string originalName = VFS.GetFileName (entry.Name);
                    entry.Name = VFS.CombinePath (group.Key, originalName);
                    organized.Add (entry);
                }
            }

            return organized;
        }

        private static string GetCleanTypeName (Entry entry)
        {
            var assetEntry = entry as AssetEntry;
            if (assetEntry?.AssetObject == null)
                return "Resources";

            string typeName = assetEntry.AssetObject.TypeName;

            switch (typeName)
            {
                case "Texture2D":
                    return "Textures";
                case "AudioClip":
                    return "Audio";
                case "TextAsset":
                    return "Text";
                case "Shader":
                    return "Shaders";
                case "Material":
                    return "Materials";
                case "Mesh":
                    return "Meshes";
                case "AnimationClip":
                    return "Animations";
                case "Font":
                    return "Fonts";
                case "MonoBehaviour":
                    return "Scripts";
                case "GameObject":
                    return "GameObjects";
                case "Sprite":
                    return "Sprites";
                case "VideoClip":
                    return "Videos";
                default:
                    // For unknown types, try to make it more readable
                    if (string.IsNullOrEmpty(typeName))
                        return $"Type_{(entry as AssetEntry)?.AssetObject?.ClassId}";
                    return typeName;
            }
        }

        private static string GetSortName (Entry entry)
        {
            // Try to extract a meaningful sort name
            string name = entry.Name;
            if (string.IsNullOrEmpty (name))
            {
                var assetEntry = entry as AssetEntry;
                if (assetEntry != null)
                    return assetEntry.AssetObject.PathId.ToString ("X16");
            }
            return name;
        }
    }

    internal class UnityResourcesAsset : ArcFile
    {
        public readonly IDictionary<string, ArcView> ResourceMap;

        public UnityResourcesAsset (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IDictionary<string, ArcView> res_map)
            : base (arc, impl, dir)
        {
            ResourceMap = res_map;
        }

        #region IDisposable Members
        bool m_disposed = false;

        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    foreach (var res in ResourceMap.Values)
                    {
                        res.Dispose();
                    }
                }
                m_disposed = true;
            }
            base.Dispose (disposing);
        }
        #endregion
    }
}
