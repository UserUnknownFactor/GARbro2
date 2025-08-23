using NAudio.Wave;

namespace GARbro
{
    public class WaveStreamImpl : WaveStream
    {
        GameRes.SoundInput  m_input;
        WaveFormat          m_format;
        long                m_logicalPosition;

        private int         silenceBytes;

        public override WaveFormat WaveFormat { get { return m_format; } }

        public override long Position
        {
            get { return m_logicalPosition; }
            set { 
                if (value < 0 || value > Length)
                    return;

                if (value <= m_input.Length)
                {
                    m_input.Position = value;
                    m_logicalPosition = value;
                }
                else
                {
                    // Position is within the silence padding
                    m_input.Position = m_input.Length;
                    m_logicalPosition = value;
                }
            }
        }

        public override long Length 
        { 
            get { return m_input.Length + silenceBytes; } 
        }

        public WaveStreamImpl (GameRes.SoundInput input, int silenceDurationMs = 300)
        {
            m_input = input;
            var format = m_input.Format;
            m_format = WaveFormat.CreateCustomFormat (
                (WaveFormatEncoding)format.FormatTag,
                (int)format.SamplesPerSecond,
                format.Channels,
                (int)format.AverageBytesPerSecond,
                format.BlockAlign,
                format.BitsPerSample
            );
            silenceBytes = (int)(format.AverageBytesPerSecond * silenceDurationMs / 1000.0);
            m_logicalPosition = 0;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int totalBytesRead = 0;

            if (m_logicalPosition < m_input.Length)
            {
                long remainingAudio = m_input.Length - m_logicalPosition;
                int audioToRead = (int)System.Math.Min(count, remainingAudio);

                int audioRead = m_input.Read(buffer, offset, audioToRead);
                totalBytesRead += audioRead;
                m_logicalPosition += audioRead;
                offset += audioRead;
                count -= audioRead;
            }

            if (count > 0 && m_logicalPosition < Length)
            {
                long remainingSilence = Length - m_logicalPosition;
                int silenceToAdd = (int)System.Math.Min(count, remainingSilence);

                System.Array.Clear(buffer, offset, silenceToAdd);
                totalBytesRead += silenceToAdd;
                m_logicalPosition += silenceToAdd;
            }

            return totalBytesRead;
        }

        public override int ReadByte ()
        {
            if (m_logicalPosition >= Length)
                return -1; // EOF

            if (m_logicalPosition < m_input.Length)
            {
                int result = m_input.ReadByte();
                if (result != -1)
                    m_logicalPosition++;
                return result;
            }
            else
            {
                m_logicalPosition++;
                return 0;
            }
        }

        #region IDisposable Members
        bool disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                    m_input.Dispose();
                disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }
}