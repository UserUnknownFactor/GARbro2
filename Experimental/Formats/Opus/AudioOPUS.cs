using System;
using System.ComponentModel.Composition;
using System.IO;
using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GameRes.Formats.Opus
{
    public class OpusOptions : ResourceOptions
    {
        public int Bitrate { get; set; }
        public OpusApplication Application { get; set; }

        public OpusOptions()
        {
            Bitrate = 320000;
            Application = OpusApplication.OPUS_APPLICATION_AUDIO;
        }
    }

    [Export (typeof (AudioFormat))]
    public class OpusAudio : AudioFormat
    {
        public override string         Tag { get { return "OPUS"; } }
        public override string Description { get { return "Ogg/Opus audio format"; } }
        public override uint     Signature { get { return  0; } }
        public override bool      CanWrite { get { return  true; } }

        private const int OPUS_SAMPLE_RATE = 48000;

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (file.Signature != 0x5367674F) // 'OggS'
                return null;
            var header = file.ReadHeader (0x1C);
            int table_size = header[0x1A];
            if (table_size < 1)
                return null;
            int header_size = header[0x1B];
            if (header_size < 0x10)
                return null;
            int header_pos = 0x1B + table_size;
            header = file.ReadHeader (header_pos + header_size);
            if (!header.AsciiEqual (header_pos, "OpusHead"))
                return null;

            int channels = header[header_pos + 9];
            int rate     = header[header_pos + 12] | 
                          (header[header_pos + 13] << 8) | 
                          (header[header_pos + 14] << 16) | 
                          (header[header_pos + 15] << 24);

            file.Position = 0;
            var decoder = OpusDecoder.Create (OPUS_SAMPLE_RATE, channels);
            var ogg_in = new OpusOggReadStream (decoder, file.AsStream);
            var pcm = new MemoryStream();
            try
            {
                using (var output = new BinaryWriter (pcm, System.Text.Encoding.UTF8, true))
                {
                    while (ogg_in.HasNextPacket)
                    {
                        var packet = ogg_in.DecodeNextPacket();
                        if (packet != null)
                        {
                            for (int i = 0; i < packet.Length; ++i)
                                output.Write (packet[i]);
                        }
                    }
                }
                var format = new WaveFormat
                {
                    FormatTag = 1,
                    Channels = (ushort)channels,
                    SamplesPerSecond = (uint)rate,
                    BitsPerSample = 16,
                };
                format.BlockAlign = (ushort)(format.Channels * format.BitsPerSample / 8);
                format.AverageBytesPerSecond = format.SamplesPerSecond * format.BlockAlign;
                pcm.Position = 0;
                var sound = new RawPcmInput (pcm, format);
                file.Dispose();
                return sound;
            }
            catch
            {
                pcm.Dispose();
                throw;
            }
        }

        public override void Write (SoundInput source, Stream output)
        {
            if (source == null)
                throw new ArgumentNullException (nameof(source));
            if (output == null)
                throw new ArgumentNullException (nameof(output));

            var options = new OpusOptions();

            var pcmSource = source as RawPcmInput;
            if (pcmSource != null)
            {
                WriteOpusStream (pcmSource, output, options);
            }
            else
            {
                // Need to convert to PCM first
                using (var pcmStream = new MemoryStream())
                {
                    source.Position = 0;
                    var buffer = new byte[4096];
                    int read;
                    while ((read = source.Read (buffer, 0, buffer.Length)) > 0)
                    {
                        pcmStream.Write (buffer, 0, read);
                    }

                    pcmStream.Position = 0;
                    using (var tempPcm = new RawPcmInput (pcmStream, source.Format))
                    {
                        WriteOpusStream (tempPcm, output, options);
                    }
                }
            }
        }

        private void WriteOpusStream (RawPcmInput pcmSource, Stream output, OpusOptions options)
        {
            var format = pcmSource.Format;
            

            var encoder = OpusEncoder.Create (OPUS_SAMPLE_RATE, format.Channels, options.Application);
            encoder.Bitrate = Math.Min (options.Bitrate, 510000 * format.Channels);

            var tags = new OpusTags();
            tags.Comment = "Encoded by GARbro";

            var oggOut = new OpusOggWriteStream (encoder, output, tags, (int)format.SamplesPerSecond);

            WriteFromStream (pcmSource, oggOut, format.Channels);
        }

        private Stream ResampleTo48kHz (RawPcmInput pcmSource, WaveFormat sourceFormat)
        {
            var inputFormat = new NAudio.Wave.WaveFormat(
                (int)sourceFormat.SamplesPerSecond,
                sourceFormat.BitsPerSample,
                sourceFormat.Channels);

            pcmSource.Position = 0;
            var waveProvider = new RawSourceWaveStream (pcmSource, inputFormat);
            var sampleProvider = waveProvider.ToSampleProvider();
            var resampler = new WdlResamplingSampleProvider (sampleProvider, OPUS_SAMPLE_RATE);

            var outputProvider = resampler.ToWaveProvider16();
            var outputStream = new MemoryStream();
            var buffer = new byte[4800 * sourceFormat.Channels * 2];

            int bytesRead;
            while ((bytesRead = outputProvider.Read (buffer, 0, buffer.Length)) > 0)
                outputStream.Write (buffer, 0, bytesRead);

            outputStream.Position = 0;
            return outputStream;
        }

        private void WriteFromStream (Stream audioStream, OpusOggWriteStream oggOut, int channels)
        {
            const int frameDurationMs = 20;
            const int sampleRate = OPUS_SAMPLE_RATE;
            int frameSize = sampleRate * frameDurationMs / 1000;
            int bytesPerFrame = frameSize * channels * 2; // 16-bit samples

            var buffer = new byte[bytesPerFrame];
            var samples = new short[frameSize * channels];

            audioStream.Position = 0;

            while (true)
            {
                int bytesRead = audioStream.Read (buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                    break;

                int sampleCount = bytesRead / 2;
                Buffer.BlockCopy (buffer, 0, samples, 0, bytesRead);

                if (sampleCount > 0)
                    oggOut.WriteSamples (samples, 0, sampleCount);
            }

            oggOut.Finish();
        }
    }
}