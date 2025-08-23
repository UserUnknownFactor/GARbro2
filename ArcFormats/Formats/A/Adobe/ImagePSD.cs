using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Adobe
{
    internal enum PsdMode
    {
        BITMAP = 0,
        GRAYSCALE = 1,
        INDEXED = 2,
        RGB = 3,
        CMYK = 4,
        MULTICHANNEL = 7,
        DUOTONE = 8,
        LAB = 9
    }

    internal enum PsdCompression
    {
        RAW = 0,
        RLE = 1,
        ZIP = 2,
        ZIP_P = 3
    }

    internal class LayerInfo
    {
        public int Top;
        public int Left;
        public int Bottom;
        public int Right;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public short ChannelCount;
        public List<LayerChannelInfo> Channels;
        public string BlendMode;
        public byte Opacity;
        public byte Flags;
        public string Name;
        public byte[] PixelData;
        public bool IsVisible => (Flags & 0x02) == 0;
    }

    internal class LayerChannelInfo
    {
        public short ChannelId;
        public int DataLength;
    }

    internal class PsdMetaData : ImageMetaData
    {
        public int Channels;
        public PsdMode Mode;
        public int Version;
        public int Depth;
        public bool HasAlpha;
        public List<LayerInfo> Layers;
        public bool HasValidMergedImage;
    }

    [Export(typeof(ImageFormat))]
    public class PsdFormat : ImageFormat
    {
        public override string         Tag { get { return "PSD"; } }
        public override string Description { get { return "Adobe Photoshop image format"; } }
        public override uint     Signature { get { return 0x53504238; } } // '8BPS'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (26).ToArray();
            int version = BigEndian.ToInt16 (header, 4);
            if (version != 1 && version != 2) // 2 -> PSB (large format)
                return null;

            // 6 reserved bytes ignored

            int channels = BigEndian.ToInt16 (header, 0x0C);
            if (channels < 1 || channels > 56)
                return null;

            uint height = BigEndian.ToUInt32 (header, 0x0E);
            uint width  = BigEndian.ToUInt32 (header, 0x12);

            uint maxDimension = (version == 1) ? 30000u : 300000u;
            if (width < 1 || width > maxDimension || height < 1 || height > maxDimension)
                return null;

            int depth = BigEndian.ToInt16 (header, 0x16);
            if (depth != 1 && depth != 8 && depth != 16 && depth != 32)
                return null;

            int int_mode = BigEndian.ToInt16(header, 0x18);
            if (int_mode > (int)PsdMode.LAB)
                return null;

            PsdMode mode = (PsdMode)int_mode;

            bool hasAlpha = false;
            if (mode == PsdMode.RGB && channels == 4)
                hasAlpha = true;
            else if (mode == PsdMode.GRAYSCALE && channels == 2)
                hasAlpha = true;
            else if (mode == PsdMode.CMYK && channels == 5)
                hasAlpha = true;

            return new PsdMetaData
            {
                Width = width,
                Height = height,
                Channels = channels,
                BPP = channels * depth,
                Mode = mode,
                Version = version,
                Depth = depth,
                HasAlpha = hasAlpha,
                Layers = new List<LayerInfo>(),
                HasValidMergedImage = true
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (PsdMetaData)info;
            using (var reader = new PsdReader (stream, meta))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, reader.Palette, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PsdFormat.Write not implemented");
        }
    }

    internal class PsdReader : IDisposable
    {
        IBinaryStream   m_input;
        PsdMetaData     m_info;
        byte[]          m_output;
        int             m_channel_size;
        int             m_stride;
        long            m_layerSectionStart;
        long            m_imageSectionStart;

        public PixelFormat Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public byte[] Data { get { return m_output; } }

        public PsdReader(IBinaryStream input, PsdMetaData info)
        {
            m_info = info;
            m_input = input;

            SetupPixelFormat();

            m_channel_size = (int)m_info.Height * (int)m_info.Width * m_info.Depth / 8;
            m_stride = (int)m_info.Width * ((Format.BitsPerPixel + 7) / 8);
            m_stride = (m_stride + 3) & ~3;
        }

        private void SetupPixelFormat()
        {
            switch (m_info.Mode)
            {
                case PsdMode.RGB:
                    if (m_info.Depth == 8)
                    {
                        if (3 == m_info.Channels) Format = PixelFormats.Bgr24;
                        else if (4 == m_info.Channels) Format = PixelFormats.Bgra32;
                        else if (m_info.Channels > 4) Format = PixelFormats.Bgr32;
                        else
                            throw new NotSupportedException($"Unsupported channel count: {m_info.Channels}");
                    }
                    else if (m_info.Depth == 16)
                    {
                        if (3 == m_info.Channels) Format = PixelFormats.Rgb48;
                        else if (4 == m_info.Channels) Format = PixelFormats.Rgba64;
                        else
                            throw new NotSupportedException($"Unsupported 16-bit channel count: {m_info.Channels}");
                    }
                    else if (m_info.Depth == 32)
                    {
                        if (3 == m_info.Channels) Format = PixelFormats.Rgb128Float;
                        else if (4 == m_info.Channels) Format = PixelFormats.Rgba128Float;
                        else
                            throw new NotSupportedException($"Unsupported 32-bit channel count: {m_info.Channels}");
                    }
                    else
                        throw new NotSupportedException($"Unsupported bit depth: {m_info.Depth}");
                    break;

                case PsdMode.INDEXED:
                    if (m_info.Depth == 8) Format = PixelFormats.Indexed8;
                    else
                        throw new NotSupportedException($"Unsupported indexed color depth: {m_info.Depth}");
                    break;

                case PsdMode.GRAYSCALE:
                    if (m_info.Depth == 8) Format = PixelFormats.Gray8;
                    else if (m_info.Depth == 16) Format = PixelFormats.Gray16;
                    else if (m_info.Depth == 32) Format = PixelFormats.Gray32Float;
                    else
                        throw new NotSupportedException($"Unsupported grayscale depth: {m_info.Depth}");
                    break;

                case PsdMode.BITMAP:
                    Format = PixelFormats.BlackWhite;
                    m_stride = ((int)m_info.Width + 7) / 8;
                    m_stride = (m_stride + 3) & ~3;
                    break;

                case PsdMode.CMYK:
                    if (m_info.Depth == 8)
                        Format = PixelFormats.Cmyk32;
                    else
                        throw new NotSupportedException($"Unsupported CMYK depth: {m_info.Depth}");
                    break;

                case PsdMode.LAB:
                    if (m_info.Depth == 8) Format = PixelFormats.Bgr24;
                    else if (m_info.Depth == 16) Format = PixelFormats.Rgb48;
                    else
                        throw new NotSupportedException($"Unsupported Lab depth: {m_info.Depth}");
                    break;

                default:
                    throw new NotImplementedException($"PSD color mode {m_info.Mode} not supported");
            }
        }

        public void Unpack ()
        {
            m_input.Position = 0x1A;
            int color_data_length = Binary.BigEndian (m_input.ReadInt32());
            long next_pos = m_input.Position + color_data_length;

            if (color_data_length > 0)
            {
                if (m_info.Mode == PsdMode.INDEXED)
                    ReadPalette (color_data_length);
                m_input.Position = next_pos;
            }

            int resourceLength = Binary.BigEndian (m_input.ReadInt32());
            m_input.Position += resourceLength;

            m_layerSectionStart = m_input.Position;

            long layerMaskLength = (m_info.Version == 1) ?
                Binary.BigEndian(m_input.ReadInt32()) :
                Binary.BigEndian(m_input.ReadInt64());

            if (layerMaskLength > 0)
            {
                long layerSectionEnd = m_input.Position + layerMaskLength;
                try
                {
                    ReadLayers();
                }
                catch
                {
                    // If layer reading fails, continue to merged image
                }
                m_input.Position = layerSectionEnd;
            }
            else
            {
                m_input.Position += layerMaskLength;
            }

            m_imageSectionStart = m_input.Position;

            if (!CheckMergedImageValid())
            {
                if (m_info.Layers != null && m_info.Layers.Count > 0)
                {
                    UseLayersAsFallback();
                    return;
                }

                throw new InvalidOperationException("No valid merged image data and no layers found");
            }

            try
            {
                ReadMergedImage();
            }
            catch
            {
                if (m_info.Layers != null && m_info.Layers.Count > 0)
                {
                    UseLayersAsFallback();
                    return;
                }
                throw;
            }

            if (IsImageInvalid())
            {
                if (m_info.Layers != null && m_info.Layers.Count > 0)
                {
                    UseLayersAsFallback();
                    return;
                }
            }
        }

        private bool CheckMergedImageValid()
        {
            long savedPos = m_input.Position;
            try
            {
                long remainingBytes = m_input.Length - m_input.Position;
                long minRequired = 2; // At least compression method

                if (remainingBytes < minRequired)
                    return false;

                int compression = Binary.BigEndian(m_input.ReadInt16());
                if (compression > 3)
                    return false;

                remainingBytes = m_input.Length - m_input.Position;

                if (compression == 0) // Raw
                {
                    long expectedSize = m_info.Channels * m_channel_size;
                    if (remainingBytes < expectedSize)
                        return false;
                }

                return true;
            }
            finally
            {
                m_input.Position = savedPos;
            }
        }

        private bool IsImageInvalid()
        {
            if (m_output == null || m_output.Length == 0)
                return true;

            bool allWhite = true;
            bool allBlack = true;

            for (int i = 0; i < Math.Min(m_output.Length, 1000); i++)
            {
                if (m_output[i] != 0xFF) allWhite = false;
                if (m_output[i] != 0x00) allBlack = false;

                if (!allWhite && !allBlack)
                    return false;
            }

            return allWhite || allBlack;
        }

        private void UseLayersAsFallback()
        {
            var visibleLayers = m_info.Layers.Where(l => (l.IsVisible && l.PixelData != null)).ToList();

            if (visibleLayers.Count == 0)
                throw new InvalidOperationException("No layers available");

            var layer = visibleLayers.OrderByDescending(l => l.PixelData.Length).First();
            UseLayerAsImage(layer);
        }

        private void UseLayerAsImage(LayerInfo layer)
        {
            m_output = new byte[m_stride * (int)m_info.Height];

            if (m_info.HasAlpha && Format == PixelFormats.Bgra32)
            {
                for (int i = 3; i < m_output.Length; i += 4)
                    m_output[i] = 0; // Clear to transparent
            }
            else
            {
                for (int i = 0; i < m_output.Length; i++)
                    m_output[i] = 255; // Clear to white
            }

            if (layer.PixelData != null && layer.PixelData.Length > 0)
                CopyLayerToOutput(layer);
        }

        private void CopyLayerToOutput(LayerInfo layer)
        {
            int bytesPerPixel = (Format.BitsPerPixel + 7) / 8;

            // Process the layer's pixel data
            var processedData = ProcessLayerPixels(layer);

            for (int y = 0; y < layer.Height; y++)
            {
                int srcY = y;
                int dstY = layer.Top + y;

                if (dstY >= 0 && dstY < m_info.Height)
                {
                    for (int x = 0; x < layer.Width; x++)
                    {
                        int srcX = x;
                        int dstX = layer.Left + x;

                        if (dstX >= 0 && dstX < m_info.Width)
                        {
                            int srcOffset = (srcY * layer.Width + srcX) * bytesPerPixel;
                            int dstOffset = dstY * m_stride + dstX * bytesPerPixel;

                            if (srcOffset + bytesPerPixel <= processedData.Length &&
                                dstOffset + bytesPerPixel <= m_output.Length)
                            {
                                Buffer.BlockCopy(processedData, srcOffset, m_output, dstOffset, bytesPerPixel);
                            }
                        }
                    }
                }
            }
        }

        private byte[] ProcessLayerPixels(LayerInfo layer)
        {
            int pixelCount = layer.Width * layer.Height;
            int outputBytesPerPixel = (Format.BitsPerPixel + 7) / 8;
            var output = new byte[pixelCount * outputBytesPerPixel];

            if (layer.PixelData == null || layer.PixelData.Length == 0)
                return output;

            // Similar to regular channel unpacking but for layer data
            int channelsToProcess = Math.Min(layer.ChannelCount, outputBytesPerPixel);

            if (m_info.Depth == 8)
            {
                for (int i = 0; i < pixelCount; i++)
                {
                    for (int ch = 0; ch < channelsToProcess; ch++)
                    {
                        int srcIndex = ch * pixelCount + i;
                        int dstIndex = i * outputBytesPerPixel + GetChannelMapping(ch);

                        if (srcIndex < layer.PixelData.Length && dstIndex < output.Length)
                            output[dstIndex] = layer.PixelData[srcIndex];
                    }
                }
            }

            return output;
        }

        private void ReadLayers()
        {
            long layerInfoLen = (m_info.Version == 1) ?
                Binary.BigEndian(m_input.ReadInt32()) :
                Binary.BigEndian(m_input.ReadInt64());

            if (layerInfoLen == 0)
                return;

            long layerInfoEnd = m_input.Position + layerInfoLen;

            short layerCount = Binary.BigEndian(m_input.ReadInt16());
            if (layerCount < 0)
                layerCount = Math.Abs(layerCount);

            m_info.Layers = new List<LayerInfo>();

            for (int i = 0; i < layerCount; i++)
            {
                var layer = new LayerInfo();

                layer.Top = Binary.BigEndian(m_input.ReadInt32());
                layer.Left = Binary.BigEndian(m_input.ReadInt32());
                layer.Bottom = Binary.BigEndian(m_input.ReadInt32());
                layer.Right = Binary.BigEndian(m_input.ReadInt32());

                layer.ChannelCount = Binary.BigEndian(m_input.ReadInt16());
                layer.Channels = new List<LayerChannelInfo>();

                for (int ch = 0; ch < layer.ChannelCount; ch++)
                {
                    var channelInfo = new LayerChannelInfo
                    {
                        ChannelId = Binary.BigEndian(m_input.ReadInt16()),
                        DataLength = Binary.BigEndian(m_input.ReadInt32())
                    };
                    layer.Channels.Add(channelInfo);
                }

                m_input.ReadInt32(); // Blend mode signature
                layer.BlendMode = Encoding.ASCII.GetString(m_input.ReadBytes(4));
                layer.Opacity = m_input.ReadUInt8();
                m_input.ReadUInt8(); // Clipping
                layer.Flags = m_input.ReadUInt8();
                m_input.ReadUInt8(); // Filler

                int extraDataLen = Binary.BigEndian(m_input.ReadInt32());
                long extraDataEnd = m_input.Position + extraDataLen;

                // Layer mask data
                int maskDataLen = Binary.BigEndian(m_input.ReadInt32());
                m_input.Position += maskDataLen;

                // Layer blending ranges
                int blendingRangesLen = Binary.BigEndian(m_input.ReadInt32());
                m_input.Position += blendingRangesLen;

                // Layer name
                int nameLen = m_input.ReadUInt8();
                int namePadding = (4 - ((nameLen + 1) % 4)) % 4;

                if (nameLen > 0)
                {
                    layer.Name = Encoding.ASCII.GetString(m_input.ReadBytes(nameLen));
                    m_input.Position += namePadding;
                }
                else
                {
                    m_input.Position += namePadding;
                }

                m_input.Position = extraDataEnd;

                m_info.Layers.Add(layer);
            }

            foreach (var layer in m_info.Layers)
            {
                if (layer.Width > 0 && layer.Height > 0)
                {
                    try
                    {
                        layer.PixelData = ReadLayerPixelData(layer);
                    }
                    catch
                    {
                        layer.PixelData = null;
                    }
                }
            }
        }

        private byte[] ReadLayerPixelData(LayerInfo layer)
        {
            int pixelCount = layer.Width * layer.Height;
            int bytesPerPixel = m_info.Depth / 8;
            var layerData = new byte[pixelCount * layer.ChannelCount * bytesPerPixel];

            // Read each channel in the order they appear in the file
            for (int chIndex = 0; chIndex < layer.Channels.Count; chIndex++)
            {
                var channel = layer.Channels[chIndex];
                int compression = Binary.BigEndian(m_input.ReadInt16());
                byte[] channelData;

                switch ((PsdCompression)compression)
                {
                    case PsdCompression.RAW:
                        channelData = m_input.ReadBytes(pixelCount * bytesPerPixel);
                        break;

                    case PsdCompression.RLE:
                        channelData = UnpackLayerRLE(layer.Height, layer.Width, bytesPerPixel);
                        break;

                    default:
                        throw new NotSupportedException($"Layer compression {compression} not supported");
                }

                // Place channel data based on channel ID, not order in file
                int destChannel = GetLayerChannelIndex(channel.ChannelId);
                if (destChannel >= 0 && destChannel < layer.ChannelCount)
                {
                    int destOffset = destChannel * pixelCount * bytesPerPixel;
                    Buffer.BlockCopy(channelData, 0, layerData, destOffset, channelData.Length);
                }
            }

            return layerData;
        }

        private int GetLayerChannelIndex(short channelId)
        {
            switch (channelId)
            {
                case 0: return 0;  // Red
                case 1: return 1;  // Green
                case 2: return 2;  // Blue
                case -1: return 3; // Alpha
                default: return channelId;
            }
        }

        private byte[] UnpackLayerRLE(int height, int width, int bytesPerPixel)
        {
            var result = new byte[height * width * bytesPerPixel];
            var scanlineLengths = new int[height];

            bool use32BitCounts = (m_info.Version == 2);
            for (int i = 0; i < height; i++)
            {
                if (use32BitCounts)
                    scanlineLengths[i] = Binary.BigEndian(m_input.ReadInt32());
                else
                    scanlineLengths[i] = Binary.BigEndian(m_input.ReadUInt16());
            }

            // Decompress RLE data
            int dstOffset = 0;
            for (int y = 0; y < height; y++)
            {
                int lineEnd = dstOffset + width * bytesPerPixel;

                while (dstOffset < lineEnd)
                {
                    sbyte count = m_input.ReadInt8();

                    if (count >= 0)
                    {
                        // Literal run
                        int bytes = (count + 1) * bytesPerPixel;
                        m_input.Read(result, dstOffset, bytes);
                        dstOffset += bytes;
                    }
                    else if (count > -128)
                    {
                        // Repeat run
                        int repeatCount = 1 - count;
                        var value = m_input.ReadBytes(bytesPerPixel);

                        for (int i = 0; i < repeatCount; i++)
                        {
                            Buffer.BlockCopy(value, 0, result, dstOffset, bytesPerPixel);
                            dstOffset += bytesPerPixel;
                        }
                    }
                }
            }

            return result;
        }

        private void ReadMergedImage()
        {
            int compression = Binary.BigEndian(m_input.ReadInt16());
            if (compression > 3)
                throw new NotSupportedException($"PSD compression method {compression} not supported");

            byte[] pixels;
            switch ((PsdCompression)compression)
            {
                case PsdCompression.RAW:
                    pixels = ReadRawData();
                    break;
                case PsdCompression.RLE:
                    pixels = UnpackRLE();
                    break;
                case PsdCompression.ZIP:
                case PsdCompression.ZIP_P:
                    pixels = UnpackZip(compression == 3);
                    break;
                default:
                    throw new NotSupportedException($"Unknown compression method: {(PsdCompression)compression}");
            }

            ProcessPixels(pixels);
        }

        private byte[] ReadRawData()
        {
            int totalSize = m_info.Channels * m_channel_size;
            return m_input.ReadBytes(totalSize);
        }

        private void ProcessPixels(byte[] pixels)
        {
            if (m_info.Mode == PsdMode.LAB)
                pixels = ConvertLabToRgb(pixels);

            if (m_info.Mode == PsdMode.BITMAP || (m_info.Channels == 1 && m_info.Mode == PsdMode.GRAYSCALE))
            {
                m_output = new byte[m_stride * (int)m_info.Height];
                if (m_info.Mode == PsdMode.BITMAP)
                    ProcessBitmapMode(pixels);
                else
                    Buffer.BlockCopy(pixels, 0, m_output, 0, Math.Min(pixels.Length, m_output.Length));
                return;
            }

            int outputBytesPerPixel = (Format.BitsPerPixel + 7) / 8;
            int channelsToProcess = GetChannelsToProcess(outputBytesPerPixel);

            m_output = new byte[m_stride * (int)m_info.Height];

            if (m_info.Depth == 8)
                UnpackChannels8Bit(pixels, channelsToProcess);
            else if (m_info.Depth == 16)
                UnpackChannels16Bit(pixels, channelsToProcess);
            else if (m_info.Depth == 32)
                UnpackChannels32Bit(pixels, channelsToProcess);
        }

        private int GetChannelsToProcess(int outputBytesPerPixel)
        {
            switch (m_info.Mode)
            {
                case PsdMode.RGB:
                    return Math.Min(m_info.Channels, 4);
                case PsdMode.GRAYSCALE:
                    return Math.Min(m_info.Channels, 2);
                case PsdMode.CMYK:
                    return Math.Min(m_info.Channels, 5);
                case PsdMode.INDEXED:
                    return 1;
                default:
                    return Math.Min(outputBytesPerPixel, m_info.Channels);
            }
        }

        private void ProcessBitmapMode(byte[] pixels)
        {
            int srcStride = ((int)m_info.Width + 7) / 8;
            int dstStride = m_stride;

            for (int y = 0; y < m_info.Height; y++)
            {
                int srcOffset = y * srcStride;
                int dstOffset = y * dstStride;

                for (int x = 0; x < srcStride && x < dstStride; x++)
                {
                    if (srcOffset + x < pixels.Length && dstOffset + x < m_output.Length)
                        m_output[dstOffset + x] = pixels[srcOffset + x];
                }
            }
        }

        private void UnpackChannels8Bit(byte[] pixels, int channelsToProcess)
        {
            int pixelCount = (int)(m_info.Width * m_info.Height);
            int outputChannels = GetOutputChannelCount();

            //System.Diagnostics.Debug.WriteLine($"Mode: {m_info.Mode}, Channels: {m_info.Channels}, ChannelsToProcess: {channelsToProcess}, OutputChannels: {outputChannels}");
            //System.Diagnostics.Debug.WriteLine($"Pixel data size: {pixels.Length}, Expected: {pixelCount * m_info.Channels}");

            for (int i = 0; i < pixelCount; i++)
            {
                int dstOffset = i * outputChannels;

                if (m_info.Mode == PsdMode.RGB)
                {
                    // PSD stores channels separately: RRRR...GGGG...BBBB...
                    // We need to interleave them as BGR (or BGRA)
                    if (outputChannels >= 3)
                    {
                        if (2 < m_info.Channels && 2 * pixelCount + i < pixels.Length)
                            m_output[dstOffset + 0] = pixels[2 * pixelCount + i];
                        else
                            m_output[dstOffset + 0] = 0;

                        if (1 < m_info.Channels && 1 * pixelCount + i < pixels.Length)
                            m_output[dstOffset + 1] = pixels[1 * pixelCount + i];
                        else
                            m_output[dstOffset + 1] = 0;

                        if (0 < m_info.Channels && 0 * pixelCount + i < pixels.Length)
                            m_output[dstOffset + 2] = pixels[0 * pixelCount + i];
                        else
                            m_output[dstOffset + 2] = 0;

                        if (outputChannels == 4)
                        {
                            if (m_info.Channels >= 4 && 3 * pixelCount + i < pixels.Length)
                                m_output[dstOffset + 3] = pixels[3 * pixelCount + i];
                            else
                                m_output[dstOffset + 3] = 255;
                        }
                    }
                }
                else
                {
                    for (int ch = 0; ch < channelsToProcess && ch < outputChannels; ch++)
                    {
                        int srcIndex = ch * pixelCount + i;
                        if (srcIndex < pixels.Length && dstOffset + ch < m_output.Length)
                            m_output[dstOffset + ch] = pixels[srcIndex];
                    }
                }
            }
        }

        private void UnpackChannels16Bit(byte[] pixels, int channelsToProcess)
        {
            int pixelCount = (int)(m_info.Width * m_info.Height);
            int outputChannels = GetOutputChannelCount();

            for (int y = 0; y < m_info.Height; y++)
            {
                for (int x = 0; x < m_info.Width; x++)
                {
                    int pixelIndex = y * (int)m_info.Width + x;
                    int dstOffset = y * m_stride + x * outputChannels * 2;

                    for (int ch = 0; ch < channelsToProcess && ch < outputChannels; ch++)
                    {
                        int srcChannel = ch;
                        int dstChannel = GetChannelMapping(ch);

                        if (dstChannel < outputChannels)
                        {
                            int srcIndex = (srcChannel * pixelCount + pixelIndex) * 2;
                            int dstIndex = dstOffset + dstChannel * 2;

                            if (srcIndex + 1 < pixels.Length && dstIndex + 1 < m_output.Length)
                            {
                                m_output[dstIndex] = pixels[srcIndex + 1];
                                m_output[dstIndex + 1] = pixels[srcIndex];
                            }
                        }
                    }
                }
            }
        }

        private void UnpackChannels32Bit(byte[] pixels, int channelsToProcess)
        {
            throw new NotImplementedException("32-bit float channel unpacking is not implemented");
        }

        private int GetOutputChannelCount()
        {
            switch (Format.BitsPerPixel)
            {
                case 8: return 1;
                case 16: return 1;
                case 24: return 3;
                case 32: return 4;
                case 48: return 3;
                case 64: return 4;
                case 128: return 4;
                default: return Format.BitsPerPixel / 8;
            }
        }

        private int GetChannelMapping(int channel)
        {
            if (m_info.Mode == PsdMode.RGB)
            {
                switch (channel)
                {
                    case 0: return 2; // R → B position
                    case 1: return 1; // G → G position
                    case 2: return 0; // B → R position
                    case 3: return 3; // A → A position
                    default: return channel;
                }
            }
            return channel;
        }

        private byte[] ConvertLabToRgb(byte[] labPixels)
        {
            int pixelCount = (int)(m_info.Width * m_info.Height);
            byte[] rgbPixels = new byte[pixelCount * 3];

            for (int i = 0; i < pixelCount; i++)
            {
                double L = labPixels[i] * 100.0 / 255.0;
                double a = labPixels[i + m_channel_size] - 128.0;
                double b = labPixels[i + 2 * m_channel_size] - 128.0;

                double fy = (L + 16.0) / 116.0;
                double fx = fy + a / 500.0;
                double fz = fy - b / 200.0;

                double x = fx > 0.206897 ? fx * fx * fx : (fx - 16.0 / 116.0) / 7.787;
                double y = fy > 0.206897 ? fy * fy * fy : (fy - 16.0 / 116.0) / 7.787;
                double z = fz > 0.206897 ? fz * fz * fz : (fz - 16.0 / 116.0) / 7.787;

                x *= 0.95047;
                y *= 1.00000;
                z *= 1.08883;

                double r = x * 3.2406 + y * -1.5372 + z * -0.4986;
                double g = x * -0.9689 + y * 1.8758 + z * 0.0415;
                double b_val = x * 0.0557 + y * -0.2040 + z * 1.0570;

                r = r > 0.0031308 ? 1.055 * Math.Pow(r, 1.0 / 2.4) - 0.055 : 12.92 * r;
                g = g > 0.0031308 ? 1.055 * Math.Pow(g, 1.0 / 2.4) - 0.055 : 12.92 * g;
                b_val = b_val > 0.0031308 ? 1.055 * Math.Pow(b_val, 1.0 / 2.4) - 0.055 : 12.92 * b_val;

                rgbPixels[i * 3] = (byte)Math.Max(0, Math.Min(255, (int)(r * 255)));
                rgbPixels[i * 3 + 1] = (byte)Math.Max(0, Math.Min(255, (int)(g * 255)));
                rgbPixels[i * 3 + 2] = (byte)Math.Max(0, Math.Min(255, (int)(b_val * 255)));
            }

            return rgbPixels;
        }

        void ReadPalette(int palette_size)
        {
            int colors = Math.Min (256, palette_size / 3);
            var palette_data = m_input.ReadBytes(palette_size);
            var colors_array = new Color[colors];

            for (int i = 0; i < colors; i++)
            {
                colors_array[i] = Color.FromRgb(
                    palette_data[i],
                    palette_data[i + 256],
                    palette_data[i + 512]
                );
            }

            Palette = new BitmapPalette(colors_array);
        }

        byte[] UnpackRLE()
        {
            var scanlines = new int[m_info.Channels, (int)m_info.Height];
            bool use32BitCounts = (m_info.Version == 2);

            // Read scanline byte counts (compressed size for each scanline)
            for (int ch = 0; ch < m_info.Channels; ++ch)
            {
                for (uint row = 0; row < m_info.Height; ++row)
                {
                    if (use32BitCounts)
                        scanlines[ch, row] = Binary.BigEndian(m_input.ReadInt32());
                    else
                        scanlines[ch, row] = Binary.BigEndian(m_input.ReadUInt16());
                }
            }

            var pixels = new byte[m_info.Channels * m_channel_size];
            int dst = 0;
            int bytesPerSample = m_info.Depth / 8;
            int samplesPerRow = (int)m_info.Width;

            for (int ch = 0; ch < m_info.Channels; ++ch)
            {
                for (uint row = 0; row < m_info.Height; ++row)
                {
                    int line_count = scanlines[ch, row]; // Compressed B to read
                    int n = 0; // B read so far
                    int decodedSamples = 0; // Samples decoded for this row

                    while (n < line_count && decodedSamples < samplesPerRow)
                    {
                        sbyte count = m_input.ReadInt8();
                        ++n;

                        if (count >= 0)
                        {
                            // Literal run: copy count+1 samples
                            int literalCount = count + 1;
                            int bytes = literalCount * bytesPerSample;

                            m_input.Read(pixels, dst, bytes);
                            dst += bytes;
                            n += bytes;
                            decodedSamples += literalCount;
                        }
                        else if (count > -128)
                        {
                            // RLE run: repeat the next sample
                            int repeatCount = 1 - count;

                            if (bytesPerSample == 1)
                            {
                                byte value = m_input.ReadUInt8();
                                ++n;
                                for (int i = 0; i < repeatCount; ++i)
                                    pixels[dst++] = value;
                            }
                            else if (bytesPerSample == 2)
                            {
                                byte b1 = m_input.ReadUInt8();
                                byte b2 = m_input.ReadUInt8();
                                n += 2;
                                for (int i = 0; i < repeatCount; ++i)
                                {
                                    pixels[dst++] = b1;
                                    pixels[dst++] = b2;
                                }
                            }
                            else if (bytesPerSample == 4)
                            {
                                var value = m_input.ReadBytes(4);
                                n += 4;
                                for (int i = 0; i < repeatCount; ++i)
                                {
                                    Buffer.BlockCopy(value, 0, pixels, dst, 4);
                                    dst += 4;
                                }
                            }

                            decodedSamples += repeatCount;
                        }
                    }
                }
            }

            return pixels;
        }

        byte[] UnpackZip(bool withPrediction)
        {
            var compressedData = m_input.ReadBytes((int)(m_input.Length - m_input.Position));
            byte[] decompressed;

            using (var ms = new MemoryStream(compressedData, 2, compressedData.Length - 2))
            using (var zlib = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                zlib.CopyTo(output);
                decompressed = output.ToArray();
            }

            if (withPrediction)
            {
                ApplyPrediction(decompressed);
            }

            return decompressed;
        }

        void ApplyPrediction(byte[] data)
        {
            int bytesPerSample = m_info.Depth / 8;
            int samplesPerRow = (int)m_info.Width;

            for (int ch = 0; ch < m_info.Channels; ++ch)
            {
                int channelOffset = ch * m_channel_size;

                for (int row = 0; row < m_info.Height; ++row)
                {
                    int rowOffset = channelOffset + row * samplesPerRow * bytesPerSample;

                    if (bytesPerSample == 1)
                    {
                        for (int col = 1; col < samplesPerRow; ++col)
                        {
                            int idx = rowOffset + col;
                            if (idx < data.Length && idx - 1 >= 0)
                                data[idx] = (byte)(data[idx] + data[idx - 1]);
                        }
                    }
                    else if (bytesPerSample == 2)
                    {
                        for (int col = 1; col < samplesPerRow; ++col)
                        {
                            int idx = rowOffset + col * 2;
                            int prev = rowOffset + (col - 1) * 2;

                            if (idx + 1 < data.Length && prev + 1 < data.Length)
                            {
                                ushort prevValue = (ushort)((data[prev] << 8) | data[prev + 1]);
                                ushort delta = (ushort)((data[idx] << 8) | data[idx + 1]);
                                ushort newValue = (ushort)(prevValue + delta);

                                data[idx] = (byte)(newValue >> 8);
                                data[idx + 1] = (byte)(newValue & 0xFF);
                            }
                        }
                    }
                    else if (bytesPerSample == 4)
                    {
                        for (int col = 1; col < samplesPerRow; ++col)
                        {
                            int idx = rowOffset + col * 4;
                            int prev = rowOffset + (col - 1) * 4;

                            if (idx + 3 < data.Length && prev + 3 < data.Length)
                            {
                                byte[] prevBytes = new byte[4] { data[prev], data[prev + 1], data[prev + 2], data[prev + 3] };
                                byte[] currBytes = new byte[4] { data[idx], data[idx + 1], data[idx + 2], data[idx + 3] };

                                if (BitConverter.IsLittleEndian)
                                {
                                    Array.Reverse(prevBytes);
                                    Array.Reverse(currBytes);
                                }

                                float prevValue = BitConverter.ToSingle(prevBytes, 0);
                                float delta = BitConverter.ToSingle(currBytes, 0);
                                float newValue = prevValue + delta;

                                byte[] newBytes = BitConverter.GetBytes(newValue);
                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(newBytes);

                                Buffer.BlockCopy(newBytes, 0, data, idx, 4);
                            }
                        }
                    }
                }
            }
        }

        #region IDisposable Members
        bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                //if (disposing) { }
                _disposed = true;
            }
        }
        #endregion
    }
}