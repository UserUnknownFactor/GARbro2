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
    public class MkvFormat : VideoFormat
    {
        public override string Tag { get { return "MKV"; } }
        public override string Description { get { return "Matroska Video"; } }
        public override uint Signature { get { return 0x1A45DFA3; } } // EBML header (same as WebM)
        public override bool CanWrite { get { return false; } }

        // EBML element IDs - same as WebM but with different DocType
        private static readonly uint EBML_ID = 0x1A45DFA3;
        private static readonly uint DOCTYPE_ID = 0x4282;
        private static readonly uint SEGMENT_ID = 0x18538067;
        private static readonly uint INFO_ID = 0x1549A966;
        private static readonly uint TRACKS_ID = 0x1654AE6B;
        private static readonly ushort TRACK_ENTRY_ID = 0xAE;
        private static readonly ushort TRACK_TYPE_ID = 0x83;
        private static readonly ushort TRACK_VIDEO_ID = 0xE0;
        private static readonly ushort TRACK_AUDIO_ID = 0xE1;
        private static readonly ushort PIXEL_WIDTH_ID = 0xB0;
        private static readonly ushort PIXEL_HEIGHT_ID = 0xBA;
        private static readonly ushort CODEC_ID_ID = 0x86;
        private static readonly uint DURATION_ID = 0x4489;
        private static readonly uint TIMECODE_SCALE_ID = 0x2AD7B1;

        public override VideoData Read (IBinaryStream file, VideoMetaData info)
        {
            if (File.Exists (info.FileName)) {
                // real file
                file.Dispose();
                return new VideoData (info);
            }

            return new VideoData (file.AsStream, info, true);
        }

        public override VideoMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < 8)
                return null;

            file.Position = 0;
            uint signature = file.ReadUInt32();
            if (signature != EBML_ID)
                return null;

            // Check if it's MKV and not WebM
            bool isMkv = false;
            try
            {
                // Skip EBML header size
                ReadVarInt (file);

                // Look for DocType element
                while (file.Position < 100) // Check only the beginning
                {
                    uint id = ReadElementId (file);
                    ulong size = ReadVarInt (file);

                    if (id == DOCTYPE_ID)
                    {
                        byte[] docTypeBytes = file.ReadBytes((int)size);
                        string docType = Encoding.ASCII.GetString (docTypeBytes);

                        // "matroska" for MKV, "webm" for WebM
                        isMkv = docType.Equals("matroska", StringComparison.OrdinalIgnoreCase);
                        break;
                    }
                    else
                    {
                        file.Position += (long)size;
                    }
                }
            }
            catch
            {
                // If we can't determine, assume it's MKV
                isMkv = true;
            }

            if (!isMkv)
                return null;

            var meta = new VideoMetaData
            {
                Width = 0,
                Height = 0,
                Duration = 0,
                FrameRate = 0,
                Codec = "H.264", // Default codec for MKV
                CommonExtension = "mkv",
                HasAudio = false
            };

            try
            {
                // Reset position and parse the file
                file.Position = 0;

                // Skip EBML header
                SkipElement (file);

                // Find Segment element
                if (!FindElement (file, SEGMENT_ID))
                    return meta;

                // Skip size (we'll just read until the end)
                ReadVarInt (file);

                // Parse segment contents
                ParseSegment (file, meta);
            }
            catch (Exception)
            {
                // If parsing fails, return basic metadata
            }

            return meta;
        }

        // The rest of the methods are identical to WebMFormat
        // since MKV and WebM share the same EBML structure

        private void ParseSegment (IBinaryStream file, VideoMetaData meta)
        {
            long endPos = file.Length;

            while (file.Position < endPos)
            {
                uint id = ReadElementId (file);
                ulong size = ReadVarInt (file);
                long nextPos = file.Position + (long)size;

                if (nextPos > endPos)
                    break;

                if (id == INFO_ID)
                {
                    ParseInfo (file, file.Position + (long)size, meta);
                }
                else if (id == TRACKS_ID)
                {
                    ParseTracks (file, file.Position + (long)size, meta);
                }

                file.Position = nextPos;
            }
        }

        private void ParseInfo (IBinaryStream file, long endPos, VideoMetaData meta)
        {
            long timecodeScale = 1000000; // Default: 1ms
            double duration = 0;

            while (file.Position < endPos)
            {
                uint id = ReadElementId (file);
                ulong size = ReadVarInt (file);
                long nextPos = file.Position + (long)size;

                if (nextPos > endPos)
                    break;

                if (id == TIMECODE_SCALE_ID)
                {
                    timecodeScale = ReadUnsignedInt (file, (int)size);
                }
                else if (id == DURATION_ID)
                {
                    duration = ReadFloat (file, (int)size);
                }
                else
                {
                    file.Position = nextPos;
                }
            }

            // Calculate duration in milliseconds
            if (duration > 0)
            {
                meta.Duration = (long)(duration * timecodeScale / 1000000);
            }
        }

        private void ParseTracks (IBinaryStream file, long endPos, VideoMetaData meta)
        {
            while (file.Position < endPos)
            {
                uint id = ReadElementId (file);
                ulong size = ReadVarInt (file);
                long nextPos = file.Position + (long)size;

                if (nextPos > endPos)
                    break;

                if (id == TRACK_ENTRY_ID)
                {
                    ParseTrackEntry (file, nextPos, meta);
                }

                file.Position = nextPos;
            }
        }

        private void ParseTrackEntry (IBinaryStream file, long endPos, VideoMetaData meta)
        {
            byte trackType = 0;
            string codecId = null;

            while (file.Position < endPos)
            {
                uint id = ReadElementId (file);
                ulong size = ReadVarInt (file);
                long nextPos = file.Position + (long)size;

                if (nextPos > endPos)
                    break;

                if (id == TRACK_TYPE_ID)
                {
                    trackType = file.ReadUInt8();
                }
                else if (id == CODEC_ID_ID)
                {
                    byte[] codecBytes = file.ReadBytes((int)size);
                    codecId = Encoding.ASCII.GetString (codecBytes);
                }
                else if (id == TRACK_VIDEO_ID)
                {
                    ParseVideoTrack (file, nextPos, meta);
                }
                else if (id == TRACK_AUDIO_ID)
                {
                    meta.HasAudio = true;
                    file.Position = nextPos;
                }
                else
                {
                    file.Position = nextPos;
                }
            }

            // Track type 1 is video
            if (trackType == 1 && !string.IsNullOrEmpty (codecId))
            {
                // Map codec ID to friendly name
                if (codecId == "V_VP8")
                    meta.Codec = "VP8";
                else if (codecId == "V_VP9")
                    meta.Codec = "VP9";
                else if (codecId == "V_AV1")
                    meta.Codec = "AV1";
                else if (codecId.StartsWith("V_MPEG4/ISO/AVC"))
                    meta.Codec = "H.264";
                else if (codecId.StartsWith("V_MPEGH/ISO/HEVC"))
                    meta.Codec = "H.265";
                else
                    meta.Codec = codecId;
            }
        }

        private void ParseVideoTrack (IBinaryStream file, long endPos, VideoMetaData meta)
        {
            while (file.Position < endPos)
            {
                uint id = ReadElementId (file);
                ulong size = ReadVarInt (file);
                long nextPos = file.Position + (long)size;

                if (nextPos > endPos)
                    break;

                if (id == PIXEL_WIDTH_ID)
                {
                    meta.Width = (uint)ReadUnsignedInt (file, (int)size);
                }
                else if (id == PIXEL_HEIGHT_ID)
                {
                    meta.Height = (uint)ReadUnsignedInt (file, (int)size);
                }
                else
                {
                    file.Position = nextPos;
                }
            }
        }

        private uint ReadElementId (IBinaryStream file)
        {
            byte firstByte = file.ReadUInt8();

            if ((firstByte & 0x80) != 0)
                return firstByte;
            else if ((firstByte & 0x40) != 0)
                return (uint)((firstByte << 8) | file.ReadUInt8());
            else if ((firstByte & 0x20) != 0)
                return (uint)((firstByte << 16) | (file.ReadUInt8() << 8) | file.ReadUInt8());
            else if ((firstByte & 0x10) != 0)
                return (uint)((firstByte << 24) | (file.ReadUInt8() << 16) | (file.ReadUInt8() << 8) | file.ReadUInt8());

            throw new FormatException("Invalid EBML element ID");
        }

        private ulong ReadVarInt (IBinaryStream file)
        {
            byte firstByte = file.ReadUInt8();
            int size = 1;
            ulong value = 0;

            if ((firstByte & 0x80) != 0)
            {
                value = (ulong)(firstByte & 0x7F);
                size = 1;
            }
            else if ((firstByte & 0x40) != 0)
            {
                value = (ulong)(firstByte & 0x3F);
                size = 2;
            }
            else if ((firstByte & 0x20) != 0)
            {
                value = (ulong)(firstByte & 0x1F);
                size = 3;
            }
            else if ((firstByte & 0x10) != 0)
            {
                value = (ulong)(firstByte & 0x0F);
                size = 4;
            }
            else if ((firstByte & 0x08) != 0)
            {
                value = (ulong)(firstByte & 0x07);
                size = 5;
            }
            else if ((firstByte & 0x04) != 0)
            {
                value = (ulong)(firstByte & 0x03);
                size = 6;
            }
            else if ((firstByte & 0x02) != 0)
            {
                value = (ulong)(firstByte & 0x01);
                size = 7;
            }
            else if ((firstByte & 0x01) != 0)
            {
                value = 0;
                size = 8;
            }
            else
            {
                throw new FormatException("Invalid EBML variable-length integer");
            }

            for (int i = 1; i < size; i++)
            {
                value = (value << 8) | file.ReadUInt8();
            }

            return value;
        }

        private long ReadUnsignedInt (IBinaryStream file, int size)
        {
            long result = 0;
            for (int i = 0; i < size; i++)
            {
                result = (result << 8) | file.ReadUInt8();
            }
            return result;
        }

        private double ReadFloat (IBinaryStream file, int size)
        {
            if (size == 4)
            {
                byte[] buffer = file.ReadBytes (4);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse (buffer);
                return BitConverter.ToSingle (buffer, 0);
            }
            else if (size == 8)
            {
                byte[] buffer = file.ReadBytes (8);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse (buffer);
                return BitConverter.ToDouble (buffer, 0);
            }

            // For other sizes, just skip
            file.Position += size;
            return 0;
        }

        private bool FindElement (IBinaryStream file, uint elementId)
        {
            while (file.Position < file.Length - 4)
            {
                uint id = ReadElementId (file);
                if (id == elementId)
                    return true;

                // Skip this element
                ulong size = ReadVarInt (file);
                file.Position += (long)size;
            }

            return false;
        }

        private void SkipElement (IBinaryStream file)
        {
            uint id = ReadElementId (file);
            ulong size = ReadVarInt (file);
            file.Position += (long)size;
        }
    }
}