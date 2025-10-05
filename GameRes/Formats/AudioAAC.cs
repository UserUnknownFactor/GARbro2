using System;
using System.ComponentModel.Composition;
using System.IO;
using NAudio.Wave;
using NAudio.MediaFoundation;

namespace GameRes.Formats
{
    public class AacInput : SoundInput
    {
        MediaFoundationReader m_reader;
        int m_bitrate;

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

        public override string SourceFormat { get { return "aac"; } }

        public AacInput (Stream file) : base (file)
        {
            // Initialize Media Foundation
            MediaFoundationApi.Startup();
            
            m_reader = new StreamMediaFoundationReader (file);
            m_bitrate = m_reader.WaveFormat.AverageBytesPerSecond * 8;
            
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

        public bool CanCopyDirectly()
        {
            return Source.CanSeek && Source.CanRead;
        }

        #region IDisposable Members
        bool _aac_disposed;
        protected override void Dispose (bool disposing)
        {
            if (!_aac_disposed)
            {
                if (disposing)
                {
                    m_reader?.Dispose();
                }
                _aac_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

    [Export(typeof(AudioFormat))]
    [ExportMetadata("Priority", 50)]
    public class AacAudio : AudioFormat
    {
        public override string         Tag { get { return "AAC"; } }
        public override string Description { get { return "Advanced Audio Coding"; } }
        public override uint     Signature { get { return  0; } }
        public override bool      CanWrite { get { return  true; } }

        static AacAudio()
        {
            MediaFoundationApi.Startup();
        }

        public AacAudio()
        {
            // AAC files can have different signatures
            // ADIF: 'ADIF' (0x46494441)
            // ADTS: 0xFFF* (sync word)
            // MP4/M4A container: various signatures
            Signatures = new uint[] { 0, 0x46494441 };
            Extensions = new string[] { "aac", "m4a" };
        }

        public static AudioFormat Instance { get { return s_AacFormat.Value; } }

        static readonly ResourceInstance<AudioFormat> s_AacFormat = new ResourceInstance<AudioFormat>("AAC");

        public override SoundInput TryOpen (IBinaryStream file)
        {
            uint signature = file.Signature;
            if (signature == 0x46494441) // 'ADIF'
            {
                file.Position = 0;
                return new AacInput (file.AsStream);
            }

            // Check for ADTS sync word (0xFFF*)
            var header = file.ReadHeader (4).ToArray();
            if ((header[0] == 0xFF) && ((header[1] & 0xF0) == 0xF0))
            {
                file.Position = 0;
                return new AacInput (file.AsStream);
            }

            // Check for MP4/M4A container
            if (header[0] == 0 && header[1] == 0 && header[2] == 0)
            {
                file.Position = 4;
                uint atom_type = file.ReadUInt32();
                file.Position = 0;
                
                // Common MP4/M4A atoms
                if (atom_type == 0x70797466 || // 'ftyp'
                    atom_type == 0x6D646174 || // 'mdat'
                    atom_type == 0x6D6F6F76 || // 'moov'
                    atom_type == 0x66726565)   // 'free'
                {
                    try
                    {
                        return new AacInput (file.AsStream);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        public override void Write (SoundInput source, Stream output)
        {
            var aacSource = source as AacInput;
            if (aacSource != null && aacSource.CanCopyDirectly())
                CopyAacDirect(aacSource, output);
            else
                EncodePcmToAac(source, output);
        }

        private void CopyAacDirect(AacInput source, Stream output)
        {
            source.Source.Position = 0;
            
            byte[] buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = source.Source.Read(buffer, 0, buffer.Length)) > 0)
                output.Write(buffer, 0, bytesRead);
            output.Flush();
        }

        private void EncodePcmToAac(SoundInput source, Stream output, int bitrate = 128000)
        {
            string tempFile = Path.GetTempFileName();
            string aacTempFile = Path.ChangeExtension(tempFile, ".aac");

            source.Position = 0;

            try
            {
                var format = source.Format;
                var waveFormat = new NAudio.Wave.WaveFormat(
                    (int)format.SamplesPerSecond,
                    format.BitsPerSample,
                    format.Channels
                );

                var mediaType = MediaFoundationEncoder.SelectMediaType(
                    AudioSubtypes.MFAudioFormat_AAC,
                    waveFormat,
                    bitrate);

                if (mediaType == null)
                {
                    throw new InvalidOperationException(
                        "AAC encoding is not supported on this system. " +
                        "Please install Windows Media Foundation.");
                }

                using (var reader = new RawSourceWaveStream(source, waveFormat))
                {
                    MediaFoundationEncoder.EncodeToAac(reader, aacTempFile, bitrate);
                }

                using (var aacFile = File.OpenRead(aacTempFile))
                {
                    aacFile.CopyTo(output);
                }
                output.Flush();
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                if (File.Exists(aacTempFile)) File.Delete(aacTempFile);
            }
        }
    }
}