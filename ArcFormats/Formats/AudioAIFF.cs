using System.ComponentModel.Composition;
using System.IO;
using NAudio.Wave;

namespace GameRes.Formats
{
    [Export(typeof(AudioFormat))]
    public class AiffAudio : AudioFormat
    {
        public override string         Tag { get { return "AIFF"; } }
        public override string Description { get { return "Audio Interchange File Format"; } }
        public override uint     Signature { get { return 0x4D524F46; } } // 'FORM'
        public override bool      CanWrite { get { return false; } }

        public AiffAudio ()
        {
            Extensions = new string[] { "aif", "aiff" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            return new AiffInput (file.AsStream);
        }
    }

    public class AiffInput : SoundInput
    {
        int                     m_bitrate = 0;
        AiffFileReader          m_reader;

        public override long Position
        {
            get { return m_reader.Position; }
            set { m_reader.Position = value; }
        }

        public override bool CanSeek { get { return true; } }

        public override int SourceBitrate
        {
            get { return m_bitrate; }
        }

        public override string SourceFormat { get { return "aiff"; } }

        public AiffInput (Stream file) : base (file)
        {
            m_reader = new AiffFileReader (file);
            var format = new GameRes.WaveFormat {
                FormatTag                = (ushort)m_reader.WaveFormat.Encoding,
                Channels                 = (ushort)m_reader.WaveFormat.Channels,
                SamplesPerSecond         = (uint)m_reader.WaveFormat.SampleRate,
                BitsPerSample            = (ushort)m_reader.WaveFormat.BitsPerSample,
                BlockAlign               = (ushort)m_reader.BlockAlign,
                AverageBytesPerSecond    = (uint)m_reader.WaveFormat.AverageBytesPerSecond,
            };
            this.Format = format;
            this.PcmSize = m_reader.Length;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return m_reader.Read (buffer, offset, count);
        }

        #region IDisposable Members
        bool m_disposed;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    m_reader.Dispose();
                }
                m_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }
}
