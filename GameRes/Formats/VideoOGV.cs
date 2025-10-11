using System;
using System.IO;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Collections.Generic;

namespace GameRes
{
    [Export(typeof(VideoFormat))]
    public class OgvFormat : VideoFormat
    {
        public override string         Tag { get { return "OGV"; } }
        public override string Description { get { return "Ogg Video"; } }
        public override uint     Signature { get { return OGG_PAGE_SIGNATURE; } } // "OggS"
        public override bool      CanWrite { get { return false; } }

        // Ogg page header constants
        private const uint OGG_PAGE_SIGNATURE = 0x5367674F; // "OggS"
        private const byte OGG_VERSION = 0;

        // Codec identifiers
        private const string THEORA_MAGIC = "\x80theora";
        private const string VORBIS_MAGIC = "\x01vorbis";
        private const string OPUS_MAGIC = "OpusHead";
        private const string VP8_MAGIC = "VP80";
        private const string DAALA_MAGIC = "\x80daala";
        private const string DIRAC_MAGIC = "BBCD";
        private const string OGM_VIDEO_MAGIC = "\x01video";
        private const string FLAC_MAGIC = "\x7FFLAC";
        private const string SPEEX_MAGIC = "Speex   ";

        public OgvFormat()
        {
            Extensions = new string[] { "ogv", "ogm" };
        }

        public override VideoData Read(IBinaryStream file, VideoMetaData info)
        {
            if (File.Exists (info.FileName) &&
                Extensions.Any (ext => string.Equals (ext,
                    VFS.GetExtension (info.FileName, true), StringComparison.OrdinalIgnoreCase)))
            {
                // Real file
                file.Dispose();
                return new VideoData(info);
            }

            return new VideoData(file.AsStream, info, true);
        }

        public override VideoMetaData ReadMetaData(IBinaryStream file)
        {
            if (file.Length < 27)
                return null;

            file.Position = 0;
            uint signature = file.ReadUInt32();
            if (signature != OGG_PAGE_SIGNATURE)
                return null;

            if (!ContainsVideoStream(file))
                return null;

            var meta = new VideoMetaData
            {
                Width = 0,
                Height = 0,
                Duration = 0,
                FrameRate = 0,
                BitRate = 0,
                Codec = "Unknown",
                AudioCodec = null,
                CommonExtension = "ogv",
                HasAudio = false
            };

            try
            {
                file.Position = 0;
                ParseOggStreams(file, meta);
            }
            catch { }

            //if (meta.Width == 0 || meta.Height == 0 || meta.Codec == "Unknown") return null;

            return meta;
        }

        private bool ContainsVideoStream(IBinaryStream file)
        {
            file.Position = 0;
            int pagesChecked = 0;
            const int maxPagesToCheck = 10;

            while (file.Position < file.Length && pagesChecked < maxPagesToCheck)
            {
                var page = ReadOggPageQuick(file);
                if (page == null)
                    break;

                // Check BOS (beginning of stream) pages for video codecs
                if ((page.HeaderType & 0x02) != 0)
                {
                    if (IsVideoCodec(page.Data))
                        return true;
                }

                pagesChecked++;
            }

            return false;
        }

        private bool IsVideoCodec (byte[] data)
        {
            if (data == null || data.Length < 4)
                return false;

            if (data.Length >= 7)
            {
                if (data[0] == 0x80 && data[1] == 't' && data[2] == 'h' &&
                    data[3] == 'e' && data[4] == 'o' && data[5] == 'r' && data[6] == 'a')
                    return true;

                if (data[0] == 0x01 && data[1] == 'v' && data[2] == 'i' &&
                    data[3] == 'd' && data[4] == 'e' && data[5] == 'o')
                    return true;
            }

            if (data.Length >= 6)
            {
                if (data[0] == 0x80 && data[1] == 'd' && data[2] == 'a' &&
                    data[3] == 'a' && data[4] == 'l' && data[5] == 'a')
                    return true;
            }

            // Check for VP8 and Dirac (these are safe to check as strings since they're all ASCII)
            if (data.Length >= 4)
            {
                string magic4 = Encoding.ASCII.GetString (data, 0, 4);
                if (magic4 == VP8_MAGIC || magic4 == DIRAC_MAGIC)
                    return true;
            }

            return false;
        }

        private bool IsAudioCodec (byte[] data)
        {
            if (data == null || data.Length < 4)
                return false;

            if (data.Length >= 7)
            {
                if (data[0] == 0x01 && data[1] == 'v' && data[2] == 'o' &&
                    data[3] == 'r' && data[4] == 'b' && data[5] == 'i' && data[6] == 's')
                    return true;
            }

            if (data.Length >= 5)
            {
                if (data[0] == 0x7F && data[1] == 'F' && data[2] == 'L' &&
                    data[3] == 'A' && data[4] == 'C')
                    return true;
            }

            if (data.Length >= 8)
            {
                string magic8 = Encoding.ASCII.GetString (data, 0, 8);
                if (magic8 == OPUS_MAGIC || magic8 == SPEEX_MAGIC)
                    return true;
            }

            return false;
        }

        private OggPage ReadOggPageQuick(IBinaryStream file)
        {
            if (file.Position + 27 > file.Length)
                return null;

            long startPos = file.Position;

            uint signature = file.ReadUInt32();
            if (signature != OGG_PAGE_SIGNATURE)
            {
                // Try to resync by looking for next OggS signature
                if (!ResyncToNextPage(file))
                    return null;

                signature = file.ReadUInt32();
                if (signature != OGG_PAGE_SIGNATURE)
                    return null;
            }

            byte version = file.ReadUInt8();
            if (version != OGG_VERSION)
                return null;

            var page = new OggPage();
            page.HeaderType = file.ReadUInt8();
            page.GranulePosition = file.ReadInt64();
            page.Serial = file.ReadUInt32();
            page.SequenceNumber = file.ReadUInt32();
            page.Checksum = file.ReadUInt32();
            byte segmentCount = file.ReadUInt8();

            if (file.Position + segmentCount > file.Length)
                return null;

            // Read segment table
            int dataSize = 0;
            byte[] segmentTable = file.ReadBytes(segmentCount);
            foreach (byte segSize in segmentTable)
            {
                dataSize += segSize;
            }

            if (file.Position + dataSize > file.Length)
                return null;

            // For quick check, only read first 64 bytes of data to identify codec
            int readSize = Math.Min(dataSize, 64);
            page.Data = file.ReadBytes(readSize);

            if (dataSize > readSize)
                file.Position += dataSize - readSize;

            return page;
        }

        private bool ResyncToNextPage(IBinaryStream file)
        {
            // Look for next OggS signature (up to 64KB ahead)
            const int maxSearchBytes = 65536;
            long searchEnd = Math.Min(file.Position + maxSearchBytes, file.Length - 27);

            while (file.Position < searchEnd)
            {
                if (file.ReadUInt8() == 'O')
                {
                    if (file.Position + 3 <= file.Length)
                    {
                        byte[] next3 = file.ReadBytes(3);
                        if (next3[0] == 'g' && next3[1] == 'g' && next3[2] == 'S')
                        {
                            file.Position -= 4; // Back to start of "OggS"
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void ParseOggStreams(IBinaryStream file, VideoMetaData meta)
        {
            file.Position = 0;
            var streamSerials = new Dictionary<uint, StreamInfo>();

            while (file.Position < file.Length)
            {
                var page = ReadOggPage(file);
                if (page == null)
                    break;

                // First page of a stream contains codec identification
                if ((page.HeaderType & 0x02) != 0) // BOS (beginning of stream) flag
                {
                    var streamInfo = IdentifyStream(page.Data);
                    if (streamInfo != null)
                    {
                        streamInfo.Serial = page.Serial;
                        streamSerials[page.Serial] = streamInfo;

                        if (streamInfo.Type == StreamType.Video)
                        {
                            ParseVideoHeaders(page.Data, streamInfo, meta);
                        }
                        else if (streamInfo.Type == StreamType.Audio)
                        {
                            meta.HasAudio = true;
                            if (string.IsNullOrEmpty(meta.AudioCodec))
                            {
                                meta.AudioCodec = streamInfo.CodecName;
                            }
                        }
                    }
                }
                else if (streamSerials.ContainsKey(page.Serial))
                {
                    var streamInfo = streamSerials[page.Serial];
                    if (streamInfo.Type == StreamType.Video && !streamInfo.HeadersParsed)
                    {
                        ParseVideoHeaders(page.Data, streamInfo, meta);
                    }
                }

                if ((page.HeaderType & 0x04) != 0) // EOS flag
                {
                    if (streamSerials.ContainsKey(page.Serial))
                    {
                        var streamInfo = streamSerials[page.Serial];
                        if (streamInfo.Type == StreamType.Video && page.GranulePosition > 0)
                            CalculateDuration(page.GranulePosition, streamInfo, meta);
                    }
                }
            }
        }

        private OggPage ReadOggPage(IBinaryStream file)
        {
            if (file.Position + 27 > file.Length)
                return null;

            long startPos = file.Position;

            uint signature = file.ReadUInt32();
            if (signature != OGG_PAGE_SIGNATURE)
                return null;

            byte version = file.ReadUInt8();
            if (version != OGG_VERSION)
                return null;

            var page = new OggPage();
            page.HeaderType = file.ReadUInt8();
            page.GranulePosition = file.ReadInt64();
            page.Serial = file.ReadUInt32();
            page.SequenceNumber = file.ReadUInt32();
            page.Checksum = file.ReadUInt32();
            byte segmentCount = file.ReadUInt8();

            if (file.Position + segmentCount > file.Length)
                return null;

            // Read segment table
            int dataSize = 0;
            byte[] segmentTable = file.ReadBytes(segmentCount);
            foreach (byte segSize in segmentTable)
            {
                dataSize += segSize;
            }

            if (file.Position + dataSize > file.Length)
                return null;

            page.Data = file.ReadBytes(dataSize);
            return page;
        }

        private StreamInfo IdentifyStream(byte[] data)
        {
            if (data == null || data.Length < 4)
                return null;

            var info = new StreamInfo();
            if (IsVideoCodec(data))
            {
                info.Type = StreamType.Video;

                if (data.Length >= 7)
                {
                    string magic7 = Encoding.ASCII.GetString(data, 0, 7);
                    if (magic7 == THEORA_MAGIC)
                        info.CodecName = "Theora";
                    else if (magic7 == OGM_VIDEO_MAGIC)
                        info.CodecName = "OGM Video";
                }

                if (info.CodecName == null && data.Length >= 6)
                {
                    string magic6 = Encoding.ASCII.GetString(data, 0, 6);
                    if (magic6 == DAALA_MAGIC)
                        info.CodecName = "Daala";
                }

                if (info.CodecName == null && data.Length >= 4)
                {
                    string magic4 = Encoding.ASCII.GetString(data, 0, 4);
                    if (magic4 == VP8_MAGIC)
                        info.CodecName = "VP8";
                    else if (magic4 == DIRAC_MAGIC)
                        info.CodecName = "Dirac";
                }

                return info;
            }

            if (IsAudioCodec(data))
            {
                info.Type = StreamType.Audio;

                if (data.Length >= 8)
                {
                    string magic8 = Encoding.ASCII.GetString(data, 0, 8);
                    if (magic8 == OPUS_MAGIC)
                        info.CodecName = "Opus";
                    else if (magic8 == SPEEX_MAGIC)
                        info.CodecName = "Speex";
                }

                if (info.CodecName == null && data.Length >= 7)
                {
                    string magic7 = Encoding.ASCII.GetString(data, 0, 7);
                    if (magic7 == VORBIS_MAGIC)
                        info.CodecName = "Vorbis";
                }

                if (info.CodecName == null && data.Length >= 5)
                {
                    string magic5 = Encoding.ASCII.GetString(data, 0, 5);
                    if (magic5 == FLAC_MAGIC)
                        info.CodecName = "FLAC";
                }

                return info;
            }

            return null;
        }

        private void ParseVideoHeaders(byte[] data, StreamInfo streamInfo, VideoMetaData meta)
        {
            if (streamInfo.HeadersParsed)
                return;

            if (streamInfo.CodecName == "Theora")
            {
                ParseTheoraHeader(data, meta);
                streamInfo.HeadersParsed = true;
            }
            else if (streamInfo.CodecName == "VP8")
            {
                ParseVP8Header(data, meta);
                streamInfo.HeadersParsed = true;
            }
            else if (streamInfo.CodecName == "Daala")
            {
                ParseDaalaHeader(data, meta);
                streamInfo.HeadersParsed = true;
            }
            else if (streamInfo.CodecName == "OGM Video")
            {
                ParseOGMHeader(data, meta);
                streamInfo.HeadersParsed = true;
            }

            if (!string.IsNullOrEmpty(streamInfo.CodecName) && meta.Codec == "Unknown")
            {
                meta.Codec = streamInfo.CodecName;
            }
        }

        private void ParseTheoraHeader (byte[] data, VideoMetaData meta)
        {
            if (data.Length < 42)
                return;

            // Skip magic string
            int pos = 7;

            byte versionMajor = data[pos++];
            byte versionMinor = data[pos++];
            byte versionRevision = data[pos++];

            uint frameWidthMB = (uint)((data[pos] << 8) | data[pos + 1]);
            pos += 2;
            uint frameHeightMB = (uint)((data[pos] << 8) | data[pos + 1]);
            pos += 2;

            uint pictureWidth = (uint)((data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2]);
            pos += 3;
            uint pictureHeight = (uint)((data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2]);
            pos += 3;

            // Picture offset (8 bits each)
            byte pictureX = data[pos++];
            byte pictureY = data[pos++];

            uint framerateNumerator = (uint)((data[pos] << 24) | (data[pos + 1] << 16) |
                                             (data[pos + 2] << 8) | data[pos + 3]);
            pos += 4;
            uint framerateDenominator = (uint)((data[pos] << 24) | (data[pos + 1] << 16) |
                                               (data[pos + 2] << 8) | data[pos + 3]);
            pos += 4;

            uint aspectNumerator = (uint)((data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2]);
            pos += 3;
            uint aspectDenominator = (uint)((data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2]);
            pos += 3;

            byte colorSpace = data[pos++];

            uint nominalBitrate = (uint)((data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2]);
            pos += 3;

            byte qualityHint = (byte)(data[pos] & 0x3F);

            // Set metadata
            if (pictureWidth > 0 && pictureHeight > 0)
            {
                meta.Width = pictureWidth;
                meta.Height = pictureHeight;
            }
            else if (frameWidthMB > 0 && frameHeightMB > 0)
            {
                meta.Width = frameWidthMB * 16;
                meta.Height = frameHeightMB * 16;
            }

            if (framerateDenominator > 0)
                meta.FrameRate = (double)framerateNumerator / framerateDenominator;

            if (nominalBitrate > 0)
                meta.BitRate = (int)nominalBitrate;
        }

        private void ParseVP8Header(byte[] data, VideoMetaData meta)
        {
            // VP8 in Ogg is less common, basic parsing
            if (data.Length < 26)
                return;

            int pos = 4; // Skip "VP80"

            // Parse VP8 header (simplified)
            if (data.Length > pos + 14)
            {
                // Width and height are typically at fixed positions in VP8 stream
                uint width = (uint)(data[pos + 6] | (data[pos + 7] << 8)) & 0x3FFF;
                uint height = (uint)(data[pos + 8] | (data[pos + 9] << 8)) & 0x3FFF;

                if (width > 0 && height > 0)
                {
                    meta.Width = width;
                    meta.Height = height;
                }
            }
        }

        private void ParseDaalaHeader(byte[] data, VideoMetaData meta)
        {
            // Daala is experimental, basic parsing
            if (data.Length < 42)
                return;

            int pos = 6; // Skip magic

            // Similar structure to Theora
            byte versionMajor = data[pos++];
            byte versionMinor = data[pos++];
            byte versionRevision = data[pos++];

            if (data.Length > pos + 8)
            {
                uint width = (uint)(data[pos] | (data[pos + 1] << 8) | 
                                   (data[pos + 2] << 16) | (data[pos + 3] << 24));
                pos += 4;
                uint height = (uint)(data[pos] | (data[pos + 1] << 8) | 
                                    (data[pos + 2] << 16) | (data[pos + 3] << 24));

                if (width > 0 && height > 0)
                {
                    meta.Width = width;
                    meta.Height = height;
                }
            }
        }

        private void ParseOGMHeader(byte[] data, VideoMetaData meta)
        {
            if (data.Length < 52)
                return;

            int pos = 7; // Skip "\x01video"

            if (data.Length > pos + 40)
            {
                // Skip stream header GUID
                pos += 16;

                // Video format header size
                uint headerSize = (uint)(data[pos] | (data[pos + 1] << 8) | 
                                        (data[pos + 2] << 16) | (data[pos + 3] << 24));
                pos += 4;

                if (headerSize >= 40 && data.Length >= pos + 40)
                {
                    // BITMAPINFOHEADER structure
                    pos += 4; // Skip biSize
                    int width = data[pos] | (data[pos + 1] << 8) | 
                               (data[pos + 2] << 16) | (data[pos + 3] << 24);
                    pos += 4;
                    int height = data[pos] | (data[pos + 1] << 8) | 
                                (data[pos + 2] << 16) | (data[pos + 3] << 24);

                    if (width > 0 && height > 0)
                    {
                        meta.Width = (uint)Math.Abs(width);
                        meta.Height = (uint)Math.Abs(height);
                    }
                }
            }
        }

        private void CalculateDuration(long granulePosition, StreamInfo streamInfo, VideoMetaData meta)
        {
            if (streamInfo.CodecName == "Theora" && meta.FrameRate > 0)
            {
                // Theora granule position encodes frame count
                long frameCount = granulePosition >> 6; // Upper bits are frame count
                meta.Duration = (long)(frameCount * 1000.0 / meta.FrameRate);
            }
        }

        #region Helper Classes
        private class OggPage
        {
            public byte HeaderType { get; set; }
            public long GranulePosition { get; set; }
            public uint Serial { get; set; }
            public uint SequenceNumber { get; set; }
            public uint Checksum { get; set; }
            public byte[] Data { get; set; }
        }

        private enum StreamType
        {
            Unknown,
            Video,
            Audio
        }

        private class StreamInfo
        {
            public StreamType Type { get; set; }
            public string CodecName { get; set; }
            public uint Serial { get; set; }
            public bool HeadersParsed { get; set; }
        }
        #endregion
    }
}