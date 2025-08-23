// Based on the [UnityPack](https://github.com/HearthSim/UnityPack)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Policy;
using System.Text;
using GameRes.Utility;
using NAudio.SoundFont;
using static GameRes.Formats.ShiinaRio.Mi4Format;

namespace GameRes.Formats.Unity
{
    internal class Asset
    {
        int m_format;
        long m_data_offset;
        bool m_is_little_endian;
        UnityTypeData m_tree = new UnityTypeData();
        Dictionary<long, int> script_types = new Dictionary<long, int>();
        List<AssetRef> m_externals = new List<AssetRef>();
        List<SerializedType> m_reference_types = new List<SerializedType>();
        Dictionary<int, TypeTree> m_types = new Dictionary<int, TypeTree>();
        Dictionary<long, UnityObject> m_objects = new Dictionary<long, UnityObject>();

        bool m_big_id_enabled;
        long m_file_size;
        int m_metadata_size;
        long m_unknown;

        public int Format { get { return m_format; } }
        public bool IsLittleEndian { get { return m_is_little_endian; } }
        public long DataOffset { get { return m_data_offset; } }
        public UnityTypeData Tree { get { return m_tree; } }
        public IEnumerable<UnityObject> Objects { get { return m_objects.Values; } }
        public bool BigIdEnabled { get { return m_big_id_enabled; } }

        public void Load (AssetReader input)
        {
            m_metadata_size = input.ReadInt32();  // header_size
            m_file_size = input.ReadUInt32(); // file_size
            m_format = input.ReadInt32();
            m_data_offset = input.ReadUInt32();

            if (m_format >= 9)
            {
                m_is_little_endian = 0 == input.ReadByte();
                input.Skip (3); // reserved bytes

                if (m_format >= 22)
                {
                    m_metadata_size = input.ReadInt32();
                    m_file_size = input.ReadInt64();
                    m_data_offset = input.ReadInt64();
                    m_unknown = input.ReadInt64();
                }
            }
            else
            {
                long savedPos = input.Position;
                input.Position = m_file_size - m_metadata_size;
                m_is_little_endian = 0 == input.ReadByte();
                input.Position = savedPos;
            }

            input.SetupReaders (this);
            m_tree.Load (input);

            if (m_format >= 14)
                m_big_id_enabled = true;
            else if (m_format >= 7 && m_format < 14)
                m_big_id_enabled = 0 != input.ReadInt32();
            else
                m_big_id_enabled = false;

            input.SetupReadId (m_big_id_enabled);

            int obj_count = input.ReadInt32();
            ArchiveFormat.IsSaneCountEx (obj_count);
            for (int i = 0; i < obj_count; ++i)
            {
                input.Align();
                var obj = new UnityObject (this, input.Name);
                obj.Load (input);
                RegisterObject (obj);
            }

            if (m_format >= 11)
            {
                int count = input.ReadInt32();
                ArchiveFormat.IsSaneCountEx (count);
                script_types = new Dictionary<long, int>(count);
                for (int i = 0; i < count; ++i)
                {
                    var obj_id = new LocalSerializedObjectIdentifier();
                    obj_id.Load (input, m_format);
                    script_types[obj_id.ID] = obj_id.FileIdx;
                }
            }

            if (m_format >= 6)
            {
                int count = input.ReadInt32();
                ArchiveFormat.IsSaneCountEx (count);
                m_externals = new List<AssetRef>(count);
                for (int i = 0; i < count; ++i)
                {
                    var asset_ref = AssetRef.Load (input, m_format);
                    m_externals.Add (asset_ref);
                }
            }

            if (m_format >= 20)
            {
                int count = input.ReadInt32();
                ArchiveFormat.IsSaneCountEx (count);
                for (int i = 0; i < count; ++i)
                {
                    var new_type = new SerializedType (m_format, true);
                    new_type.Load (input, m_tree.hasDependencies);
                    m_reference_types.Add (new_type);
                }
            }

            if (m_format >= 5)
                input.ReadCString(); // userInformation
        }

        void RegisterObject (UnityObject obj)
        {
            if (m_tree.TypeTrees.ContainsKey (obj.TypeId))
            {
                m_types[obj.TypeId] = m_tree.TypeTrees[obj.TypeId];
            }
            else if (!m_types.ContainsKey (obj.TypeId))
            {
                /*
                var trees = TypeTree.Default (this).TypeTrees;
                if (trees.ContainsKey (obj.ClassId))
                {
                    m_types[obj.TypeId] = trees[obj.ClassId];
                }
                else
                */
                {
                    Trace.WriteLine (string.Format("Unknown type id {0}", obj.ClassId.ToString()), "[Unity.Asset]");
                    m_types[obj.TypeId] = null;
                }
            }
            if (m_objects.ContainsKey (obj.PathId))
                throw new ApplicationException (string.Format("Duplicate asset object {0} (PathId: {1})", obj, obj.PathId));
            m_objects[obj.PathId] = obj;
        }
    }

    class LocalSerializedObjectIdentifier
    {
        int m_local_serialized_file_index;
        long m_local_identifier_in_file;

        public long ID { get { return m_local_identifier_in_file; } }

        public int FileIdx { get { return m_local_serialized_file_index; } }

        public void Load (AssetReader reader, int format)
        {
            m_local_serialized_file_index = reader.ReadInt32();
            if (format < 14)
                m_local_identifier_in_file = (long)reader.ReadInt32();
            else
            {
                reader.Align();
                m_local_identifier_in_file = reader.ReadInt64();
            }
        }

    }

    internal class AssetRef
    {
        public string AssetPath;
        public Guid   Guid;
        public int    Type;
        public string FilePath;
        public object Asset;

        public static AssetRef Load (AssetReader reader, int format)
        {
            var r = new AssetRef();
            if (format >= 6)
                r.AssetPath = reader.ReadCString();

            if (format >= 5)
            {
                r.Guid = new Guid (reader.ReadBytes (16));
                r.Type = reader.ReadInt32();
            }
            r.FilePath = reader.ReadCString();
            r.Asset = null;
            return r;
        }
    }

    internal class UnityObject
    {
        public Asset  Asset;
        public string ContainerName; 
        public long   PathId;
        public long   Offset;
        public uint   Size;
        public int    TypeId;
        public int    ClassId;
        public bool   IsDestroyed;
        public byte   Stripped; // for format 15, 16

        public UnityObject (Asset owner, string name = "")
        {
            Asset = owner;
            ContainerName = name;
        }

        public AssetReader Open (Stream input)
        {
            var stream = new StreamRegion (input, Offset, Size, true);
            var reader = new AssetReader (stream, "");
            reader.SetupReaders (Asset);
            return reader;
        }

        public void Load (AssetReader reader)
        {
            if (Asset.BigIdEnabled)
                PathId = reader.ReadInt64();
            else if (Asset.Format < 14)
                PathId = reader.ReadInt32();
            else
            {
                reader.Align();
                PathId = reader.ReadInt64();
            }

            if (Asset.Format >= 22)
                Offset = reader.ReadInt64();
            else
                Offset = reader.ReadOffset();
            Offset += Asset.DataOffset;

            Size = reader.ReadUInt32();

            if (Asset.Format < 16)
            {
                TypeId = reader.ReadInt32();
                ClassId = reader.ReadInt16();
            }
            else
            {
                var type_id = reader.ReadInt32();
                var class_id = Asset.Tree.ClassIds[type_id];
                TypeId = class_id;
                ClassId = class_id;
            }

            if (Asset.Format < 11)
                IsDestroyed = reader.ReadInt16() != 0;

            if (Asset.Format >= 11 && Asset.Format < 17)
                reader.ReadInt16(); // script_type_index

            if (Asset.Format >= 15 && Asset.Format < 17)
                Stripped = reader.ReadByte();
        }

        #region ClassID to Name Map
        private static readonly Dictionary<int, string> ClassIDToName = new Dictionary<int, string>
        {
            {0, "Object"},
            {1, "GameObject"},
            {2, "Component"},
            {3, "LevelGameManager"},
            {4, "Transform"},
            {5, "TimeManager"},
            {6, "GlobalGameManager"},
            {8, "Behaviour"},
            {9, "GameManager"},
            {11, "AudioManager"},
            {13, "InputManager"},
            {18, "EditorExtension"},
            {19, "Physics2DSettings"},
            {20, "Camera"},
            {21, "Material"},
            {23, "MeshRenderer"},
            {25, "Renderer"},
            {27, "Texture"},
            {28, "Texture2D"},
            {29, "OcclusionCullingSettings"},
            {30, "GraphicsSettings"},
            {33, "MeshFilter"},
            {41, "OcclusionPortal"},
            {43, "Mesh"},
            {45, "Skybox"},
            {47, "QualitySettings"},
            {48, "Shader"},
            {49, "TextAsset"},
            {50, "Rigidbody2D"},
            {53, "Collider2D"},
            {54, "Rigidbody"},
            {55, "PhysicsManager"},
            {56, "Collider"},
            {57, "Joint"},
            {58, "CircleCollider2D"},
            {59, "HingeJoint"},
            {60, "PolygonCollider2D"},
            {61, "BoxCollider2D"},
            {62, "PhysicsMaterial2D"},
            {64, "MeshCollider"},
            {65, "BoxCollider"},
            {66, "CompositeCollider2D"},
            {68, "EdgeCollider2D"},
            {70, "CapsuleCollider2D"},
            {72, "ComputeShader"},
            {74, "AnimationClip"},
            {75, "ConstantForce"},
            {78, "TagManager"},
            {81, "AudioListener"},
            {82, "AudioSource"},
            {83, "AudioClip"},
            {84, "RenderTexture"},
            {86, "CustomRenderTexture"},
            {89, "Cubemap"},
            {90, "Avatar"},
            {91, "AnimatorController"},
            {93, "RuntimeAnimatorController"},
            {94, "ShaderNameRegistry"},
            {95, "Animator"},
            {96, "TrailRenderer"},
            {98, "DelayedCallManager"},
            {102, "TextMesh"},
            {104, "RenderSettings"},
            {108, "Light"},
            {109, "ShaderInclude"},
            {110, "BaseAnimationTrack"},
            {111, "Animation"},
            {114, "MonoBehaviour"},
            {115, "MonoScript"},
            {116, "MonoManager"},
            {117, "Texture3D"},
            {118, "NewAnimationTrack"},
            {119, "Projector"},
            {120, "LineRenderer"},
            {121, "Flare"},
            {122, "Halo"},
            {123, "LensFlare"},
            {124, "FlareLayer"},
            {126, "NavMeshProjectSettings"},
            {128, "Font"},
            {129, "PlayerSettings"},
            {130, "NamedObject"},
            {134, "PhysicsMaterial"},
            {135, "SphereCollider"},
            {136, "CapsuleCollider"},
            {137, "SkinnedMeshRenderer"},
            {138, "FixedJoint"},
            {141, "BuildSettings"},
            {142, "AssetBundle"},
            {143, "CharacterController"},
            {144, "CharacterJoint"},
            {145, "SpringJoint"},
            {146, "WheelCollider"},
            {147, "ResourceManager"},
            {150, "PreloadData"},
            {152, "MovieTexture"},
            {153, "ConfigurableJoint"},
            {154, "TerrainCollider"},
            {156, "TerrainData"},
            {157, "LightmapSettings"},
            {158, "WebCamTexture"},
            {159, "EditorSettings"},
            {162, "EditorUserSettings"},
            {164, "AudioReverbFilter"},
            {165, "AudioHighPassFilter"},
            {166, "AudioChorusFilter"},
            {167, "AudioReverbZone"},
            {168, "AudioEchoFilter"},
            {169, "AudioLowPassFilter"},
            {170, "AudioDistortionFilter"},
            {171, "SparseTexture"},
            {180, "AudioBehaviour"},
            {181, "AudioFilter"},
            {182, "WindZone"},
            {183, "Cloth"},
            {184, "SubstanceArchive"},
            {185, "ProceduralMaterial"},
            {186, "ProceduralTexture"},
            {187, "Texture2DArray"},
            {188, "CubemapArray"},
            {191, "OffMeshLink"},
            {192, "OcclusionArea"},
            {193, "Tree"},
            {195, "NavMeshAgent"},
            {196, "NavMeshSettings"},
            {198, "ParticleSystem"},
            {199, "ParticleSystemRenderer"},
            {200, "ShaderVariantCollection"},
            {205, "LODGroup"},
            {206, "BlendTree"},
            {207, "Motion"},
            {208, "NavMeshObstacle"},
            {210, "SortingGroup"},
            {212, "SpriteRenderer"},
            {213, "Sprite"},
            {214, "CachedSpriteAtlas"},
            {215, "ReflectionProbe"},
            {218, "Terrain"},
            {220, "LightProbeGroup"},
            {221, "AnimatorOverrideController"},
            {222, "CanvasRenderer"},
            {223, "Canvas"},
            {224, "RectTransform"},
            {225, "CanvasGroup"},
            {226, "BillboardAsset"},
            {227, "BillboardRenderer"},
            {228, "SpeedTreeWindAsset"},
            {229, "AnchoredJoint2D"},
            {230, "Joint2D"},
            {231, "SpringJoint2D"},
            {232, "DistanceJoint2D"},
            {233, "HingeJoint2D"},
            {234, "SliderJoint2D"},
            {235, "WheelJoint2D"},
            {236, "ClusterInputManager"},
            {237, "BaseVideoTexture"},
            {238, "NavMeshData"},
            {240, "AudioMixer"},
            {241, "AudioMixerController"},
            {243, "AudioMixerGroupController"},
            {244, "AudioMixerEffectController"},
            {245, "AudioMixerSnapshotController"},
            {246, "PhysicsUpdateBehaviour2D"},
            {247, "ConstantForce2D"},
            {248, "Effector2D"},
            {249, "AreaEffector2D"},
            {250, "PointEffector2D"},
            {251, "PlatformEffector2D"},
            {252, "SurfaceEffector2D"},
            {253, "BuoyancyEffector2D"},
            {254, "RelativeJoint2D"},
            {255, "FixedJoint2D"},
            {256, "FrictionJoint2D"},
            {257, "TargetJoint2D"},
            {258, "LightProbes"},
            {259, "LightProbeProxyVolume"},
            {271, "SampleClip"},
            {272, "AudioMixerSnapshot"},
            {273, "AudioMixerGroup"},
            {290, "AssetBundleManifest"},
            {300, "RuntimeInitializeOnLoadManager"},
            {310, "UnityConnectSettings"},
            {319, "AvatarMask"},
            {320, "PlayableDirector"},
            {328, "VideoPlayer"},
            {329, "VideoClip"},
            {330, "ParticleSystemForceField"},
            {331, "SpriteMask"},
            {363, "OcclusionCullingData"},
            {900, "MarshallingTestObject"},
            {1001, "PrefabInstance"},
            {1002, "EditorExtensionImpl"},
            {1003, "AssetImporter"},
            {1005, "Mesh3DSImporter"},
            {1006, "TextureImporter"},
            {1007, "ShaderImporter"},
            {1008, "ComputeShaderImporter"},
            {1020, "AudioImporter"},
            {1026, "HierarchyState"},
            {1028, "AssetMetaData"},
            {1029, "DefaultAsset"},
            {1030, "DefaultImporter"},
            {1031, "TextScriptImporter"},
            {1032, "SceneAsset"},
            {1034, "NativeFormatImporter"},
            {1035, "MonoImporter"},
            {1038, "LibraryAssetImporter"},
            {1040, "ModelImporter"},
            {1041, "FBXImporter"},
            {1042, "TrueTypeFontImporter"},
            {1045, "EditorBuildSettings"},
            {1048, "InspectorExpandedState"},
            {1049, "AnnotationManager"},
            {1050, "PluginImporter"},
            {1051, "EditorUserBuildSettings"},
            {1055, "IHVImageFormatImporter"},
            {1101, "AnimatorStateTransition"},
            {1102, "AnimatorState"},
            {1105, "HumanTemplate"},
            {1107, "AnimatorStateMachine"},
            {1108, "PreviewAnimationClip"},
            {1109, "AnimatorTransition"},
            {1110, "SpeedTreeImporter"},
            {1111, "AnimatorTransitionBase"},
            {1112, "SubstanceImporter"},
            {1113, "LightmapParameters"},
            {1120, "LightingDataAsset"},
            {1124, "SketchUpImporter"},
            {1125, "BuildReport"},
            {1126, "PackedAssets"},
            {1127, "VideoClipImporter"},
            {100000, "int"},
            {100001, "bool"},
            {100002, "float"},
            {100003, "MonoObject"},
            {100004, "Collision"},
            {100005, "Vector3f"},
            {100006, "RootMotionData"},
            {100007, "Collision2D"},
            {100008, "AudioMixerLiveUpdateFloat"},
            {100009, "AudioMixerLiveUpdateBool"},
            {100010, "Polygon2D"},
            {100011, "void"},
            {19719996, "TilemapCollider2D"},
            {41386430, "ImportLog"},
            {55640938, "GraphicsStateCollection"},
            {73398921, "VFXRenderer"},
            {156049354, "Grid"},
            {156483287, "ScenesUsingAssets"},
            {171741748, "ArticulationBody"},
            {181963792, "Preset"},
            {285090594, "IConstraint"},
            {294290339, "AssemblyDefinitionReferenceImporter"},
            {355983997, "AudioResource"},
            {369655926, "AssetImportInProgressProxy"},
            {382020655, "PluginBuildInfo"},
            {387306366, "MemorySettings"},
            {403037116, "BuildMetaDataImporter"},
            {403037117, "BuildInstructionImporter"},
            {426301858, "EditorProjectAccess"},
            {468431735, "PrefabImporter"},
            {483693784, "TilemapRenderer"},
            {612988286, "SpriteAtlasAsset"},
            {638013454, "SpriteAtlasDatabase"},
            {641289076, "AudioBuildInfo"},
            {644342135, "CachedSpriteAtlasRuntimeData"},
            {655991488, "MultiplayerManager"},
            {662584278, "AssemblyDefinitionReferenceAsset"},
            {668709126, "BuiltAssetBundleInfoSet"},
            {687078895, "SpriteAtlas"},
            {702665669, "DifferentMarshallingTestObject"},
            {747330370, "RayTracingShaderImporter"},
            {780535461, "BuildArchiveImporter"},
            {815301076, "PreviewImporter"},
            {825902497, "RayTracingShader"},
            {850595691, "LightingSettings"},
            {877146078, "PlatformModuleSetup"},
            {890905787, "VersionControlSettings"},
            {893571522, "CustomCollider2D"},
            {895512359, "AimConstraint"},
            {937362698, "VFXManager"},
            {947337230, "RoslynAnalyzerConfigAsset"},
            {954905827, "RuleSetFileAsset"},
            {994735392, "VisualEffectSubgraph"},
            {994735403, "VisualEffectSubgraphOperator"},
            {994735404, "VisualEffectSubgraphBlock"},
            {1001480554, "Prefab"},
            {1027052791, "LocalizationImporter"},
            {1114811875, "ReferencesArtifactGenerator"},
            {1152215463, "AssemblyDefinitionAsset"},
            {1154873562, "SceneVisibilityState"},
            {1183024399, "LookAtConstraint"},
            {1210832254, "SpriteAtlasImporter"},
            {1223240404, "MultiArtifactTestImporter"},
            {1233149941, "AudioContainerElement"},
            {1268269756, "GameObjectRecorder"},
            {1307931743, "AudioRandomContainer"},
            {1325145578, "LightingDataAssetParent"},
            {1386491679, "PresetManager"},
            {1403656975, "StreamingManager"},
            {1480428607, "LowerResBlitTexture"},
            {1521398425, "VideoBuildInfo"},
            {1541671625, "C4DImporter"},
            {1542919678, "StreamingController"},
            {1557264870, "ShaderContainer"},
            {1597193336, "RoslynAdditionalFileAsset"},
            {1642787288, "RoslynAdditionalFileImporter"},
            {1652712579, "MultiplayerRolesData"},
            {1660057539, "SceneRoots"},
            {1731078267, "BrokenPrefabAsset"},
            {1736697216, "AndroidAssetPackImporter"},
            {1740304944, "VulkanDeviceFilterLists"},
            {1742807556, "GridLayout"},
            {1766753193, "AssemblyDefinitionImporter"},
            {1773428102, "ParentConstraint"},
            {1777034230, "RuleSetFileImporter"},
            {1818360608, "PositionConstraint"},
            {1818360609, "RotationConstraint"},
            {1818360610, "ScaleConstraint"},
            {1839735485, "Tilemap"},
            {1896753125, "PackageManifest"},
            {1896753126, "PackageManifestImporter"},
            {1903396204, "RoslynAnalyzerConfigImporter"},
            {1931382933, "UIRenderer"},
            {1953259897, "TerrainLayer"},
            {1971053207, "SpriteShapeRenderer"},
            {2058629509, "VisualEffectAsset"},
            {2058629510, "VisualEffectImporter"},
            {2058629511, "VisualEffectResource"},
            {2059678085, "VisualEffectObject"},
            {2083052967, "VisualEffect"},
            {2083778819, "LocalizationAsset"},
            {2089858483, "ScriptedImporter"},
            {2103361453, "ShaderIncludeImporter"}
        };
        #endregion

        public string TypeName
        {
            get
            {
                if (ClassIDToName.TryGetValue(ClassId, out string typeName))
                    return typeName;
        
                return string.Format("Type{0}", ClassId);
            }
        }

        public TypeTree Type
        {
            get
            {
                TypeTree type;
                Asset.Tree.TypeTrees.TryGetValue (TypeId, out type);
                return type;
            }
        }

        public override string ToString()
        {
            return string.Format("<{0} {1}>", Type, ClassId);
        }

        public IDictionary Deserialize (AssetReader input)
        {
            var type_tree = Asset.Tree.TypeTrees;
            if (!type_tree.ContainsKey (TypeId))
                return null;
            var type_map = new Hashtable();
            var type = type_tree[TypeId];
            foreach (var node in type.Children)
            {
                type_map[node.Name] = DeserializeType (input, node);
            }
            return type_map;
        }

        object DeserializeType (AssetReader input, TypeTree node)
        {
            object obj = null;
            if (node.IsArray)
            {
                int size = input.ReadInt32();
                var data_field = node.Children.FirstOrDefault (n => n.Name == "data");
                if (data_field != null)
                {
                    if ("TypelessData" == node.Type)
                        obj = input.ReadBytes (size * data_field.Size);
                    else
                        obj = DeserializeArray (input, size, data_field);
                }
            }
            else if (node.Size < 0)
            {
                if (node.Type == "string")
                {
                    obj = input.ReadString();
                    if (node.Children[0].IsAligned)
                        input.Align();
                }
                else if (node.Type == "StreamingInfo")
                {
                    var info = new StreamingInfo();
                    info.Load (input, Asset.Tree);
                    obj = info;
                }
                else
                    throw new NotImplementedException("Unknown class encountered in asset deserialzation.");
            }
            else if ("int" == node.Type)
                obj = input.ReadInt32();
            else if ("unsigned int" == node.Type)
                obj = input.ReadUInt32();
            else if ("bool" == node.Type)
                obj = input.ReadBool();
            else
                input.Position += node.Size;
            if (node.IsAligned)
                input.Align();
            return obj;
        }

        object[] DeserializeArray (AssetReader input, int length, TypeTree elem)
        {
            var array = new object[length];
            for (int i = 0; i < length; ++i)
                array[i] = DeserializeType (input, elem);
            return array;
        }
    }

    class ReferenceType
    {
        string m_ClassName;
        string m_NameSpace;
        string m_AssemblyName;

        public void Load (AssetReader reader)
        {
            m_ClassName = reader.ReadCString();
            m_NameSpace = reader.ReadCString();
            m_AssemblyName = reader.ReadCString();
        }
    }

    internal class TypeTree
    {
        int m_format;
        List<TypeTree> m_children = new List<TypeTree>();

        public int Version;
        public bool IsArray;
        public string Type;
        public string Name;
        public int Size;
        public uint Index;
        public int Flags;
        public long RefTypeHash; // for format >= 19

        public IList<TypeTree> Children { get { return m_children; } }

        public bool IsAligned { get { return (Flags & 0x4000) != 0; } }

        static readonly string Null = "(null)";
        static readonly Lazy<byte[]> StringsDat = new Lazy<byte[]>(() => LoadResource("strings.dat"));

        public TypeTree (int format)
        {
            m_format = format;
            m_string = null;
        }

        public void Load (AssetReader reader)
        {
            if (10 == m_format || m_format >= 12)
                LoadBlob (reader);
            else
                LoadRaw (reader);
        }

        void LoadRaw (AssetReader reader)
        {
            Type = reader.ReadCString();
            Name = reader.ReadCString();
            Size = reader.ReadInt32();
            Index = reader.ReadUInt32();
            IsArray = reader.ReadInt32() != 0;
            Version = reader.ReadInt32();
            Flags = reader.ReadInt32();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; ++i)
            {
                var child = new TypeTree (m_format);
                child.Load (reader);
                Children.Add (child);
            }
        }

        byte[] m_string;

        void LoadBlob (AssetReader reader)
        {
            int count = reader.ReadInt32();
            int buffer_bytes = reader.ReadInt32();
            int node_struct_size = m_format >= 19 ? 32 : 24;

            byte[] struct_data = null;

            if (count <= 0)
                return;
            struct_data = reader.ReadBytes (node_struct_size * count);
            m_string = reader.ReadBytes (buffer_bytes);

            var parents = new Stack<TypeTree>();
            parents.Push (this);
            using (var buf = new BinMemoryStream (struct_data))
            {
                for (int i = 0; i < count; ++i)
                {
                    int version = buf.ReadInt16();
                    int depth = buf.ReadUInt8();
                    TypeTree current;
                    if (0 == depth)
                        current = this;
                    else
                    {
                        while (parents.Count > depth)
                            parents.Pop();
                        current = new TypeTree (m_format);
                        parents.Peek().Children.Add (current);
                        parents.Push (current);
                    }
                    current.Version = version;
                    current.IsArray = buf.ReadUInt8() != 0;
                    current.Type = GetString (buf.ReadInt32());
                    current.Name = GetString (buf.ReadInt32());
                    current.Size = buf.ReadInt32();
                    current.Index = buf.ReadUInt32();
                    current.Flags = buf.ReadInt32();
                    if (m_format < 19) continue;
                    current.RefTypeHash = buf.ReadInt64();
                }
            }
        }

        string GetString (int offset)
        {
            byte[] strings;
            if (offset < 0)
            {
                offset &= 0x7FFFFFFF;
                strings = StringsDat.Value;
            }
            else if (offset < m_string.Length)
                strings = m_string;
            else
                return Null;
            return Binary.GetCString (strings, offset, strings.Length - offset, Encoding.UTF8);
        }

        internal static byte[] LoadResource (string name)
        {
            var res = EmbeddedResource.Load (name, typeof (TypeTree));
            if (null == res)
                throw new FileNotFoundException("Resource not found.", name);
            return res;
        }
    }

    internal class SerializedType
    {
        int m_format;
        bool m_is_ref_type;
        int[] m_type_dependencies;
        ReferenceType m_ref_type;
        TypeTree m_type_tree;
        int m_class_id;
        bool m_is_stripped_type;
        short m_script_type_index;
        byte[] m_script_id;
        byte[] m_old_type_hash;

        public int ClassID { get { return m_class_id; } }
        public byte[] Hash { get { return m_old_type_hash; } }
        public TypeTree Tree { get { return m_type_tree; } }

        public SerializedType (int format, bool is_ref_type = false) {
            m_is_ref_type = is_ref_type;
            m_format = format;
        }

        public void Load (AssetReader reader, bool has_dependencies)
        {
            m_class_id = reader.ReadInt32();

            if (m_format >= 16)
                m_is_stripped_type = reader.ReadBool();

            if (m_format >= 17)
                m_script_type_index = reader.ReadInt16();

            if (m_format >= 13)
            {
                m_script_id = null;
                if ((m_is_ref_type && m_script_type_index >= 0) ||
                    (m_format < 16 && m_class_id < 0) ||
                    (m_format >= 16 && m_class_id == 114)
                )
                {
                    m_script_id = reader.ReadBytes (16);
                }
                m_old_type_hash = reader.ReadBytes (16);
            }

            if (has_dependencies) {
                m_type_tree = new TypeTree (m_format);
                m_type_tree.Load (reader);

                int format = reader.Format;
                if (format >= 21)
                {
                    if (m_is_ref_type)
                    {
                        m_ref_type = new ReferenceType();
                        m_ref_type.Load (reader);
                    }
                    else
                        m_type_dependencies = reader.ReadInt32Array();
                }
            }
        }

    }

    internal class UnityTypeData
    {
        int[] m_version;
        List<int> m_class_ids;
        List<SerializedType> m_serialized_types;
        Dictionary<int, byte[]> m_hashes;
        Dictionary<int, TypeTree> m_type_trees;
        bool m_has_dependence_types;

        public UnityTypeData()
        {
            m_version = new int[4] { 2017, 0, 0, 0 };
            m_class_ids = new List<int>();
            m_serialized_types = new List<SerializedType>();
            m_hashes =  new Dictionary<int, byte[]>();
            m_type_trees = new Dictionary<int, TypeTree>();
            m_has_dependence_types = false;
        }

        public int[] Version { get { return m_version; } }
        public IList<int> ClassIds { get { return m_class_ids; } }
        public IDictionary<int, byte[]> Hashes { get { return m_hashes; } }
        public IDictionary<int, TypeTree> TypeTrees { get { return m_type_trees; } }
        public bool hasDependencies { get { return m_has_dependence_types; } }

        public void Load (AssetReader reader)
        {
            int format = reader.Format;
            int platform = 0;

            if (format >= 7)
                m_version = ParseUnityVersion (reader.ReadCString());

            if (format >= 8)
                platform = reader.ReadInt32();

            if (format >= 13)
                m_has_dependence_types = reader.ReadBool();

            int count = reader.ReadInt32();
            ArchiveFormat.IsSaneCountEx (count);
            for (int i = 0; i < count; ++i)
            {
                var new_type = new SerializedType (format);
                new_type.Load (reader, m_has_dependence_types);
                m_class_ids.Add (new_type.ClassID);
                m_hashes[new_type.ClassID] = new_type.Hash;
                m_type_trees[new_type.ClassID] = new_type.Tree;
                m_serialized_types.Add (new_type);
            }
        }

        static public int[] ParseUnityVersion (string versionString)
        {
            var result = new int[4] { 2017, 0, 0, 0 };

            if (string.IsNullOrEmpty (versionString))
                return result;

            var parts = versionString.Split('.');

            for (int i = 0; i < parts.Length && i < 3; i++)
            {
                string numStr = "";
                foreach (char c in parts[i])
                {
                    if (char.IsDigit (c))
                        numStr += c;
                    else
                        break;
                }

                if (!string.IsNullOrEmpty (numStr))
                    result[i] = int.Parse (numStr);
            }

            if (parts.Length > 0)
            {
                string lastPart = parts[parts.Length - 1];
                for (int i = 0; i < lastPart.Length; i++)
                {
                    if (!char.IsDigit (lastPart[i]))
                    {
                        result[3] = (int)lastPart[i];
                        break;
                    }
                }
            }

            return result;
        }
    }

    internal class NamedObject
    {
        public string m_Name;

        public void Load(AssetReader reader)
        {
            m_Name = reader.ReadString();
            reader.Align();
        }

        public static string PeekName(AssetReader reader, long offset)
        {
            string name = null;
            try
            {
                long savedPos = reader.Position;
                reader.Position = offset;
                name = reader.ReadString();
                reader.Position = savedPos;
            }
            catch { }
            return name;
        }
    }

    internal class StreamingInfo
    {
        public long Offset;
        public uint Size;
        public string Path;

        public void Load (AssetReader reader, UnityTypeData type)
        {
            if (type.Version[0] >= 2020)
                Offset = reader.ReadInt64();
            else
                Offset = reader.ReadUInt32();
            Size = reader.ReadUInt32();
            Path = reader.ReadString();
            reader.Align();
        }
    }
}
