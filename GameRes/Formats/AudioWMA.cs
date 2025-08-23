//#define NAUDIO11
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace GameRes.Formats
{
    [Export(typeof(AudioFormat))]
    public class WmaAudio : AudioFormat
    {
        public override string         Tag { get { return "WMA"; } }
        public override string Description { get { return "Windows Media Audio format"; } }
        public override uint     Signature { get { return  0x75B22630; } }
        public override bool      CanWrite { get { return  false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            return new WmaInput (file.AsStream);
        }
    }

    public class WmaInput : SoundInput
    {
        int                     m_bitrate;
        MediaFoundationReader   m_reader;

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

        public override string SourceFormat { get { return "wma"; } }

        public WmaInput (Stream file) : base (file)
        {
            var reader = new CustomMediaFoundationReader (file);
            if (reader.Duration != 0)
                m_bitrate = (int)(file.Length * 80000000L / reader.Duration);
            else
                m_bitrate = reader.WaveFormat.AverageBytesPerSecond * 8;
            m_reader = reader;
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
        bool _wma_disposed;
        protected override void Dispose (bool disposing)
        {
            if (!_wma_disposed)
            {
                if (disposing)
                {
                    m_reader.Dispose();
                }
                _wma_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

    /// <summary>
    /// Custom implementation of StreamMediaFoundationReader.
    /// Extends it with Duration property.
    /// </summary>
    internal class CustomMediaFoundationReader : StreamMediaFoundationReader
    {
        /// <summary>
        /// Specifies the duration of a presentation, in 100-nanosecond units. 
        /// </summary>
        public long Duration { get; private set; }

        public CustomMediaFoundationReader (Stream stream, MediaFoundationReaderSettings settings = null)
            : base (stream, settings)
        {
        }

        protected override IMFSourceReader CreateReader (MediaFoundationReaderSettings settings)
        {
            var source_reader = base.CreateReader (settings);
            Duration = GetDuration (source_reader);
            return source_reader;
        }

        private static long GetDuration (IMFSourceReader reader)
        {
#if NAUDIO11
            var variantPtr = Marshal.AllocHGlobal(MarshalHelpers.SizeOf<PropVariant>());
#else
            var variantPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
#endif
            try
            {
                int hResult = reader.GetPresentationAttribute (MediaFoundationInterop.MF_SOURCE_READER_MEDIASOURCE,
                    MediaFoundationAttributes.MF_PD_DURATION, variantPtr);
                if (hResult == MediaFoundationErrors.MF_E_ATTRIBUTENOTFOUND)
                    return 0;
                if (hResult != 0)
                    Marshal.ThrowExceptionForHR (hResult);
#if NAUDIO11
                var variant = MarshalHelpers.PtrToStructure<PropVariant>(variantPtr);
#else
                var variant = Marshal.PtrToStructure<PropVariant>(variantPtr);
#endif
                return (long)variant.Value;
            }
            finally 
            {
                PropVariant.Clear (variantPtr);
                Marshal.FreeHGlobal (variantPtr);
            }
        }
    }
}
