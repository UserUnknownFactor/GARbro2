namespace GameRes.Formats.Unity
{
    enum AudioFormat : int
    {
        Unknown = 0,
        Acc = 1,
        Aiff = 2,
        It = 10,
        Mod = 12,
        Mpeg = 13,
        OggVorbis = 14,
        S3M = 17,
        Wav = 20,
        Xm = 21,
        Xma = 22,
        Vag = 23,
        AudioQueue = 24,
    }

    internal class AudioClip: NamedObject
    {
        public int      m_LoadType;
        public int      m_Channels;
        public int      m_Frequency;
        public int      m_BitsPerSample;
        public float    m_Length;
        public bool     m_IsTrackerFormat;
        public int      m_SubsoundIndex;
        public bool     m_PreloadAudioData;
        public bool     m_LoadInBackground;
        public bool     m_Legacy3D;
        public string   m_Source;
        public long     m_Offset;
        public long     m_Size;
        public int      m_CompressionFormat;

        public new void Load (AssetReader reader)
        {
            base.Load(reader);

            if (reader.Format > 9)
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
                m_CompressionFormat = reader.ReadInt32();
            }
            else
            {
                m_LoadType = reader.ReadInt32();
                m_CompressionFormat = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt32();
                m_Size = reader.ReadUInt32();
            }
        }
    }
}
