using System;
using System.IO;
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.Unity
{
    internal class VideoClip : NamedObject
    {
        public string m_OriginalPath;
        public uint m_ProxyWidth;
        public uint m_ProxyHeight;
        public uint m_Width;
        public uint m_Height;
        public uint m_PixelAspecRatioNum;
        public uint m_PixelAspecRatioDen;
        public double m_FrameRate;
        public long m_FrameCount;
        public int m_Format;
        public ushort[] m_AudioChannelCount;
        public uint[] m_AudioSampleRate;
        public string[] m_AudioLanguage;
        public StreamedResource m_ExternalResources;
        public bool m_HasSplitAlpha;
        public bool m_sRGB;

        public void Load(AssetReader reader, UnityTypeData type)
        {
            base.Load(reader);
            var version = type.Version[0];

            m_OriginalPath = reader.ReadString();
            reader.Align();
            m_ProxyWidth = reader.ReadUInt32();
            m_ProxyHeight = reader.ReadUInt32();
            m_Width = reader.ReadUInt32();
            m_Height = reader.ReadUInt32();

            if (version >= 2017)
            {
                m_PixelAspecRatioNum = reader.ReadUInt32();
                m_PixelAspecRatioDen = reader.ReadUInt32();
            }

            m_FrameRate = reader.ReadDouble();
            m_FrameCount = reader.ReadUInt64();
            m_Format = reader.ReadInt32();

            // Audio channels
            int channelCount = reader.ReadInt32();
            m_AudioChannelCount = new ushort[channelCount];
            for (int i = 0; i < channelCount; i++)
                m_AudioChannelCount[i] = reader.ReadUInt16();
            reader.Align();

            // Audio sample rates
            int sampleCount = reader.ReadInt32();
            m_AudioSampleRate = new uint[sampleCount];
            for (int i = 0; i < sampleCount; i++)
                m_AudioSampleRate[i] = reader.ReadUInt32();

            // Audio languages
            int langCount = reader.ReadInt32();
            m_AudioLanguage = new string[langCount];
            for (int i = 0; i < langCount; i++)
            {
                m_AudioLanguage[i] = reader.ReadString();
                reader.Align();
            }

            if (version >= 2020)
            {
                // Skip video shaders (PPtr array)
                int shaderCount = reader.ReadInt32();
                for (int i = 0; i < shaderCount; i++)
                {
                    reader.ReadInt32(); // m_FileID
                    reader.ReadInt64(); // m_PathID
                }
            }

            // External resources
            m_ExternalResources = new StreamedResource();
            m_ExternalResources.Load(reader);

            m_HasSplitAlpha = reader.ReadBool();

            if (version >= 2020)
            {
                m_sRGB = reader.ReadBool();
                reader.Align();
            }
        }

        public VideoEntry CreateEntry()
        {
            var entry = new VideoEntry
            {
                Name = Path.GetFileName(m_OriginalPath),
                Type = "video",
                Offset = m_ExternalResources.m_Offset,
                Size = (uint)m_ExternalResources.m_Size,
            };
            return entry;
        }

        public VideoMetaData GetMetaData()
        {
            string ext = Path.GetExtension(m_OriginalPath).TrimStart('.');
            if (string.IsNullOrEmpty(ext))
                ext = "mp4";

            return new VideoMetaData
            {
                Width = m_Width,
                Height = m_Height,
                Duration = (long)((double)m_FrameCount / m_FrameRate * 1000.0),
                FrameRate = m_FrameRate,
                HasAudio = m_AudioChannelCount != null && m_AudioChannelCount.Length > 0,
                FileName = Path.GetFileName(m_OriginalPath),
                CommonExtension = ext,
                Codec = GetCodecName()
            };
        }

        private string GetCodecName()
        {
            switch (m_Format)
            {
                case 0: return "H.264";
                case 1: return "H.265";
                case 2: return "VP8";
                case 3: return "VP9";
                default: return "Unknown";
            }
        }

        public byte[] GetVideoData(AssetReader reader, ArcFile arc = null)
        {
            if (m_ExternalResources == null || m_ExternalResources.m_Size == 0)
                return null;

            if (!string.IsNullOrEmpty(m_ExternalResources.m_Source))
            {
                var videoData = UnityAssetHelper.LoadResourceData (
                    arc, m_ExternalResources.m_Source, 
                    m_ExternalResources.m_Offset, m_ExternalResources.m_Size
                );
                return videoData;
            }
            else
            {
                // Inline data
                reader.Position = m_ExternalResources.m_Offset;
                return reader.ReadBytes((int)m_ExternalResources.m_Size);
            }
        }
    }

    internal class StreamedResource
    {
        internal string m_Source;
        internal long m_Offset;
        internal long m_Size;

        public void Load(AssetReader reader)
        {
            m_Source = reader.ReadString();
            reader.Align();
            m_Offset = reader.ReadInt64();
            m_Size = reader.ReadInt64();
        }
    }

    internal class VideoEntry : AssetEntry
    {
        public override string Type { get { return "video"; } set { } }
        public VideoMetaData MetaData { get; set; }
    }

    public class VideoClipDecoder
    {
        private VideoClip m_clip;
        private AssetReader m_reader;
        private ArcFile m_arc;

        internal VideoClipDecoder(VideoClip clip, AssetReader reader, ArcFile arc = null)
        {
            m_clip = clip;
            m_reader = reader;
            m_arc = arc;
        }

        public VideoData GetVideo()
        {
            var data = m_clip.GetVideoData(m_reader, m_arc);
            if (data == null || data.Length == 0)
                return null;

            var meta = m_clip.GetMetaData();

            // Detect actual format from data signature
            if (data.Length > 12)
            {
                // Check for MP4/M4V signature
                if (data[4] == 'f' && data[5] == 't' && data[6] == 'y' && data[7] == 'p')
                {
                    meta.CommonExtension = "mp4";
                    meta.Codec = "H.264/MP4";
                }
                // Check for WebM signature
                else if (data[0] == 0x1a && data[1] == 0x45 && data[2] == 0xDF && data[3] == 0xA3)
                {
                    meta.CommonExtension = "webm";
                    meta.Codec = "VP8/WebM";
                }
                // Check for AVI signature
                else if (data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
                         data[8] == 'A' && data[9] == 'V' && data[10] == 'I')
                {
                    meta.CommonExtension = "avi";
                    meta.Codec = "AVI";
                }
                // Check for MOV signature
                else if ((data[4] == 'm' && data[5] == 'o' && data[6] == 'o' && data[7] == 'v') ||
                         (data[4] == 'w' && data[5] == 'i' && data[6] == 'd' && data[7] == 'e'))
                {
                    meta.CommonExtension = "mov";
                    meta.Codec = "QuickTime/MOV";
                }
            }

            var stream = new MemoryStream(data);
            return new VideoData(stream, meta, true);
        }

        internal VideoEntry CreateEntry()
        {
            var entry = m_clip.CreateEntry();
            entry.MetaData = m_clip.GetMetaData();

            // Set bundle if external resource
            if (!string.IsNullOrEmpty(m_clip.m_ExternalResources.m_Source))
            {
                entry.Bundle = new BundleEntry { Name = m_clip.m_ExternalResources.m_Source };
            }

            return entry;
        }
    }
}