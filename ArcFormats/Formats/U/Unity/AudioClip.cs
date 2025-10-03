using System;
using System.IO;
using System.Linq;
using GameRes.Formats.Fmod;
using GameRes.Utility;

namespace GameRes.Formats.Unity
{
    enum AudioCompressionFormat : int
    {
        PCM = 0,
        Vorbis   = 1,
        ADPCM   = 2,
        MP3     = 3,
        VAG     = 4,
        HEVAG   = 5,
        XMA     = 6,
        AAC     = 7,
        GCADPCM = 8,
        ATRAC9  = 9
    }

    internal class AudioClip : NamedObject
    {
        public int m_Format;
        public int m_Type;
        public bool m_3D;
        public bool m_UseHardware;
        public int m_Stream;

        public int m_LoadType;
        public int m_Channels;
        public int m_Frequency;
        public int m_BitsPerSample;
        public float m_Length;
        public bool m_IsTrackerFormat;
        public int m_SubsoundIndex;
        public bool m_PreloadAudioData;
        public bool m_LoadInBackground;
        public bool m_Legacy3D;
        public string m_Source;
        public long m_Offset;
        public long m_Size;
        public AudioCompressionFormat m_CompressionFormat;
        public byte[] m_AudioData;

        public void Load(AssetReader reader, UnityTypeData type)
        {
            base.Load(reader);
            var version = type.Version[0];

            if (version < 5)
            {
                m_Format = reader.ReadInt32();
                m_Type = reader.ReadInt32();
                m_3D = reader.ReadBool();
                m_UseHardware = reader.ReadBool();
                reader.Align();

                if (version >= 4 || (version == 3 && reader.Format >= 2))
                {
                    m_Stream = reader.ReadInt32();
                    m_Size = reader.ReadInt32();
                    var tsize = m_Size % 4 != 0 ? m_Size + 4 - m_Size % 4 : m_Size;
                    if (reader.Source.Length + reader.Origin - reader.Position != tsize)
                    {
                        m_Offset = reader.ReadUInt32();
                        m_Source = reader.Name + ".resS";
                    }
                }
                else
                {
                    m_Size = reader.ReadInt32();
                }

                // Default values for old format
                m_Channels = 2;
                m_Frequency = 44100;
                m_BitsPerSample = 16;
                m_CompressionFormat = AudioCompressionFormat.PCM;
            }
            else
            {
                m_LoadType = reader.ReadInt32();
                m_Channels = reader.ReadInt32();
                m_Frequency = reader.ReadInt32();
                m_BitsPerSample = reader.ReadInt32();
                m_Length = reader.ReadFloat();
                m_IsTrackerFormat = reader.ReadBool();
                reader.Align();
                m_SubsoundIndex = reader.ReadInt32();
                m_PreloadAudioData = reader.ReadBool();
                m_LoadInBackground = reader.ReadBool();
                m_Legacy3D = reader.ReadBool();
                reader.Align();
                m_Source = reader.ReadString();
                reader.Align();
                m_Offset = reader.ReadInt64();
                m_Size = reader.ReadInt64();
                m_CompressionFormat = (AudioCompressionFormat)reader.ReadInt32();
            }

            // Load audio data if inline
            if (string.IsNullOrEmpty(m_Source) && m_Size > 0)
                m_AudioData = reader.ReadBytes((int)m_Size);
        }

        public byte[] ConvertToAudio(byte[] audioData)
        {
            if (audioData[0] != 'R' && m_CompressionFormat == AudioCompressionFormat.PCM)
                return CreateWavFromPCM(audioData);

            return audioData;
        }

        private byte[] CreateWavFromPCM(byte[] pcmData)
        {
            using (var output = new MemoryStream())
            using (var writer = new BinaryWriter(output))
            {
                // Calculate sizes
                int dataSize = pcmData.Length;
                int fileSize = dataSize + 36; // 44 - 8 (RIFF header)

                // RIFF header
                writer.Write(new char[] { 'R', 'I', 'F', 'F' });
                writer.Write(fileSize);
                writer.Write(new char[] { 'W', 'A', 'V', 'E' });

                // fmt chunk
                writer.Write(new char[] { 'f', 'm', 't', ' ' });
                writer.Write(16); // fmt chunk size
                writer.Write((short)1); // PCM format
                writer.Write((short)m_Channels);
                writer.Write(m_Frequency);
                writer.Write(m_Frequency * m_Channels * m_BitsPerSample / 8); // byte rate
                writer.Write((short)(m_Channels * m_BitsPerSample / 8)); // block align
                writer.Write((short)m_BitsPerSample);

                // data chunk
                writer.Write(new char[] { 'd', 'a', 't', 'a' });
                writer.Write(dataSize);
                writer.Write(pcmData);

                return output.ToArray();
            }
        }

        public string GetExtension()
        {
            switch (m_CompressionFormat)
            {
                case AudioCompressionFormat.PCM:
                    return ".wav";
                case AudioCompressionFormat.Vorbis:
                    return ".ogg";
                case AudioCompressionFormat.MP3:
                    return ".mp3";
                case AudioCompressionFormat.AAC:
                    return ".m4a";
                case AudioCompressionFormat.ADPCM:
                    return ".wav"; // Can be in WAV container
                default:
                    return ".audioclip";
            }
        }
    }

    internal class AudioEntry : AssetEntry
    {
        public override string Type { get { return "audio"; } set { } }
        internal AudioClip Clip { get; set; }
    }


    internal class AudioClipDecoder
    {
        AudioClip m_clip;

        internal AudioClipDecoder(AudioClip clip)
        {
            m_clip = clip;
        }

        public AudioEntry CreateEntry()
        {
            var entry = new AudioEntry
            {
                Name = m_clip.m_Name + m_clip.GetExtension(),
                Type = "audio",
                Offset = m_clip.m_Offset,
                Size = (uint)m_clip.m_Size,
                Clip = m_clip
            };

            if (!string.IsNullOrEmpty(m_clip.m_Source))
            {
                entry.Bundle = new BundleEntry { Name = m_clip.m_Source };
            }

            return entry;
        }
    }
}
