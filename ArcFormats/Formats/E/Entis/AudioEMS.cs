using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Entis
{
    internal class EmsacSoundInfo
    {
        public int      Version;            // field_00
        public CvType   Transformation;     // field_04
        public EriCode  Architecture;       // field_08
        public int      ChannelCount;       // field_0C
        public uint     SamplesPerSecond;   // field_10
        public uint     BlocksetCount;      // field_14
        public int      SubbandDegree;      // field_18
        public int      TotalSampleCount;   // field_1C
        public int      LappedDegree;       // field_20
    }

    [Export(typeof(AudioFormat))]
    public sealed class EmsAudio : AudioFormat
    {
        public override string         Tag { get { return "EMS"; } }
        public override string Description { get { return "EMSAC compressed audio format"; } }
        public override uint     Signature { get { return  0x69746E45; } } // 'Entis'

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x40);
            if (0x02000200 != header.ToUInt32 (8))
                return null;
            if (!header.AsciiEqual (0x10, "EMSAC-Sound-2"))
                return null;

            var decoder = new EmsacDecoder (file);
            var pcm = decoder.Decode();
            var sound = new RawPcmInput (pcm, decoder.Format);
            file.Dispose();

            return sound;
        }
    }

    internal class EmsacDecoder
    {
        Stream          m_input;
        EmsacSoundInfo  m_info;
        long            m_streamPosition;
        WaveFormat      m_format;
        EmsacExpansion  m_expansion;
        int             m_lappedSubbandSize;
        int             m_version;
        EriCode         m_architecture;

        public WaveFormat Format { get { return m_format; } }

        const ushort BitsPerSample = 16;

        public EmsacDecoder (IBinaryStream input)
        {
            m_input = input.AsStream;
        }

        public Stream Decode ()
        {
            m_input.Position = 0x40;
            using (var erif = new EriFile (m_input))
            {
                ReadHeader (erif);
                int totalBytes = m_info.TotalSampleCount * BitsPerSample / 8;
                var output = new MemoryStream (totalBytes);
                try
                {
                    var inputBuffer = new byte[0x10000];
                    var decodedBuffer = new byte[0x10000];
                    erif.BaseStream.Position = m_streamPosition;

                    while (totalBytes > 0)
                    {
                        var section = erif.ReadSection();
                        if (section.Id != "SoundStm")
                            break;

                        int streamLength = (int)section.Length;
                        if (streamLength > inputBuffer.Length)
                            inputBuffer = new byte[streamLength];

                        erif.Read (inputBuffer, 0, streamLength);
                        int decodedLength = inputBuffer.ToInt32 (4) * m_lappedSubbandSize;

                        if (decodedLength > decodedBuffer.Length)
                            decodedBuffer = new byte[decodedLength];

                        DecodeBlock (inputBuffer, decodedBuffer);
                        int bytesToWrite = Math.Min (decodedLength, totalBytes);
                        output.Write (decodedBuffer, 0, bytesToWrite);
                        totalBytes -= bytesToWrite;
                    }
                }
                catch (EndOfStreamException) { /* ignore EOF errors */ }
                output.Position = 0;
                return output;
            }
        }

        void DecodeBlock (byte[] input, byte[] output)
        {
            if (m_expansion.Version > 0x20100)
                throw new InvalidFormatException ("Not supported EMSAC version.");

            int channelCount = m_expansion.ChannelCount;
            if (0 == channelCount || channelCount > 2)
                throw new InvalidFormatException ("Invalid number of channels.");

            int subbandDegree = 0;
            int subbandMultiplier = 1;
            bool useDct = 0 != (m_expansion.Transformation & CvType.DCT_ERI);

            if (useDct)
            {
                subbandDegree = m_expansion.SubbandDegree;
                subbandMultiplier <<= subbandDegree;
            }

            int subbandSize = 1 << subbandDegree;
            int sampleCount = input.ToInt32 (4);
            int samplesPerChannel = sampleCount / channelCount;

            if (0 == samplesPerChannel)
                throw new InvalidFormatException ("Invalid number of samples.");

            var channelBuffers = new int[channelCount][];
            var workBuffer     = new int[subbandSize];
            var weightTable    = new int[subbandSize];
            var sampleBuffer   = new short[sampleCount];

            for (int i = 0; i < channelCount; ++i)
            {
                channelBuffers[i] = new int[subbandSize];
            }

            int outputPos = 0;
            int inputPos = 8;
            int symbolTableOffset = inputPos + sampleCount;

            // Create decoder symbol table
            var decoder = CreateDecoderContext (input, symbolTableOffset);

            for (int block = 0; block < samplesPerChannel; ++block)
            {
                // Decode each channel
                for (int channel = 0; channel < channelCount; ++channel)
                {
                    var channelData = channelBuffers[channel];
                    int symbolsDecoded = DecodeSymbols (decoder, workBuffer, subbandSize);

                    if (symbolsDecoded < subbandSize)
                        throw new InvalidFormatException();

                    byte quantizationCode = input[inputPos++];
                    InverseQuantize (weightTable, workBuffer, subbandSize, quantizationCode);

                    if (useDct)
                        InverseDCT (channelData, weightTable, 2, subbandDegree);
                    else
                        Buffer.BlockCopy (weightTable, 0, channelData, 0, 4 * subbandSize);
                }

                // Convert to samples
                int currentPos = outputPos;
                for (int channel = 0; channel < channelCount; ++channel)
                {
                    int pos = currentPos;
                    var channelData = channelBuffers[channel];

                    for (int i = 0; i < subbandSize; ++i)
                    {
                        int floatValue = channelData[i];
                        int sign = floatValue >> 31;
                        int exponent = (floatValue >> 23) & 0xFF;
                        int mantissa = floatValue & 0x7FFFFF | 0x800000;
                        int shift = 150 - exponent;
                        int sample;

                        if (shift > 8)
                        {
                            if (shift <= 31)
                                sample = sign ^ (sign + (mantissa >> shift));
                            else
                                sample = 0;
                        }
                        else
                        {
                            sample = sign ^ (sign + 0x7FFF);
                        }

                        sampleBuffer[pos] = (short)sample;
                        pos += channelCount;
                    }
                    ++currentPos;
                }
                outputPos += channelCount * subbandSize;
            }

            // Apply differential decoding
            ApplyDifferentialDecoding (sampleBuffer, samplesPerChannel, channelCount, subbandSize);

            // Copy to output
            Buffer.BlockCopy (sampleBuffer, 0, output, 0, sampleCount * 2);
        }

        void ApplyDifferentialDecoding (short[] samples, int samplesPerChannel, int channelCount, int subbandSize)
        {
            int step = channelCount * subbandSize;

            for (int channel = 0; channel < channelCount; ++channel)
            {
                int pos = channel;
                short difference = (short)(m_expansion.CompressionState[channel] - samples[pos]);

                // Apply initial difference
                for (int i = 0; i < 12 && i < samplesPerChannel; ++i)
                {
                    int sample = difference + samples[pos];
                    samples[pos] = Clamp (sample);
                    pos += channelCount;
                    difference >>= 1;
                }

                // Process remaining samples
                pos = channel + step;
                for (int i = 1; i < samplesPerChannel && pos < samples.Length; ++i)
                {
                    if (pos - step >= 0 && pos + step / 2 < samples.Length)
                    {
                        int prediction = samples[pos + step / 2] + samples[pos - step / 2] 
                                       - samples[pos - step] - samples[pos];
                        int average1 = (short)(prediction + samples[pos - step / 2] + samples[pos]) >> 1;
                        int average2 = (short)(samples[pos - step / 2] + samples[pos] - prediction) >> 1;

                        // Update previous samples
                        short diff1 = (short)(average2 - samples[pos - step / 2]);
                        int prevPos = pos - step;
                        for (int j = 0; j < 12 && prevPos >= 0; ++j)
                        {
                            int updatedSample = diff1 + samples[prevPos];
                            samples[prevPos] = Clamp (updatedSample);
                            prevPos -= step;
                            diff1 >>= 1;
                        }

                        // Update future samples
                        short diff2 = (short)(average1 - samples[pos]);
                        int nextPos = pos;
                        for (int j = 0; j < 12 && nextPos < samples.Length; ++j)
                        {
                            int updatedSample = diff2 + samples[nextPos];
                            samples[nextPos] = Clamp (updatedSample);
                            nextPos += step;
                            diff2 >>= 1;
                        }
                    }
                    pos += step;
                }

                // Update compression state
                int lastPos = channel + (samplesPerChannel - 1) * step;
                if (lastPos - step >= 0 && lastPos - 2 * step >= 0)
                {
                    int finalValue = 2 * samples[lastPos - step] - samples[lastPos - 2 * step];
                    m_expansion.CompressionState[channel] = Clamp (finalValue);
                }
            }
        }

        static short Clamp (int sample)
        {
            if (sample > 0x7FFF)
                return 0x7FFF;
            else if (sample < -32768)
                return -32768;
            return (short)sample;
        }

        void ReadHeader (EriFile erif)
        {
            var section = erif.ReadSection();
            if (section.Id != "Header  " || section.Length <= 0 || section.Length > int.MaxValue)
                throw new InvalidFormatException ("Invalid header section");

            m_streamPosition = erif.BaseStream.Position + section.Length;
            section = erif.ReadSection();
            if (section.Id != "FileHdr" || section.Length < 8)
                throw new InvalidFormatException ("Invalid file header");

            var fileHeader = new byte[section.Length];
            erif.Read (fileHeader, 0, fileHeader.Length);
            if (0 == (fileHeader[5] & 1))
                throw new InvalidFormatException ("Invalid file header flags");

            section = erif.ReadSection();
            if (section.Id != "SoundInf" || section.Length < 0x24)
                throw new InvalidFormatException ("Invalid sound info section");

            var info = new EmsacSoundInfo();
            info.Version            = erif.ReadInt32();
            info.Transformation     = (CvType)erif.ReadInt32();
            info.Architecture       = (EriCode)erif.ReadInt32();
            info.ChannelCount       = erif.ReadInt32();
            info.SamplesPerSecond   = erif.ReadUInt32();
            info.BlocksetCount      = erif.ReadUInt32();
            info.SubbandDegree      = erif.ReadInt32();
            info.TotalSampleCount   = erif.ReadInt32();
            info.LappedDegree       = erif.ReadInt32();

            SetSoundInfo (info);
            SetWaveFormat (info);

            erif.BaseStream.Position = m_streamPosition;
            var streamSize = erif.FindSection ("Stream  ");
            m_streamPosition = erif.BaseStream.Position;
        }

        void SetSoundInfo (EmsacSoundInfo info)
        {
            m_info = info;
            m_expansion = new EmsacExpansion (info);
            m_version = info.Version;
            m_architecture = info.Architecture;

            int subbandSize = 2 << info.SubbandDegree;
            m_lappedSubbandSize = subbandSize << info.LappedDegree;
        }

        void SetWaveFormat (EmsacSoundInfo info)
        {
            int pcmBitrate = (int)(info.SamplesPerSecond * BitsPerSample * info.ChannelCount);
            m_format = new WaveFormat();

            m_format.FormatTag              = 1;
            m_format.Channels               = (ushort)info.ChannelCount;
            m_format.SamplesPerSecond       = info.SamplesPerSecond;
            m_format.BitsPerSample          = BitsPerSample;
            m_format.BlockAlign             = (ushort)(BitsPerSample / 8 * m_format.Channels);
            m_format.AverageBytesPerSecond  = (uint)(pcmBitrate / 8);
        }

        object CreateDecoderContext (byte[] input, int offset)
        {
            throw new NotImplementedException();
        }

        int DecodeSymbols (object decoder, int[] output, int count)
        {
            throw new NotImplementedException();
        }

        void InverseQuantize (int[] output, int[] input, int count, byte quantizationCode)
        {
            throw new NotImplementedException();
        }

        void InverseDCT (int[] output, int[] input, int stride, int degree)
        {
            throw new NotImplementedException();
        }
    }

    internal class EmsacExpansion
    {
        public int      Version;            // field_0
        public CvType   Transformation;     // field_4
        public int      ChannelCount;       // field_8
        public int      SubbandDegree;      // field_C
        public int      LappedDegree;       // field_10
        public short[]  CompressionState = new short[16];

        public EmsacExpansion (EmsacSoundInfo info)
        {
            Version         = info.Version;
            Transformation  = info.Transformation;
            ChannelCount    = info.ChannelCount;
            SubbandDegree   = info.SubbandDegree;
            LappedDegree    = info.LappedDegree;
        }
    }
}
