using System;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Macromedia
{
    [Export(typeof(AudioFormat))]
    public class SndAudio : AudioFormat
    {
        public override string         Tag => "SND";
        public override string Description => "Macromedia Director audio resource";
        public override uint     Signature => 0;
        public override bool      CanWrite => false;

        static readonly ResourceInstance<AudioFormat> Mp3 = new ResourceInstance<AudioFormat> ("MP3");

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".snd"))
                return null;
            int type = file.ReadUInt16();
            if (type != 0x0200)
                return null;
            var reader = new Reader (file.AsStream, ByteOrder.BigEndian);
            reader.Skip (2);
            int count = reader.ReadU16();
            if (0 == count)
                return null;
            int command = reader.ReadU16();
            if (command != 0x8051)
                return null;
            reader.ReadI16();
            int pos = reader.ReadI32();
            if (pos != reader.Position)
                return null;
            ushort channels = 1;
            ushort bps = 8;
            reader.Skip (4);
            int param = reader.ReadI32();
            ushort sample_rate = reader.ReadU16();
            reader.Skip (10);
            byte encoding = reader.ReadU8();
            byte freq = reader.ReadU8();
            if (freq != 0x3C)
                return null;
            int frames_count = 0;
            if (0 == encoding)
            {
                frames_count = param / channels;
            }
            else if (0xFF == encoding)
            {
                channels = (ushort)param;
                frames_count = reader.ReadI32();
                reader.Skip (22);
                bps = reader.ReadU16();
                reader.Skip (14);
            }
            else
                throw new NotSupportedException (string.Format ("Not supported 'snd' encoding {0:X2}", encoding));
            if (bps != 16 && bps != 8)
                return null;

            // try mp3
            var samples_stream = new StreamRegion (reader.Source, reader.Position);
            var mp3_input = new BinaryStream (samples_stream, file.Name);
            var mp3 = Mp3.Value.TryOpen (mp3_input);
            if (mp3 != null)
                return mp3;

            var format = new WaveFormat {
                FormatTag = 1,
                Channels = channels,
                SamplesPerSecond = sample_rate,
                BlockAlign = (ushort)(bps / 8),
                BitsPerSample = bps,
            };
            format.SetBPS();
            if (8 == bps)
            {
                return new RawPcmInput (samples_stream, format);
            }
            int sample_count = frames_count * channels;
            var samples = file.ReadBytes (sample_count);
            for (int i = 1; i < samples.Length; i += 2)
            {
                byte s = samples[i-1];
                samples[i-1] = samples[i];
                samples[i] = s;
            }
            var raw = new BinMemoryStream (samples);
            file.Dispose();
            return new RawPcmInput (raw, format);
        }
    }
}
