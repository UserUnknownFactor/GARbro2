using System;
using System.IO;
using System.Text;
using System.ComponentModel.Composition;
using System.Windows.Media;

namespace GameRes
{
    [Export(typeof(VideoFormat))]
    public class WmvFormat : VideoFormat
    {
        public override string         Tag { get { return "WMV"; } }
        public override string Description { get { return "Windows Media Video"; } }
        public override uint     Signature { get { return  0x75B22630; } } // ASF header GUID first 4 bytes
        public override bool      CanWrite { get { return  false; } }

        #region ASF GUIDs

        private static readonly byte[] ASF_HEADER_GUID = {
            0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
            0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
        };

        private static readonly byte[] ASF_FILE_PROPERTIES_GUID = {
            0xA1, 0xDC, 0xAB, 0x8C, 0x47, 0xA9, 0xCF, 0x11,
            0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
        };

        private static readonly byte[] ASF_STREAM_PROPERTIES_GUID = {
            0x91, 0x07, 0xDC, 0xB7, 0xB7, 0xA9, 0xCF, 0x11,
            0x8E, 0xE6, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
        };

        private static readonly byte[] ASF_VIDEO_MEDIA_GUID = {
            0xC0, 0xEF, 0x19, 0xBC, 0x4D, 0x5B, 0xCF, 0x11,
            0xA8, 0xFD, 0x00, 0x80, 0x5F, 0x5C, 0x44, 0x2B
        };

        private static readonly byte[] ASF_AUDIO_MEDIA_GUID = {
            0x40, 0x9E, 0x69, 0xF8, 0x4D, 0x5B, 0xCF, 0x11,
            0xA8, 0xFD, 0x00, 0x80, 0x5F, 0x5C, 0x44, 0x2B
        };

        private static readonly byte[] ASF_CODEC_LIST_GUID = {
            0x40, 0x52, 0xD1, 0x86, 0x1D, 0x31, 0xD0, 0x11,
            0xA3, 0xA4, 0x00, 0xA0, 0xC9, 0x03, 0x48, 0xF6
        };

        private static readonly byte[] ASF_HEADER_EXTENSION_GUID = {
            0xB5, 0x03, 0xBF, 0x5F, 0x2E, 0xA9, 0xCF, 0x11,
            0x8E, 0xE3, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
        };

        private static readonly byte[] ASF_EXTENDED_STREAM_PROPERTIES_GUID = {
            0xCB, 0xA5, 0xE6, 0x14, 0x72, 0xC6, 0x32, 0x43,
            0x83, 0x99, 0xA9, 0x69, 0x52, 0x06, 0x5B, 0x5A
        };

        #endregion

        public WmvFormat()
        {
            Extensions = new string[] { "wmv", "asf", "wma" };
        }

        public override VideoData Read(IBinaryStream file, VideoMetaData info)
        {
            file.Position = 0;

            byte[] guid = file.ReadBytes(16);
            if (!CompareGuid(guid, ASF_HEADER_GUID))
                return null;

            if (File.Exists(info.FileName))
            {
                file.Dispose();
                return new VideoData(info);
            }

            return new VideoData(file.AsStream, info, true);
        }

        public override VideoMetaData ReadMetaData(IBinaryStream file)
        {
            if (file.Length < 30)
                return null;

            file.Position = 0;

            byte[] guid = file.ReadBytes(16);
            if (!CompareGuid(guid, ASF_HEADER_GUID))
                return null;

            ulong headerSize = file.ReadUInt64();
            uint headerObjectCount = file.ReadUInt32();
            file.Position += 2; // Skip reserved bytes

            var meta = new VideoMetaData
            {
                Width           = 0,
                Height          = 0,
                Duration        = 0,
                FrameRate       = 0,
                BitRate         = 0,
                Codec           = "Unknown",
                AudioCodec      = null,
                CommonExtension = "wmv",
                HasAudio        = false
            };

            try
            {
                long headerEnd = 30 + (long)headerSize;
                ParseHeaderObjects(file, headerEnd, meta);
            }
            catch { }

            return meta;
        }

        private void ParseHeaderObjects(IBinaryStream file, long headerEnd, VideoMetaData meta)
        {
            while (file.Position < headerEnd && file.Position < file.Length)
            {
                long objectStart = file.Position;

                if (file.Position + 24 > file.Length)
                    break;

                byte[] objectGuid = file.ReadBytes(16);
                ulong objectSize = file.ReadUInt64();

                if (objectSize < 24 || objectStart + (long)objectSize > file.Length)
                    break; // Invalid object

                if (CompareGuid(objectGuid, ASF_FILE_PROPERTIES_GUID))
                {
                    ParseFileProperties(file, meta);
                }
                else if (CompareGuid(objectGuid, ASF_STREAM_PROPERTIES_GUID))
                {
                    ParseStreamProperties(file, objectStart + (long)objectSize, meta);
                }
                else if (CompareGuid(objectGuid, ASF_CODEC_LIST_GUID))
                {
                    ParseCodecList(file, objectStart + (long)objectSize, meta);
                }
                else if (CompareGuid(objectGuid, ASF_HEADER_EXTENSION_GUID))
                {
                    ParseHeaderExtension(file, objectStart + (long)objectSize, meta);
                }

                // Move to next object
                file.Position = objectStart + (long)objectSize;
            }
        }

        private void ParseFileProperties(IBinaryStream file, VideoMetaData meta)
        {
            file.Position    += 16; // Skip file ID GUID
            ulong fileSize    = file.ReadUInt64();
            file.Position    += 8; // Skip creation date
            ulong dataPackets = file.ReadUInt64();
            ulong playDuration = file.ReadUInt64();
            ulong sendDuration = file.ReadUInt64();
            ulong preroll      = file.ReadUInt64();
            uint flags         = file.ReadUInt32();
            uint minPacketSize = file.ReadUInt32();
            uint maxPacketSize = file.ReadUInt32();
            uint maxBitrate    = file.ReadUInt32();

            if (playDuration > 0)
            {
                meta.Duration = (long)(playDuration / 10000); // 100-nanosecond units to milliseconds
            }

            if (maxBitrate > 0)
            {
                meta.BitRate = (int)maxBitrate;
            }
        }

        private void ParseStreamProperties(IBinaryStream file, long objectEnd, VideoMetaData meta)
        {
            long startPos = file.Position;

            byte[] streamTypeGuid = file.ReadBytes(16);
            file.Position += 16; // Skip error correction type GUID
            file.Position += 8;  // Skip time offset
            uint typeSpecificDataLength    = file.ReadUInt32();
            uint errorCorrectionDataLength = file.ReadUInt32();
            ushort flags      = file.ReadUInt16();
            byte streamNumber = (byte)(flags & 0x7F);
            file.Position += 4; // Skip reserved

            if (CompareGuid(streamTypeGuid, ASF_VIDEO_MEDIA_GUID))
            {
                if (typeSpecificDataLength >= 40)
                {
                    ParseVideoStreamInfo(file, typeSpecificDataLength, meta);
                }
            }
            else if (CompareGuid(streamTypeGuid, ASF_AUDIO_MEDIA_GUID))
            {
                meta.HasAudio = true;
                if (typeSpecificDataLength >= 18)
                {
                    ParseAudioStreamInfo(file, typeSpecificDataLength, meta);
                }
            }
        }

        private void ParseVideoStreamInfo(IBinaryStream file, uint dataLength, VideoMetaData meta)
        {
            long startPos = file.Position;

            // Read video info header structure
            uint videoInfoHeaderSize = file.ReadUInt32();
            if (videoInfoHeaderSize < 11)
            {
                file.Position = startPos + dataLength;
                return;
            }

            uint width = file.ReadUInt32();
            uint height = file.ReadUInt32();
            byte reserved = file.ReadUInt8();
            ushort formatDataSize = file.ReadUInt16();

            if (formatDataSize < 40)
            {
                file.Position = startPos + dataLength;
                return;
            }

            // BITMAPINFOHEADER structure
            uint biSize = file.ReadUInt32();
            int biWidth = file.ReadInt32();
            int biHeight = file.ReadInt32();
            ushort biPlanes = file.ReadUInt16();
            ushort biBitCount = file.ReadUInt16();
            uint biCompression = file.ReadUInt32();

            if (biWidth > 0 && biHeight > 0)
            {
                meta.Width = (uint)biWidth;
                meta.Height = (uint)Math.Abs(biHeight);
            }
            else if (width > 0 && height > 0)
            {
                meta.Width = width;
                meta.Height = height;
            }

            string fourcc = GetFourCC(biCompression);
            meta.Codec = GetCodecName(fourcc);

            // Move to end of this section
            file.Position = startPos + dataLength;
        }

        private void ParseAudioStreamInfo(IBinaryStream file, uint dataLength, VideoMetaData meta)
        {
            long startPos = file.Position;

            // WAVEFORMATEX structure
            ushort wFormatTag = file.ReadUInt16();
            ushort nChannels = file.ReadUInt16();
            uint nSamplesPerSec = file.ReadUInt32();
            uint nAvgBytesPerSec = file.ReadUInt32();
            ushort nBlockAlign = file.ReadUInt16();
            ushort wBitsPerSample = file.ReadUInt16();

            switch (wFormatTag)
            {
                case 0x0001: meta.AudioCodec = "PCM"; break;
                case 0x0161: meta.AudioCodec = "WMA"; break;
                case 0x0162: meta.AudioCodec = "WMA Pro"; break;
                case 0x0163: meta.AudioCodec = "WMA Lossless"; break;
                case 0x0055: meta.AudioCodec = "MP3"; break;
                default: meta.AudioCodec = $"Audio 0x{wFormatTag:X4}"; break;
            }

            // Move to end of this section
            file.Position = startPos + dataLength;
        }

        private void ParseCodecList(IBinaryStream file, long objectEnd, VideoMetaData meta)
        {
            long startPos = file.Position;

            file.Position += 16; // Skip reserved GUID
            uint codecEntryCount = file.ReadUInt32();

            for (uint i = 0; i < codecEntryCount && file.Position < objectEnd; i++)
            {
                ushort codecType = file.ReadUInt16(); // 1 = video, 2 = audio
                ushort codecNameLength = file.ReadUInt16(); // in wide characters

                string codecName = "";
                if (codecNameLength > 0 && file.Position + codecNameLength * 2 <= objectEnd)
                {
                    byte[] nameBytes = file.ReadBytes(codecNameLength * 2);
                    codecName = Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');
                }

                // Codec description length (in wide characters)
                ushort codecDescLength = file.ReadUInt16();
                // Skip codec description
                if (codecDescLength > 0)
                {
                    file.Position += codecDescLength * 2;
                }

                // Codec information length (in bytes)
                ushort codecInfoLength = file.ReadUInt16();
                // Skip codec information
                if (codecInfoLength > 0)
                {
                    file.Position += codecInfoLength;
                }

                if (!string.IsNullOrEmpty(codecName))
                {
                    if (codecType == 1 && (meta.Codec == "Unknown" || meta.Codec.Length == 4))
                    {
                        meta.Codec = codecName;
                    }
                    else if (codecType == 2 && string.IsNullOrEmpty(meta.AudioCodec))
                    {
                        meta.AudioCodec = codecName;
                    }
                }
            }
        }

        private void ParseHeaderExtension(IBinaryStream file, long objectEnd, VideoMetaData meta)
        {
            long startPos = file.Position;

            file.Position += 16; // Skip reserved field 1 (GUID)
            file.Position += 2;  // Skip reserved field 2
            uint extensionDataSize = file.ReadUInt32();

            long extensionEnd = file.Position + extensionDataSize;
            if (extensionEnd > objectEnd)
                extensionEnd = objectEnd;

            // Parse extension objects
            while (file.Position < extensionEnd && file.Position < file.Length)
            {
                long extObjectStart = file.Position;

                if (file.Position + 24 > extensionEnd)
                    break;

                byte[] extObjectGuid = file.ReadBytes(16);
                ulong extObjectSize = file.ReadUInt64();

                if (extObjectSize < 24 || extObjectStart + (long)extObjectSize > extensionEnd)
                    break;

                if (CompareGuid(extObjectGuid, ASF_EXTENDED_STREAM_PROPERTIES_GUID))
                {
                    ParseExtendedStreamProperties(file, extObjectStart + (long)extObjectSize, meta);
                }

                file.Position = extObjectStart + (long)extObjectSize;
            }
        }

        private void ParseExtendedStreamProperties(IBinaryStream file, long objectEnd, VideoMetaData meta)
        {
            file.Position += 8; // Skip start time
            file.Position += 8; // Skip end time
            uint dataBitrate = file.ReadUInt32();
            uint bufferSize = file.ReadUInt32();
            uint initialBufferFullness = file.ReadUInt32();
            uint alternateBitrate = file.ReadUInt32();

            if (dataBitrate > 0 && meta.BitRate == 0)
            {
                meta.BitRate = (int)dataBitrate;
            }

            file.Position += 4; // Skip alternate buffer size
            file.Position += 4; // Skip alternate initial buffer fullness
            file.Position += 4; // Skip maximum object size
            file.Position += 4; // Skip flags
            file.Position += 2; // Skip stream number
            file.Position += 2; // Skip stream language ID index
            file.Position += 8; // Skip average time per frame

            ushort streamNameCount = file.ReadUInt16();
            ushort payloadExtensionCount = file.ReadUInt16();

            for (int i = 0; i < streamNameCount && file.Position < objectEnd; i++)
            {
                file.Position += 2; // Skip language ID
                ushort nameLength = file.ReadUInt16();
                file.Position += nameLength * 2; // Skip name (UTF-16)
            }
        }

        #region Helper Methods
        private bool CompareGuid(byte[] guid1, byte[] guid2)
        {
            if (guid1.Length != 16 || guid2.Length != 16)
                return false;

            for (int i = 0; i < 16; i++)
            {
                if (guid1[i] != guid2[i])
                    return false;
            }
            return true;
        }

        private string GetFourCC(uint fourcc)
        {
            byte[] bytes = BitConverter.GetBytes(fourcc);
            char[] chars = new char[4];
            for (int i = 0; i < 4; i++)
            {
                chars[i] = (bytes[i] >= 32 && bytes[i] < 127) ? (char)bytes[i] : '?';
            }
            return new string(chars);
        }

        private string GetCodecName(string fourcc)
        {
            switch (fourcc.ToUpperInvariant())
            {
                case "WMV1": return "Windows Media Video 7";
                case "WMV2": return "Windows Media Video 8";
                case "WMV3": return "Windows Media Video 9";
                case "WMVA": return "Windows Media Video 9 Advanced Profile";
                case "WVC1": return "Windows Media Video 9 Advanced Profile (VC-1)";
                case "VC-1": return "VC-1";
                case "WMVP": return "Windows Media Video 9 Image";
                case "WVP2": return "Windows Media Video 9 Image v2";
                case "MP43": return "Microsoft MPEG-4 v3";
                case "MP42": return "Microsoft MPEG-4 v2";
                case "MP4S": return "Microsoft MPEG-4 v1";
                case "M4S2": return "Microsoft MPEG-4 v1";
                case "MPG4": return "Microsoft MPEG-4 v1";
                case "MSS1": return "Windows Screen Video";
                case "MSS2": return "Windows Media Video 9 Screen";
                case "MJPG": return "Motion JPEG";
                case "DIVX": return "DivX";
                case "XVID": return "Xvid";
                case "H264": return "H.264";
                case "AVC1": return "H.264/AVC";
                case "HEVC": return "H.265/HEVC";
                case "VP80": return "VP8";
                case "VP90": return "VP9";
                default: 
                    if (string.IsNullOrEmpty(fourcc) || fourcc.Contains("?"))
                        return "Unknown";
                    return fourcc;
            }
        }
        #endregion
    }
}