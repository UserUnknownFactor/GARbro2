using System;
using System.Collections;
using System.IO;
using System.Windows.Media;
using System.Xml.Linq;
using GameRes.Formats.DirectDraw;
using GameRes.Utility;

namespace GameRes.Formats.Unity
{
    enum TextureFormat : int
    {
        Alpha8 = 1,
        ARGB4444 = 2,
        RGB24 = 3,
        RGBA32 = 4,
        ARGB32 = 5,
        R16 = 6, // A 16 bit color texture format that only has a red channel.
        RGB565 = 7,
        DXT1 = 10,
        DXT5 = 12,
        RGBA4444 = 13,
        BGRA32 = 14,
        BC7 = 25,
        DXT1Crunched = 28,
        DXT5Crunched = 29,
    }

    internal class Texture2D: NamedObject
    {
        public int      m_ForcedFallbackFormat;
        public bool     m_DownscaleFallback;
        public bool     m_IsAlphaChannelOptional;

        public int      m_Width;
        public int      m_Height;
        public int      m_CompleteImageSize;
        public int      m_MipsStripped;
        public TextureFormat m_TextureFormat;
        public int      m_MipCount;
        public bool     m_MipMap;
        public bool     m_IsReadable;
        public bool     m_IsPreProcessed;
        public bool     m_IgnoreMasterTextureLimit;
        public bool     m_IgnoreMipmapLimit;
        public string   m_MipmapLimitGroupName;
        public bool     m_ReadAllowed;
        public bool     m_StreamingMipmaps;
        public int      m_StreamingMipmapsPriority;
        public int      m_ImageCount;
        public int      m_TextureDimension;
        public int      m_FilterMode;
        public int      m_Aniso;
        public float    m_MipBias;
        public int      m_WrapMode;
        public int      m_WrapV;
        public int      m_WrapW;
        public int      m_LightmapFormat;
        public int      m_ColorSpace;
        public byte[]   m_PlatformBlob;
        public int      m_DataLength;
        public byte[]   m_Data;
        public StreamingInfo m_StreamData;

        private AssetReader m_assetReader;

        public new void Load (AssetReader reader)
        {
            Load (reader, new UnityTypeData());
        }

        public void Load (AssetReader reader, UnityTypeData type)
        {
            m_assetReader = reader;
            var version = type.Version;
            base.Load (reader);

            if (version[0] > 2017 || (version[0] == 2017 && version[1] >= 3)) // 2017.3+
            {
                if (version[0] < 2023 || (version[0] == 2023 && version[1] < 2)) // < 2023.2
                {
                    m_ForcedFallbackFormat = reader.ReadInt32();
                    m_DownscaleFallback = reader.ReadBool();
                }
                if (version[0] > 2020 || (version[0] == 2020 && version[1] >= 2)) // 2020.2+
                    m_IsAlphaChannelOptional = reader.ReadBool();
                reader.Align();
            }

            m_Width = reader.ReadInt32();
            m_Height = reader.ReadInt32();
            m_CompleteImageSize = reader.ReadInt32();

            if (version[0] > 2020 || (version[0] == 2020 && version[1] >= 1)) // 2020.1+
                m_MipsStripped = reader.ReadInt32();

            m_TextureFormat = (TextureFormat)reader.ReadInt32();

            if (version[0] < 5 || (version[0] == 5 && version[1] < 2)) // < 5.2
                m_MipMap = reader.ReadBool();
            else
                m_MipCount = reader.ReadInt32();

            if (version[0] > 2 || (version[0] == 2 && version[1] >= 6)) // 2.6+
                m_IsReadable = reader.ReadBool();

            if (version[0] > 2020 || (version[0] == 2020 && version[1] >= 1)) // 2020.1+
                m_IsPreProcessed = reader.ReadBool();
            if ((version[0] == 2019 && version[1] >= 3) || (version[0] > 2019 && version[0] < 2022) ||
                (version[0] == 2022 && version[1] < 2)) // 2019.3 - 2022.2
                m_IgnoreMasterTextureLimit = reader.ReadBool();

            if (version[0] > 2022 || (version[0] == 2022 && version[1] >= 2)) // 2022.2+
            {
                m_IgnoreMipmapLimit = reader.ReadBool();
                reader.Align();
                m_MipmapLimitGroupName = reader.ReadString();
                reader.Align();
            }
            if (version[0] >= 3 && version[0] <= 5 && (version[0] < 5 || version[1] <= 4)) // 3.0 - 5.4
                m_ReadAllowed = reader.ReadBool();

            if (version[0] > 2018 || (version[0] == 2018 && version[1] >= 2)) // 2018.2+
                m_StreamingMipmaps = reader.ReadBool();

            reader.Align();

            if (version[0] > 2018 || (version[0] == 2018 && version[1] >= 2)) // 2018.2+
                m_StreamingMipmapsPriority = reader.ReadInt32();

            m_ImageCount = reader.ReadInt32();
            m_TextureDimension = reader.ReadInt32();

            // GLTextureSettings
            m_FilterMode = reader.ReadInt32();
            m_Aniso = reader.ReadInt32();
            m_MipBias = reader.ReadFloat();
            m_WrapMode = reader.ReadInt32(); // m_WrapU
            if (version[0] >= 2017) // 2017+
            {
                m_WrapV = reader.ReadInt32();
                m_WrapW = reader.ReadInt32();
            }

            if (version[0] >= 3) // 3.0+
                m_LightmapFormat = reader.ReadInt32();
            if (version[0] > 3 || (version[0] == 3 && version[1] >= 5)) // 3.5+
                m_ColorSpace = reader.ReadInt32();
            if (version[0] > 2020 || (version[0] == 2020 && version[1] >= 2)) // 2020.2+
            {
                int platformBlobLength = reader.ReadInt32();
                m_PlatformBlob = reader.ReadBytes (platformBlobLength);
                reader.Align();
            }

            m_DataLength = reader.ReadInt32();

            if (m_DataLength > 0)
                m_Data = reader.ReadBytes (m_DataLength);

            if (version[0] > 5 || (version[0] == 5 && version[1] >= 3)) // 5.3+
            {
                m_StreamData = new StreamingInfo();
                m_StreamData.Load (reader, type);
            }
        }

        public void LoadData (AssetReader reader)
        {
            if (m_Data != null && m_Data.Length > 0)
                return;

            if (m_StreamData != null && m_StreamData.Size > 0 && !string.IsNullOrEmpty (m_StreamData.Path))
            {
                // For raw assets, we need to load external data here
                // For bundles, it's loaded in OpenImage
                m_Data = UnityAssetHelper.LoadExternalResourceData (
                    m_StreamData.Path, m_StreamData.Offset, m_StreamData.Size
                );
            }
            else if (m_DataLength > 0)
                m_Data = reader.ReadBytes (m_DataLength);
        }


        public void Import (IDictionary fields)
        {
            m_Name              = fields["m_Name"] as string ?? "";
            m_Width             = (int)(fields["m_Width"] ?? 0);
            m_Height            = (int)(fields["m_Height"] ?? 0);
            m_CompleteImageSize = (int)(fields["m_CompleteImageSize"] ?? 0);
            m_TextureFormat     = (TextureFormat)(fields["m_TextureFormat"] ?? 0);
            m_MipCount          = (int)(fields["m_MipCount"] ?? 0);
            m_ImageCount        = (int)(fields["m_ImageCount"] ?? 0);
            m_TextureDimension  = (int)(fields["m_TextureDimension"] ?? 0);
            m_IsReadable        = (bool)(fields["m_IsReadable"] ?? false);
            m_Data              = fields["image data"] as byte[] ?? Array.Empty<byte>();
        }

        private int[] ParseUnityVersion (string versionString)
        {
            // Parse version string like "2021.1.3f1" to [2021, 1, 3]
            var parts = versionString.Split('.');
            var version = new int[3] { 0, 0, 0 };

            for (int i = 0; i < Math.Min (parts.Length, 3); i++)
            {
                // Extract numeric part only
                var numStr = "";
                foreach (char c in parts[i])
                {
                    if (char.IsDigit (c))
                        numStr += c;
                    else
                        break;
                }

                if (!string.IsNullOrEmpty (numStr))
                    version[i] = int.Parse (numStr);
            }

            return version;
        }
    }
}