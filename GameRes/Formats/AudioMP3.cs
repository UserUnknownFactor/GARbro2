using System.ComponentModel.Composition;
using System.IO;
using NAudio.Wave;
using NAudio.MediaFoundation;

namespace GameRes.Formats
{
    public class Mp3Input : SoundInput
    {
        int             m_bitrate;
        Mp3FileReader   m_reader;

        public override long Position
        {
            get { return m_reader.Position; }
            set { m_reader.Position = value; }
        }

        public override bool CanSeek { get { return m_reader.CanSeek; } }

        public override int SourceBitrate
        {
            get { return m_bitrate; }
        }

        public override string SourceFormat { get { return "mp3"; } }

        public Mp3Input (Stream file) : base (file)
        {
            m_reader = new Mp3FileReader (file);
            m_bitrate = m_reader.Mp3WaveFormat.AverageBytesPerSecond*8;
            var format = new GameRes.WaveFormat();
            format.FormatTag                = (ushort)m_reader.WaveFormat.Encoding;
            format.Channels                 = (ushort)m_reader.WaveFormat.Channels;
            format.SamplesPerSecond         = (uint)m_reader.WaveFormat.SampleRate;
            format.BitsPerSample            = (ushort)m_reader.WaveFormat.BitsPerSample;
            format.BlockAlign               = (ushort)m_reader.BlockAlign;
            format.AverageBytesPerSecond    = (uint)m_reader.WaveFormat.AverageBytesPerSecond;
            this.Format = format;
            this.PcmSize = m_reader.Length;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return m_reader.Read (buffer, offset, count);
        }

        public bool CanCopyDirectly ()
        {
            return Source.CanSeek && Source.CanRead;
        }

        #region IDisposable Members
        bool _mp3_disposed;
        protected override void Dispose (bool disposing)
        {
            if (!_mp3_disposed)
            {
                if (disposing)
                {
                    m_reader.Dispose();
                }
                _mp3_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

    [Export(typeof(AudioFormat))]
    [ExportMetadata("Priority", 40)]
    public class Mp3Audio : AudioFormat
    {
        public override string         Tag { get { return "MP3"; } }
        public override string Description { get { return "MPEG Layer 3 audio format"; } }
        public override uint     Signature { get { return  0; } }
        public override bool      CanWrite { get { return  true; } }

        const int SyncSearchThreshold = 0x300;

        static Mp3Audio()
        {
            MediaFoundationApi.Startup();
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (10).ToArray();
            long start_offset = SkipId3Tag (header);
            int sync_pos = 0;

            if (0 != start_offset)
            {
                file.Position = start_offset;
                if (4 != file.Read (header, 0, 4))
                    return null;
            }
            else if (0xFF != header[0])
            {
                file.Position = 1;
                header = file.ReadBytes (SyncSearchThreshold);
                sync_pos = System.Array.IndexOf<byte> (header, 0xFF, 1, SyncSearchThreshold-4);
                if (-1 == sync_pos)
                    return null;
            }

            if (0xFF != header[sync_pos] || 0xE2 != (header[sync_pos+1] & 0xE6) || 0xF0 == (header[sync_pos+2] & 0xF0))
                return null;

            file.Position = 0;
            return new Mp3Input (file.AsStream);
        }

        public override void Write (SoundInput source, Stream output)
        {
            var mp3Source = source as Mp3Input;
            if (mp3Source != null && mp3Source.CanCopyDirectly())
                CopyMp3Direct (mp3Source, output);
            else
                EncodePcmToMp3 (source, output);
        }

        private void CopyMp3Direct (Mp3Input source, Stream output)
        {
            source.Source.Position = 0;

            byte[] buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = source.Source.Read (buffer, 0, buffer.Length)) > 0)
                output.Write (buffer, 0, bytesRead);
            output.Flush();
        }

        private void EncodePcmToMp3 (SoundInput source, Stream output, int bitrate = 320000)
        {
            string tempFile = Path.GetTempFileName();
            string mp3TempFile = Path.ChangeExtension (tempFile, ".mp3");

            try
            {
                var format = source.Format;
                var waveFormat = new NAudio.Wave.WaveFormat(
                    (int)format.SamplesPerSecond,
                    format.BitsPerSample,
                    format.Channels
                );

                using (var reader = new RawSourceWaveStream (source, waveFormat))
                {
                    MediaFoundationEncoder.EncodeToMp3 (reader, mp3TempFile, bitrate);
                }

                using (var mp3File = File.OpenRead (mp3TempFile))
                {
                    mp3File.CopyTo (output);
                }
                output.Flush();
            }
            finally
            {
                if (File.Exists (tempFile))
                    File.Delete (tempFile);
                if (File.Exists (mp3TempFile))
                    File.Delete (mp3TempFile);
            }
        }

        long SkipId3Tag (byte[] buffer)
        {
            long start_offset = 0;
            if (0x49 == buffer[0] && 0x44 == buffer[1] && 0x33 == buffer[2]) // 'ID3'
            {
                if (buffer[3] < 0x80 && buffer[4] < 0x80 &&
                    buffer[6] < 0x80 && buffer[7] < 0x80 && buffer[8] < 0x80 && buffer[9] < 0x80)
                {
                    int size = buffer[6] << 21 | buffer[7] << 14 | buffer[8] << 7 | buffer[9];
                    if (buffer[3] > 3 && 0 != (buffer[5] & 0x10)) // v2.4 footer present
                        size += 10;
                    start_offset = 10 + size;
                }
            }
            return start_offset;
        }
    }
}