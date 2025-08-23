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

        public new void Load(AssetReader reader)
        {
            Load(reader, new UnityTypeData());
        }

        public void Load(AssetReader reader, UnityTypeData type)
        {
            m_assetReader = reader;

            var version = type.Version;

            base.Load(reader);

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
                m_PlatformBlob = reader.ReadBytes(platformBlobLength);
                reader.Align();
            }

            m_DataLength = reader.ReadInt32();

            if (m_DataLength > 0)
                m_Data = reader.ReadBytes(m_DataLength);

            if (version[0] > 5 || (version[0] == 5 && version[1] >= 3)) // 5.3+
            {
                m_StreamData = new StreamingInfo();
                m_StreamData.Load(reader, type);
            }
        }

        public void LoadData (AssetReader reader)
        {
            if (m_StreamData != null && m_StreamData.Size > 0 && !string.IsNullOrEmpty(m_StreamData.Path))
                m_Data = LoadStreamingData(reader.Name);
            else if (m_Data == null || m_Data.Length == 0)
                m_Data = reader.ReadBytes(m_DataLength);
        }

        private byte[] LoadStreamingData(string source)
        {
            if (m_StreamData == null || string.IsNullOrEmpty(m_StreamData.Path))
                return null;

            string resourcePath = m_StreamData.Path;
            try
            {
                // First try to find the file in the entire VFS hierarchy
                try
                {
                    var entry = VFS.FindFileInHierarchy(resourcePath);
                    if (entry != null)
                    {
                        using (var input = VFS.OpenStreamInHierarchy(entry))
                        {
                            return VFS.ReadFromAnyStream(input, m_StreamData.Offset, m_StreamData.Size);
                        }
                    }
                }
                catch (FileNotFoundException)
                {
                }

                // If not found and it's a .resS file, try relative to current archive
                if (resourcePath.EndsWith(".resS", StringComparison.OrdinalIgnoreCase))
                {
                    var assetFile = m_assetReader;
                    if (assetFile != null && VFS.CurrentArchive != null)
                    {
                        try
                        {
                            // Try to find in the same directory as the current archive
                            var entry = VFS.FindFileInArchiveDirectory(resourcePath);
                            if (entry != null)
                            {
                                using (var input = VFS.OpenStreamInHierarchy(entry))
                                {
                                    return VFS.ReadFromAnyStream(input, m_StreamData.Offset, m_StreamData.Size);
                                }
                            }
                        }
                        catch (FileNotFoundException)
                        {
                            // Continue
                        }

                        // If still not found, try the real filesystem as a last resort
                        var dir = VFS.GetDirectoryName(VFS.CurrentArchive.File.Name);
                        if (VFS.IsPathRooted(dir))
                        {
                            var fullPath = Path.Combine(dir, resourcePath);
                            if (File.Exists(fullPath))
                            {
                                using (var input = BinaryStream.FromFile(fullPath))
                                {
                                    input.Position = m_StreamData.Offset;
                                    var data = new byte[m_StreamData.Size];
                                    input.Read(data, 0, (int)m_StreamData.Size);
                                    return data;
                                }
                            }
                        }
                    }
                }

                // Try with just the filename if all else fails
                var fileName = VFS.GetFileName(resourcePath);
                if (fileName != resourcePath)
                {
                    try
                    {
                        var entry = VFS.FindFileInHierarchy(fileName);
                        if (entry != null)
                        {
                            using (var input = VFS.OpenStreamInHierarchy(entry))
                            {
                                return VFS.ReadFromAnyStream(input, m_StreamData.Offset, m_StreamData.Size);
                            }
                        }
                    }
                    catch (FileNotFoundException)
                    {
                        new FileNotFoundException(String.Format("Unable to find resource {}.", resourcePath));
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
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

        private int[] ParseUnityVersion(string versionString)
        {
            // Parse version string like "2021.1.3f1" to [2021, 1, 3]
            var parts = versionString.Split('.');
            var version = new int[3] { 0, 0, 0 };

            for (int i = 0; i < Math.Min(parts.Length, 3); i++)
            {
                // Extract numeric part only
                var numStr = "";
                foreach (char c in parts[i])
                {
                    if (char.IsDigit(c))
                        numStr += c;
                    else
                        break;
                }

                if (!string.IsNullOrEmpty(numStr))
                    version[i] = int.Parse(numStr);
            }

            return version;
        }
    }

    internal class Texture2DDecoder : IImageDecoder
    {
        AssetReader     m_reader;
        Texture2D       m_texture;
        ImageData       m_image;

        public Stream            Source { get { m_reader.Position = 0; return m_reader.Source; } }
        public ImageFormat SourceFormat { get { return null; } }
        public PixelFormat       Format { get; private set; }
        public ImageMetaData       Info { get; private set; }
        public ImageData          Image { get { return m_image ?? (m_image = Unpack()); } }

        public Texture2DDecoder (Texture2D texture, AssetReader input)
        {
            m_reader  = input;
            m_texture = texture;
            Info = new ImageMetaData {
                Width   = (uint)m_texture.m_Width,
                Height  = (uint)m_texture.m_Height,
            };
            SetFormat (m_texture.m_TextureFormat);
        }

        void SetFormat (TextureFormat format)
        {
            switch (format)
            {
            case TextureFormat.Alpha8:
                Format = PixelFormats.Gray8;
                Info.BPP = 8;
                break;

            case TextureFormat.R16:
                Format = PixelFormats.Gray16;
                Info.BPP = 16;
                break;

            case TextureFormat.RGB24:
                Format = PixelFormats.Rgb24;
                Info.BPP = 24;
                break;

            case TextureFormat.RGB565:
                Format = PixelFormats.Bgr565;
                Info.BPP = 16;
                break;

            default:
                Format = PixelFormats.Bgra32;
                Info.BPP = 32;
                break;
            }
        }

        ImageData Unpack ()
        {
            if (null == m_texture.m_Data || 0 == m_texture.m_Data.Length)
                m_texture.LoadData (m_reader);

            if (null == m_texture.m_Data || 0 == m_texture.m_Data.Length)
                throw new NotImplementedException("Can't locate Texture2D data.");

            byte[] pixels;
            switch (m_texture.m_TextureFormat)
            {
            case TextureFormat.DXT1:
                {
                    var decoder = new DxtDecoder (m_texture.m_Data, Info);
                    pixels = decoder.UnpackDXT1();
                    break;
                }
            case TextureFormat.DXT5:
                {
                    var decoder = new DxtDecoder (m_texture.m_Data, Info);
                    pixels = decoder.UnpackDXT5();
                    break;
                }
            case TextureFormat.Alpha8:
            case TextureFormat.R16:
            case TextureFormat.RGB24:
            case TextureFormat.BGRA32:
            case TextureFormat.RGB565:
                pixels = m_texture.m_Data;
                break;

            case TextureFormat.ARGB32:
                pixels = ConvertArgb (m_texture.m_Data);
                break;

            case TextureFormat.RGBA32:
                pixels = ConvertRgba (m_texture.m_Data);
                break;

            case TextureFormat.ARGB4444:
                pixels = ConvertArgb16 (m_texture.m_Data);
                break;

            case TextureFormat.BC7:
                {
                    var decoder = new Bc7Decoder (m_texture.m_Data, Info);
                    pixels = decoder.Unpack();
                    break;
                }
            default:
                throw new NotImplementedException (string.Format ("Unsupported Texture2D format: '{0}'.", m_texture.m_TextureFormat));
            }
            if (pixels == null)
                throw new NotImplementedException("Cannot read Texture2D data.");
            return ImageData.CreateFlipped (Info, Format, null, pixels, (int)Info.Width*((Format.BitsPerPixel+7)/8));
        }

        byte[] ConvertArgb (byte[] data)
        {
            // XXX conversion performed in-place.
            for (int i = 0; i < data.Length; i += 4)
            {
                uint x = BigEndian.ToUInt32 (data, i);
                LittleEndian.Pack (x, data, i);
            }
            return data;
        }

        byte[] ConvertArgb16 (byte[] data)
        {
            var output = new byte[data.Length * 2];
            int dst = 0;
            for (int i = 0; i < data.Length; i += 2)
            {
                ushort p = LittleEndian.ToUInt16 (data, i);
                output[dst++] = (byte)( (p        & 0xF) * 0x11);
                output[dst++] = (byte)(((p >>  4) & 0xF) * 0x11);
                output[dst++] = (byte)(((p >>  8) & 0xF) * 0x11);
                output[dst++] = (byte)(((p >> 12) & 0xF) * 0x11);
            }
            return output;
        }

        byte[] ConvertRgba (byte[] data)
        {
            for (int i = 0; i < data.Length; i += 4)
            {
                byte r = data[i];
                data[i] = data[i+2];
                data[i+2] = r;
            }
            return data;
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_reader.Dispose();
                m_disposed = true;
            }
        }
    }
}