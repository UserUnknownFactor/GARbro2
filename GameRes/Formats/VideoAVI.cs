using System.IO;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using GameRes.Utility;
using System.Windows.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using GameRes.Collections;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using GameRes.Compression;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace GameRes
{

    [Export(typeof(VideoFormat))]
    public class AviFormat : VideoFormat
    {
        public override string         Tag { get { return "AVI"; } }
        public override string Description { get { return "Audio Video Interleave"; } }
        public override uint     Signature { get { return 0x46464952; } } // "RIFF"
        public override bool      CanWrite { get { return false; } }

        // AVI chunk IDs
        private static readonly uint RIFF_ID = 0x46464952; // "RIFF"
        private static readonly uint AVI_ID  = 0x20495641;  // "AVI "
        private static readonly uint LIST_ID = 0x5453494C; // "LIST"
        private static readonly uint HDRL_ID = 0x6C726468; // "hdrl"
        private static readonly uint AVIH_ID = 0x68697661; // "avih"
        private static readonly uint STRL_ID = 0x6C727473; // "strl"
        private static readonly uint STRH_ID = 0x68727473; // "strh"
        private static readonly uint STRF_ID = 0x66727473; // "strf"
        private static readonly uint VIDS_ID = 0x73646976; // "vids"
        private static readonly uint AUDS_ID = 0x73647561; // "auds"
        //private static readonly uint MOVI_ID = 0x69766F6D; // "movi"
        //private static readonly uint JUNK_ID = 0x4B4E554A; // "JUNK"

        public override VideoData Read (IBinaryStream file, VideoMetaData info)
        {
            info.CommonExtension = "avi";

            if (File.Exists (info.FileName)) {
                // real file
                file.Dispose();
                return new VideoData (info);
            }

            return new VideoData (file.AsStream, info, true);
        }

        public override VideoMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < 12)
                return null;

            file.Position = 0;
            uint riffId = file.ReadUInt32();
            uint riffSize = file.ReadUInt32();
            uint aviId = file.ReadUInt32();

            if (riffId != RIFF_ID || aviId != AVI_ID)
                return null;

            var meta = new VideoMetaData
            {
                Width = 0,
                Height = 0,
                Duration = 0,
                FrameRate = 0,
                Codec = "Unknown",
                CommonExtension = "avi",
                HasAudio = false
            };

            try
            {
                ParseAviChunks (file, meta);
            }
            catch (Exception)
            {
                // If parsing fails, return basic metadata
            }

            return meta;
        }

        private void ParseAviChunks (IBinaryStream file, VideoMetaData meta)
        {
            file.Position = 12; // Skip RIFF header and AVI ID

            while (file.Position < file.Length - 8)
            {
                uint chunkId = file.ReadUInt32();
                uint chunkSize = file.ReadUInt32();
                long nextChunkPos = file.Position + chunkSize;

                // Ensure chunk size is even (AVI padding rule)
                if (chunkSize % 2 != 0)
                    nextChunkPos++;

                if (nextChunkPos > file.Length)
                    break;

                if (chunkId == LIST_ID)
                {
                    uint listType = file.ReadUInt32();

                    if (listType == HDRL_ID)
                    {
                        ParseHdrlList (file, file.Position, nextChunkPos - 4, meta);
                    }
                    else if (listType == STRL_ID)
                    {
                        ParseStrlList (file, file.Position, nextChunkPos - 4, meta);
                    }
                    else
                    {
                        // Skip other LIST types
                        file.Position = nextChunkPos;
                    }
                }
                else
                {
                    // Skip other chunks
                    file.Position = nextChunkPos;
                }
            }
        }

        private void ParseHdrlList (IBinaryStream file, long startPos, long endPos, VideoMetaData meta)
        {
            file.Position = startPos;

            while (file.Position < endPos - 8)
            {
                uint chunkId = file.ReadUInt32();
                uint chunkSize = file.ReadUInt32();
                long nextChunkPos = file.Position + chunkSize;

                // Ensure chunk size is even (AVI padding rule)
                if (chunkSize % 2 != 0)
                    nextChunkPos++;

                if (nextChunkPos > endPos)
                    break;

                if (chunkId == AVIH_ID)
                {
                    ParseAvihChunk (file, chunkSize, meta);
                }
                else if (chunkId == LIST_ID)
                {
                    uint listType = file.ReadUInt32();

                    if (listType == STRL_ID)
                    {
                        ParseStrlList (file, file.Position, nextChunkPos - 4, meta);
                    }
                    else
                    {
                        file.Position = nextChunkPos;
                    }
                }
                else
                {
                    file.Position = nextChunkPos;
                }
            }
        }

        private void ParseAvihChunk (IBinaryStream file, uint chunkSize, VideoMetaData meta)
        {
            if (chunkSize < 40)
            {
                file.Position += chunkSize;
                return;
            }

            // AVI Header structure
            uint microSecPerFrame = file.ReadUInt32(); // microseconds per frame
            file.Position += 12; // Skip maxBytesPerSec, paddingGranularity, flags
            uint totalFrames = file.ReadUInt32(); // total frames
            file.Position += 16; // Skip initialFrames, streams, suggestedBufferSize, width, height

            // Calculate duration and frame rate
            if (microSecPerFrame > 0)
            {
                meta.Duration = (long)totalFrames * microSecPerFrame / 1000; // Convert to milliseconds
                meta.FrameRate = 1000000.0 / microSecPerFrame;
            }
        }

        private void ParseStrlList (IBinaryStream file, long startPos, long endPos, VideoMetaData meta)
        {
            file.Position = startPos;
            bool isVideoStream = false;

            while (file.Position < endPos - 8)
            {
                uint chunkId = file.ReadUInt32();
                uint chunkSize = file.ReadUInt32();
                long nextChunkPos = file.Position + chunkSize;

                // Ensure chunk size is even (AVI padding rule)
                if (chunkSize % 2 != 0)
                    nextChunkPos++;

                if (nextChunkPos > endPos)
                    break;

                if (chunkId == STRH_ID)
                {
                    // Stream header
                    uint streamType = file.ReadUInt32();
                    isVideoStream = (streamType == VIDS_ID);

                    if (streamType == AUDS_ID)
                    {
                        meta.HasAudio = true;
                    }

                    file.Position = nextChunkPos;
                }
                else if (chunkId == STRF_ID && isVideoStream)
                {
                    // Stream format - for video, this is a BITMAPINFOHEADER
                    ParseBitmapInfoHeader (file, chunkSize, meta);
                }
                else
                {
                    file.Position = nextChunkPos;
                }
            }
        }

        private void ParseBitmapInfoHeader (IBinaryStream file, uint chunkSize, VideoMetaData meta)
        {
            if (chunkSize < 40)
            {
                file.Position += chunkSize;
                return;
            }

            // BITMAPINFOHEADER structure
            uint headerSize = file.ReadUInt32();
            int width = file.ReadInt32();
            int height = file.ReadInt32();
            file.Position += 2; // Skip planes
            ushort bitCount = file.ReadUInt16();
            uint compression = file.ReadUInt32();

            // Set metadata
            meta.Width = (uint)Math.Abs (width);
            meta.Height = (uint)Math.Abs (height);

            // Map compression FourCC to codec name
            meta.Codec = GetCodecName (compression);
        }

        private string GetCodecName (uint fourCC)
        {
            byte[] bytes = BitConverter.GetBytes (fourCC);
            string fourCCString = Encoding.ASCII.GetString (bytes);

            switch (fourCCString)
            {
                case "DIB ": return "Uncompressed";
                case "MJPG": return "Motion JPEG";
                case "XVID": return "Xvid";
                case "DX50": return "DivX 5";
                case "DIVX": return "DivX";
                case "H264": return "H.264";
                case "AVC1": return "H.264";
                case "X264": return "H.264";
                case "HEVC": return "H.265";
                case "HVC1": return "H.265";
                case "VP80": return "VP8";
                case "VP90": return "VP9";
                case "AV01": return "AV1";
                default: return fourCCString.Trim();
            }
        }
    }
 }