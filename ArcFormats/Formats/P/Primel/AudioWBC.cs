using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;
using NAudio.Wave;

namespace GameRes.Formats
{
    [Export(typeof(AudioFormat))]
    public class WbcAudio : AudioFormat
    {
        public override string         Tag { get { return "WBC"; } }
        public override string Description { get { return "Primel ADV System audio format"; } }
        public override uint     Signature { get { return  0x46434257; } } // 'WBCF'
        public override bool      CanWrite { get { return  false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var decoder = new WbcDecoder (file);
            var data = decoder.Decode();
            var pcm = new MemoryStream (data);
            var sound = new RawPcmInput (pcm, decoder.Format);
            file.Dispose();
            return sound;
        }
    }

    sealed class WbcDecoder
    {
        IBinaryStream   m_input;
        WaveFormat      m_format;
        int             m_chunk_size;
        byte[]          m_chunk_buffer;
        short[]         m_sample_buffer;
        int             m_bitrate;

        // Transform buffers for each channel
        float[][] m_transform_buffer_1;
        float[][] m_transform_buffer_2;
        float[][] m_transform_buffer_3;
        float[][] m_transform_buffer_4;
        float[][] m_transform_buffer_5;
        float[]   m_coefficient_buffer;
        int       m_coefficient_offset;

        public WaveFormat Format { get { return m_format; } }

        public WbcDecoder (IBinaryStream input)
        {
            m_input = input;
        }

        public byte[] Decode ()
        {
            var header = m_input.ReadHeader (0x60);
            uint endOffset = header.ToUInt32 (4);
            int codecType = header.ToUInt16 (8);

            m_format.FormatTag = 1;
            m_format.Channels = header.ToUInt16 (0xA);
            m_format.SamplesPerSecond = header.ToUInt32 (0x10);
            m_format.BitsPerSample = 16;
            m_format.BlockAlign = (ushort)(m_format.Channels * m_format.BitsPerSample / 8);
            m_format.SetBPS();

            if (0 == m_format.Channels)
                throw new InvalidFormatException();

            int sampleSize = header.ToInt32 (0x1C);
            m_sample_buffer = new short[sampleSize];
            m_channel_length = sampleSize / m_format.Channels;

            int chunkSize = header.ToInt32 (0x20);
            m_chunk_buffer = new byte[(chunkSize + 3) & ~3];
            m_bitrate = (int)m_format.AverageBytesPerSecond * 4 * chunkSize / sampleSize;

            InitializeTransformBuffers();

            int chunkCount = header.ToInt32 (0x14);
            var chunkOffsetTable = new uint[chunkCount+1];
            for (int i = 0; i < chunkCount; ++i)
                chunkOffsetTable[i] = m_input.ReadUInt32();
            chunkOffsetTable[chunkCount] = endOffset;

            var output = new byte[header.ToInt32 (0x18)];
            int destinationIndex = 0;
            for (int i = 0; i < chunkCount; ++i)
            {
                uint offset = chunkOffsetTable[i];
                chunkSize = (int)(chunkOffsetTable[i+1] - offset);
                m_input.Position = offset;
                m_input.Read (m_chunk_buffer, 0, chunkSize);
                ResetBits();

                if (codecType < 0x40)
                    throw new NotImplementedException();

                else if (codecType > 0x100 && codecType < 0x105)
                    DecodeChunk (FrequencyTable[codecType - 0x101]);
                else
                    throw new InvalidFormatException();

                int sourceIndex = 0;
                for (ushort channel = 0; channel < m_format.Channels; ++channel)
                {
                    int channelDestIndex = destinationIndex + channel * 2;
                    for (int sampleIndex = 0; sampleIndex < m_channel_length && channelDestIndex < output.Length; ++sampleIndex)
                    {
                        LittleEndian.Pack (m_sample_buffer[sourceIndex++], output, channelDestIndex);
                        channelDestIndex += 4;
                    }
                }
                destinationIndex += sampleSize * 2;
            }
            return output;
        }

        void InitializeTransformBuffers()
        {
            m_transform_buffer_1 = new float[m_format.Channels][];
            m_transform_buffer_2 = new float[m_format.Channels][];
            m_transform_buffer_3 = new float[m_format.Channels][];
            m_transform_buffer_4 = new float[m_format.Channels][];
            m_transform_buffer_5 = new float[m_format.Channels][];

            for (int i = 0; i < m_format.Channels; i++)
            {
                m_transform_buffer_1[i] = new float[m_channel_length];
                m_transform_buffer_2[i] = new float[m_channel_length];
                m_transform_buffer_3[i] = new float[m_channel_length];
                m_transform_buffer_4[i] = new float[m_channel_length];
                m_transform_buffer_5[i] = new float[m_channel_length];
            }

            m_coefficient_buffer = new float[m_channel_length * 2];
            m_coefficient_offset = m_channel_length;
        }

        int m_channel_length;
        ushort[] m_frequency_bands = new ushort[128];

        void DecodeChunk (int targetFrequency)
        {
            int halfSampleRate = (int)(m_format.SamplesPerSecond >> 1);
            int samplesPerChannel = m_channel_length;
            if (targetFrequency != 0 && targetFrequency < halfSampleRate)
            {
                samplesPerChannel = m_channel_length * targetFrequency / halfSampleRate;
            }
            uint headerByte = GetBits (8);
            if ((headerByte & 0xF0) != 0xF0)
            {
                Array.Clear (m_sample_buffer, 0, m_sample_buffer.Length);
                return;
            }
            uint stereoFlag = headerByte & 8;
            int destinationOffset = 0;
            if (stereoFlag != 0)
                destinationOffset = m_channel_length;
            int outputStart = destinationOffset;

            for (ushort channel = 0; channel < m_format.Channels; ++channel)
            {
                int bandOffset = 16 * channel;
                int sampleIndex;
                for (sampleIndex = 0; sampleIndex < 16; ++sampleIndex)
                {
                    m_frequency_bands[bandOffset + sampleIndex] = (ushort)GetBits (4);
                }
                for (sampleIndex = 0; sampleIndex < samplesPerChannel; ++sampleIndex)
                {
                    GetFrequencyBand (halfSampleRate * sampleIndex / m_channel_length);
                    int runLength = 0;
                    short sample = DecodeSample (out runLength);
                    if (sample != 0)
                    {
                        m_sample_buffer[destinationOffset + sampleIndex] = sample;
                    }
                    else
                    {
                        if (0 == runLength)
                        {
                            Array.Clear (m_sample_buffer, destinationOffset + sampleIndex, samplesPerChannel - sampleIndex);
                            sampleIndex = samplesPerChannel;
                            break;
                        }
                        for (int j = 0; j < runLength; ++j)
                        {
                            if (sampleIndex >= samplesPerChannel)
                                break;
                            m_sample_buffer[destinationOffset + sampleIndex++] = 0;
                        }
                        --sampleIndex;
                    }
                }
                Array.Clear (m_sample_buffer, destinationOffset + sampleIndex, m_channel_length - samplesPerChannel);
                destinationOffset += m_channel_length;
            }

            destinationOffset = outputStart;
            for (ushort channel = 0; channel < m_format.Channels; ++channel)
            {
                int bandOffset = 16 * channel;
                int frequencyIndex = 0;
                for (int sampleIndex = 0; sampleIndex < m_channel_length; ++sampleIndex)
                {
                    var bandValue = m_frequency_bands[GetFrequencyBand (frequencyIndex / m_channel_length) + bandOffset];
                    int amplificationFactor = GetAmplificationFactor (frequencyIndex / m_channel_length);
                    short amplifiedSample = AmplifyAndShift (m_sample_buffer[destinationOffset + sampleIndex], amplificationFactor, bandValue);
                    frequencyIndex += halfSampleRate;
                    m_sample_buffer[destinationOffset + sampleIndex] = amplifiedSample;
                }
                destinationOffset += m_channel_length;
            }

            if (stereoFlag != 0)
            {
                // Mid-side stereo decoding
                for (ushort channelPair = 0; channelPair < m_format.Channels; channelPair += 2)
                {
                    int leftChannelOffset = outputStart + channelPair * m_channel_length;
                    int rightChannelOffset = leftChannelOffset + m_channel_length;

                    for (int i = 0; i < m_channel_length; ++i)
                    {
                        short midValue = (short)(m_sample_buffer[rightChannelOffset + i] / 2);
                        short leftValue = m_sample_buffer[leftChannelOffset + i];
                        short sumValue = (short)(midValue + leftValue);
                        short diffValue = (short)(midValue - leftValue);
                        m_sample_buffer[leftChannelOffset + i] = sumValue;
                        m_sample_buffer[rightChannelOffset + i] = diffValue;
                    }
                }
            }

            int channelOffset = 0;
            for (ushort channel = 0; channel < m_format.Channels; ++channel)
            {
                ApplyInverseTransform (channelOffset, channel);
                channelOffset += m_channel_length;
            }
        }

        void ApplyForwardTransform(float[] buffer, float[] coefficients, int length)
        {
            // This is an MDCT implementation using the coefficient buffer for windowing
            float[] window = new float[length];

            // Apply analysis window (using coefficients as window function)
            for (int i = 0; i < length; i++)
                window[i] = (float)(Math.Sin(Math.PI * i / length) * Math.Sqrt(2.0));

            // Perform MDCT
            for (int k = 0; k < length/2; k++)
            {
                float sum = 0;
                for (int n = 0; n < length; n++)
                {
                    // Modified DCT-IV with pre-rotation
                    float angle = (float)(Math.PI / length * (n + 0.5 + length/4.0) * (2*k + 1));
                    sum += buffer[n] * (float)Math.Cos(angle) * window[n];
                }
                coefficients[k] = sum;  // Store in coefficients buffer
            }
        }

        void ApplyInverseTransform(int destinationOffset, int channelIndex)
        {
            if (m_channel_length <= 0 || channelIndex < 0)
                return;

            float[] workBuffer = m_transform_buffer_5[channelIndex];  // Working buffer
            float[] realBuffer = m_transform_buffer_2[channelIndex];  // Real part buffer
            float[] imagBuffer = m_transform_buffer_4[channelIndex];  // Imaginary part buffer

            float[] tempBuffer1 = m_transform_buffer_1[channelIndex]; // Temporary buffer 1
            float[] tempBuffer2 = m_transform_buffer_3[channelIndex]; // Temporary buffer 2

            // Convert samples to float with sqrt(2)/2 scaling
            const float SQRT2_OVER_2 = 0.7071067690849304f;

            for (int i = 0; i < m_channel_length; i++)
                workBuffer[i] = m_sample_buffer[destinationOffset + i] * SQRT2_OVER_2;

            // Apply MDCT using coefficient buffer
            ApplyForwardTransform(workBuffer, m_coefficient_buffer, m_channel_length);

            // Combine with previous frame using overlap-add
            for (int i = 0; i < m_channel_length/2; i++)
            {
                // Use coefficient buffer for synthesis window
                float window = (float)Math.Sin(Math.PI * i / m_channel_length);

                // Overlap-add with previous frame
                float prevSample = realBuffer[i];
                float currentSample = m_coefficient_buffer[m_coefficient_offset + i] * window;

                // Store transformed result back to sample buffer
                int outputSample = (int)((prevSample + currentSample) * 32768.0f / m_channel_length);

                // Clamp to 16-bit range
                if (outputSample > 0x7FFF)
                    outputSample = 0x7FFF;
                else if (outputSample < -32768)
                    outputSample = -32768;

                m_sample_buffer[destinationOffset + i] = (short)outputSample;
            }

            // Save current frame for overlap-add with next frame
            for (int i = 0; i < m_channel_length/2; i++)
                realBuffer[i] = m_coefficient_buffer[m_coefficient_offset + m_channel_length/2 + i];
        }

        int GetFrequencyBand (int frequency)
        {
            if (frequency >= 16000)      return 15;
            else if (frequency >= 12000) return 14;
            else if (frequency >= 10000) return 13;
            else if (frequency >= 8000)  return 12;
            else if (frequency >= 6000)  return 11;
            else if (frequency >= 4200)  return 10;
            else if (frequency >= 3400)  return 9;
            else if (frequency >= 2600)  return 8;
            else if (frequency >= 1800)  return 7;
            else if (frequency >= 1400)  return 6;
            else if (frequency >= 1000)  return 5;
            else if (frequency >= 800)   return 4;
            else if (frequency >= 600)   return 3;
            else if (frequency >= 400)   return 2;
            else if (frequency >= 200)   return 1;
            else                         return 0;
        }

        int GetAmplificationFactor (int frequency)
        {
            if (frequency >= 16000)     return 10;
            else if (frequency >= 8000) return 3;
            else if (frequency > 250)   return 1;
            else if (frequency <= 30)   return 315;
            else if (frequency <= 60)   return 45;
            else if (frequency <= 125)  return 10;
            else                        return 3;
        }

        short AmplifyAndShift (int sample, int amplificationFactor, int shiftAmount)
        {
            int result = sample;
            if (sample != 0)
            {
                if (shiftAmount >= 1)
                    result = sample << shiftAmount;
                if (amplificationFactor > 1)
                    result *= amplificationFactor;
            }
            return (short)result;
        }

        short DecodeSample (out int runLength)
        {
            uint bitPattern = GetBits (3);
            if (0 == bitPattern)
            {
                int zeroCount = 1;
                for (; zeroCount < 0x10; ++zeroCount)
                {
                    if (GetBits (1) != 1)
                        break;
                }
                runLength = zeroCount < 16 ? zeroCount : 0;
                return 0;
            }
            else if (bitPattern >= 7)
            {
                uint extendedBits = GetBits (7);
                int magnitudeBits = 0;
                int bitPosition = 6;
                while (0 != ((1 << bitPosition) & extendedBits))
                {
                    ++magnitudeBits;
                    --bitPosition;
                    if (magnitudeBits >= 7)
                    {
                        runLength = 0;
                        return (short)GetBits (16);
                    }
                }
                if (magnitudeBits >= 7)
                {
                    runLength = 0;
                    return (short)GetBits (16);
                }

                short baseMagnitude = (short)(1 << (magnitudeBits + 5));
                uint additionalBits;
                if (magnitudeBits != 0)
                    additionalBits = ((extendedBits & ((uint)63 >> magnitudeBits)) << (2 * magnitudeBits)) | GetBits(2 * magnitudeBits);
                else
                    additionalBits = (ushort)extendedBits;

                short magnitude = (short)(baseMagnitude + 32 + (additionalBits & ~(uint)baseMagnitude));
                runLength = 0;
                if (0 != ((1 << (magnitudeBits + 5)) & additionalBits))
                    return (short)-magnitude;
                else
                    return magnitude;
            }
            else
            {
                int signBit = 1 << ((int)bitPattern - 1);
                short value = (short)GetBits ((int)bitPattern);
                short magnitude = (short)(signBit + (value & ~signBit));
                runLength = 0;
                if (0 != (signBit & value))
                    return (short)-magnitude;
                else
                    return magnitude;
            }
        }

        int     m_bit_position;
        int     m_bits_available;
        uint    m_bit_buffer;

        void ResetBits ()
        {
            m_bit_position = 0;
            m_bits_available = 0;
            m_bit_buffer = 0;
        }

        void AlignBits ()
        {
            int alignment = m_bits_available & 7;
            m_bit_buffer <<= alignment;
            m_bits_available -= alignment;
        }

        uint GetBits (int count)
        {
            while (m_bits_available < count)
            {
                m_bit_buffer |= (uint)m_chunk_buffer[m_bit_position++] << (24 - m_bits_available);
                m_bits_available += 8;
            }
            m_bits_available -= count;
            uint bits = m_bit_buffer >> (32 - count);
            m_bit_buffer <<= count;
            return bits;
        }

        static int[] FrequencyTable = { 0, 0, 16000, 12000 };
    }
}