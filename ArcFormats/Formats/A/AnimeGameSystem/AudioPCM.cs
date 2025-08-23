using System;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Ags
{
    [Export(typeof(AudioFormat))]
    public class PcmAudio : AudioFormat
    {
        public override string         Tag { get { return "PCM/AGS"; } }
        public override string Description { get { return "AnimeGameSystem PCM audio"; } }
        public override uint     Signature { get { return 0; } }

        public PcmAudio ()
        {
            Extensions = new string[] { "pcm" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            uint signature = file.Signature & 0xF0FFFFFF;
            if (0x564157 != signature) // 'WAV'
                return null;
            return new PcmInput (file.AsStream);
        }
    }

    public class PcmInput : SoundInput
    {
        public override string SourceFormat { get { return "raw"; } }

        public override int SourceBitrate
        {
            get { return (int)Format.AverageBytesPerSecond * 8; }
        }

        public PcmInput (Stream file) : base (null)
        {
            file.Position = 3;
            int type = file.ReadByte();
            int data_length = (int)file.Length - 4;
            var format = new WaveFormat();
            format.FormatTag                = 1;
            format.Channels                 = (ushort)((type & 1) + 1);
            type &= ~1;
            if (0xA == type)
            {
                format.SamplesPerSecond     = 44100;
                format.BitsPerSample        = 16;
            }
            else if (6 == type)
            {
                format.SamplesPerSecond     = 22050;
                format.BitsPerSample        = 16;
            }
            else if (4 == type)
            {
                format.SamplesPerSecond     = 22050;
                format.BitsPerSample        = 8;
            }
            else
                throw new NotSupportedException ("Not supported PCM format");
            format.BlockAlign               = (ushort)(format.Channels*format.BitsPerSample/8);
            format.AverageBytesPerSecond    = format.SamplesPerSecond*format.BlockAlign;
            this.Format = format;
            this.PcmSize = data_length;
            this.Source = new StreamRegion (file, 4, data_length);
        }

        #region IO.Stream methods
        public override long Position
        {
            get { return Source.Position; }
            set { Source.Position = value; }
        }

        public override bool CanSeek { get { return Source.CanSeek; } }

        public override long Seek (long offset, SeekOrigin origin)
        {
            return Source.Seek (offset, origin);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return Source.Read (buffer, offset, count);
        }

        public override int ReadByte ()
        {
            return Source.ReadByte();
        }
        #endregion
    }
}
