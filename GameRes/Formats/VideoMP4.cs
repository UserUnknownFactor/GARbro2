using System;
using System.IO;
using System.Text;
using System.ComponentModel.Composition;
using System.Windows.Media;

namespace GameRes
{
    [Export(typeof(VideoFormat))]
    public class Mp4Format : VideoFormat
    {
        public override string Tag { get { return "MP4"; } }
        public override string Description { get { return "MPEG-4 Video"; } }
        public override uint Signature { get { return 0; } } // "ftyp"
        public override bool CanWrite { get { return false; } }

        // MP4 box types
        private static readonly uint FTYP = 0x70797466; // 'ftyp'
        private static readonly uint MOOV = 0x766F6F6D; // 'moov'
        private static readonly uint MVHD = 0x6468766D; // 'mvhd'
        private static readonly uint TRAK = 0x6B617274; // 'trak'
        private static readonly uint TKHD = 0x64686B74; // 'tkhd'
        private static readonly uint MDIA = 0x6169646D; // 'mdia'
        private static readonly uint MDHD = 0x6468646D; // 'mdhd'
        private static readonly uint MINF = 0x666E696D; // 'minf'
        private static readonly uint STBL = 0x6C627473; // 'stbl'
        private static readonly uint STSD = 0x64737473; // 'stsd'
        //private static readonly uint STSZ = 0x7A737473; // 'stsz'
        //private static readonly uint STCO = 0x6F637473; // 'stco'
        private static readonly uint HDLR = 0x726C6468; // 'hdlr'
        //private static readonly uint VMHD = 0x64686D76; // 'vmhd'
        //private static readonly uint SMHD = 0x64686D73; // 'smhd'

        public Mp4Format()
        {
            Extensions = new string[] { "m4v", "mp4" };
        }

        public override VideoData Read (IBinaryStream file, VideoMetaData info)
        {
            file.Position = 0;
            uint size = ReadUInt32BE (file);
            uint type = file.ReadUInt32();

            if (type != FTYP)
                return null;

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

            // Check for ftyp box
            file.Position = 0;
            uint size = ReadUInt32BE (file);
            uint type = file.ReadUInt32();

            if (type != FTYP)
                return null;

            // Skip the rest of ftyp box
            file.Position = size;

            var meta = new VideoMetaData
            {
                Width = 0,
                Height = 0,
                Duration = 0,
                FrameRate = 0,
                Codec = "Unknown",
                CommonExtension = "mp4",
                HasAudio = false
            };

            try
            {
                ParseBoxes (file, meta);
            }
            catch {}

            return meta;
        }

        private void ParseBoxes (IBinaryStream file, VideoMetaData meta)
        {
            while (file.Position < file.Length)
            {
                long boxStart = file.Position;
                uint boxSize = ReadUInt32BE (file);
                uint boxType = file.ReadUInt32();

                if (boxSize == 0)
                    boxSize = (uint)(file.Length - boxStart);
                else if (boxSize == 1)
                {
                    // 64-bit box size
                    ulong largeSize = ReadUInt64BE (file);
                    if (largeSize > uint.MaxValue)
                        break; // Box too large to handle
                    boxSize = (uint)largeSize;
                }

                if (boxSize < 8)
                    break; // Invalid box

                if (boxType == MOOV)
                {
                    // Parse moov box (contains metadata)
                    ParseMoovBox (file, boxStart + 8, boxStart + boxSize, meta);
                }

                // Move to the next box
                file.Position = boxStart + boxSize;
            }
        }

        private void ParseMoovBox (IBinaryStream file, long start, long end, VideoMetaData meta)
        {
            file.Position = start;

            while (file.Position < end)
            {
                long boxStart = file.Position;
                uint boxSize = ReadUInt32BE (file);
                uint boxType = file.ReadUInt32();

                if (boxSize == 0)
                    boxSize = (uint)(end - boxStart);

                if (boxSize < 8 || boxStart + boxSize > end)
                    break; // Invalid box

                if (boxType == MVHD)
                {
                    // Movie header - contains duration and timescale
                    ParseMvhdBox (file, boxStart + 8, boxStart + boxSize, meta);
                }
                else if (boxType == TRAK)
                {
                    // Track - contains video/audio stream info
                    ParseTrakBox (file, boxStart + 8, boxStart + boxSize, meta);
                }

                // Move to the next box
                file.Position = boxStart + boxSize;
            }
        }

        private void ParseMvhdBox (IBinaryStream file, long start, long end, VideoMetaData meta)
        {
            file.Position = start;
            byte version = file.ReadUInt8();

            // Skip flags
            file.Position += 3;

            if (version == 1)
            {
                // 64-bit creation and modification times
                file.Position += 16;

                uint timeScale = ReadUInt32BE (file);
                ulong duration = ReadUInt64BE (file);

                if (timeScale > 0)
                    meta.Duration = (long)(duration * 1000 / timeScale); // Convert to milliseconds
            }
            else
            {
                // 32-bit times
                file.Position += 8;

                uint timeScale = ReadUInt32BE (file);
                uint duration = ReadUInt32BE (file);

                if (timeScale > 0)
                    meta.Duration = (long)(duration * 1000 / timeScale); // Convert to milliseconds
            }
        }

        private void ParseTrakBox (IBinaryStream file, long start, long end, VideoMetaData meta)
        {
            file.Position = start;
            bool isVideoTrack = false;
            bool isAudioTrack = false;

            while (file.Position < end)
            {
                long boxStart = file.Position;
                uint boxSize = ReadUInt32BE (file);
                uint boxType = file.ReadUInt32();

                if (boxSize == 0)
                    boxSize = (uint)(end - boxStart);

                if (boxSize < 8 || boxStart + boxSize > end)
                    break; // Invalid box

                if (boxType == TKHD)
                {
                    // Track header - contains width and height for video tracks
                    ParseTkhdBox (file, boxStart + 8, boxStart + boxSize, meta);
                }
                else if (boxType == MDIA)
                {
                    // Media box - contains media info
                    long mediaStart = boxStart + 8;
                    long mediaEnd = boxStart + boxSize;

                    // Check if this is a video or audio track
                    isVideoTrack = IsVideoTrack (file, mediaStart, mediaEnd);
                    isAudioTrack = IsAudioTrack (file, mediaStart, mediaEnd);

                    if (isVideoTrack)
                    {
                        // Parse media box for video info
                        ParseMdiaBox (file, mediaStart, mediaEnd, meta);
                    }
                    else if (isAudioTrack)
                    {
                        meta.HasAudio = true;
                    }
                }

                // Move to the next box
                file.Position = boxStart + boxSize;
            }
        }

        private bool IsVideoTrack (IBinaryStream file, long start, long end)
        {
            long originalPos = file.Position;
            bool result = false;

            try
            {
                file.Position = start;

                while (file.Position < end)
                {
                    long boxStart = file.Position;
                    uint boxSize = ReadUInt32BE (file);
                    uint boxType = file.ReadUInt32();

                    if (boxSize < 8 || boxStart + boxSize > end)
                        break;

                    if (boxType == HDLR)
                    {
                        // Skip version and flags
                        file.Position += 4;

                        // Skip pre-defined
                        file.Position += 4;

                        // Handler type
                        uint handlerType = file.ReadUInt32();

                        // 'vide' = video track
                        if (handlerType == 0x65646976)
                        {
                            result = true;
                            break;
                        }
                    }

                    file.Position = boxStart + boxSize;
                }
            }
            finally
            {
                file.Position = originalPos;
            }

            return result;
        }

        private bool IsAudioTrack (IBinaryStream file, long start, long end)
        {
            long originalPos = file.Position;
            bool result = false;

            try
            {
                file.Position = start;

                while (file.Position < end)
                {
                    long boxStart = file.Position;
                    uint boxSize = ReadUInt32BE (file);
                    uint boxType = file.ReadUInt32();

                    if (boxSize < 8 || boxStart + boxSize > end)
                        break;

                    if (boxType == HDLR)
                    {
                        // Skip version and flags
                        file.Position += 4;

                        // Skip pre-defined
                        file.Position += 4;

                        // Handler type
                        uint handlerType = file.ReadUInt32();

                        // 'soun' = audio track
                        if (handlerType == 0x6E756F73)
                        {
                            result = true;
                            break;
                        }
                    }

                    file.Position = boxStart + boxSize;
                }
            }
            finally
            {
                file.Position = originalPos;
            }

            return result;
        }

        private void ParseTkhdBox (IBinaryStream file, long start, long end, VideoMetaData meta)
        {
            file.Position = start;
            byte version = file.ReadUInt8();

            // Skip flags
            file.Position += 3;

            if (version == 1)
            {
                // 64-bit creation, modification, and track ID
                file.Position += 24;

                // Skip reserved
                file.Position += 4;

                // Skip duration
                file.Position += 8;
            }
            else
            {
                // 32-bit times and track ID
                file.Position += 12;

                // Skip reserved
                file.Position += 4;

                // Skip duration
                file.Position += 4;
            }

            // Skip more reserved bytes
            file.Position += 8;

            // Skip layer and alternate group
            file.Position += 4;

            // Skip volume and reserved
            file.Position += 4;

            // Skip matrix
            file.Position += 36;

            // Width and height are 16.16 fixed point
            uint widthFixed = ReadUInt32BE (file);
            uint heightFixed = ReadUInt32BE (file);

            // Convert from fixed point to integer
            meta.Width = widthFixed >> 16;
            meta.Height = heightFixed >> 16;
        }

        private void ParseMdiaBox (IBinaryStream file, long start, long end, VideoMetaData meta)
        {
            file.Position = start;

            while (file.Position < end)
            {
                long boxStart = file.Position;
                uint boxSize = ReadUInt32BE (file);
                uint boxType = file.ReadUInt32();

                if (boxSize == 0)
                    boxSize = (uint)(end - boxStart);

                if (boxSize < 8 || boxStart + boxSize > end)
                    break;

                if (boxType == MDHD)
                {
                    // Media header - contains timescale and duration
                    ParseMdhdBox (file, boxStart + 8, boxStart + boxSize, meta);
                }
                else if (boxType == MINF)
                {
                    // Media info - contains sample tables
                    ParseMinfBox (file, boxStart + 8, boxStart + boxSize, meta);
                }

                file.Position = boxStart + boxSize;
            }
        }

        private void ParseMdhdBox (IBinaryStream file, long start, long end, VideoMetaData meta)
        {
            file.Position = start;
            byte version = file.ReadUInt8();

            // Skip flags
            file.Position += 3;

            if (version == 1)
            {
                // 64-bit creation and modification times
                file.Position += 16;

                uint timeScale = ReadUInt32BE (file);
                ulong duration = ReadUInt64BE (file);

                if (timeScale > 0)
                {
                    meta.FrameRate = timeScale / 1000.0; // Approximate frame rate
                }
            }
            else
            {
                // 32-bit times
                file.Position += 8;

                uint timeScale = ReadUInt32BE (file);
                uint duration = ReadUInt32BE (file);

                if (timeScale > 0)
                {
                    meta.FrameRate = timeScale / 1000.0; // Approximate frame rate
                }
            }
        }

        private void ParseMinfBox (IBinaryStream file, long start, long end, VideoMetaData meta)
        {
            file.Position = start;

            while (file.Position < end)
            {
                long boxStart = file.Position;
                uint boxSize = ReadUInt32BE (file);
                uint boxType = file.ReadUInt32();

                if (boxSize == 0)
                    boxSize = (uint)(end - boxStart);

                if (boxSize < 8 || boxStart + boxSize > end)
                    break;

                if (boxType == STBL)
                {
                    // Sample table - contains codec info
                    ParseStblBox (file, boxStart + 8, boxStart + boxSize, meta);
                }

                file.Position = boxStart + boxSize;
            }
        }

        private void ParseStblBox (IBinaryStream file, long start, long end, VideoMetaData meta)
        {
            file.Position = start;

            while (file.Position < end)
            {
                long boxStart = file.Position;
                uint boxSize = ReadUInt32BE (file);
                uint boxType = file.ReadUInt32();

                if (boxSize == 0)
                    boxSize = (uint)(end - boxStart);

                if (boxSize < 8 || boxStart + boxSize > end)
                    break;

                if (boxType == STSD)
                {
                    // Sample description - contains codec info
                    ParseStsdBox (file, boxStart + 8, boxStart + boxSize, meta);
                }

                file.Position = boxStart + boxSize;
            }
        }

        private void ParseStsdBox (IBinaryStream file, long start, long end, VideoMetaData meta)
        {
            file.Position = start;

            // Skip version and flags
            file.Position += 4;

            // Number of entries
            uint entryCount = ReadUInt32BE (file);

            for (uint i = 0; i < entryCount && file.Position < end; i++)
            {
                long entryStart = file.Position;
                uint entrySize = ReadUInt32BE (file);
                uint format = file.ReadUInt32();

                if (entrySize < 8 || entryStart + entrySize > end)
                    break;

                // Skip reserved bytes
                file.Position += 6;

                // Skip data reference index
                file.Position += 2;

                // Video sample description
                if (format == 0x31637661) // 'avc1' - H.264
                {
                    meta.Codec = "H.264";

                    // Skip version and revision
                    file.Position += 4;

                    // Skip vendor
                    file.Position += 4;

                    // Skip temporal quality, spatial quality, and width/height (already got from tkhd)
                    file.Position += 12;

                    // Skip horizontal resolution, vertical resolution, and data size
                    file.Position += 12;

                    // Skip frame count
                    file.Position += 2;

                    // Compressor name (Pascal string - first byte is length)
                    byte nameLength = file.ReadUInt8();
                    if (nameLength > 0 && nameLength <= 31)
                    {
                        byte[] nameBytes = file.ReadBytes (nameLength);
                        string codecName = Encoding.ASCII.GetString (nameBytes);
                        if (!string.IsNullOrWhiteSpace (codecName))
                            meta.Codec = codecName;
                    }

                    // Skip the rest of the 32-byte compressor name field
                    file.Position = entryStart + 8 + 78;

                    // Depth and color table ID
                    file.Position += 4;
                }
                else if (format == 0x31766568) // 'hev1' - HEVC/H.265
                {
                    meta.Codec = "H.265";
                }
                else if (format == 0x31707663) // 'vp31' - VP3
                {
                    meta.Codec = "VP3";
                }
                else if (format == 0x31707678) // 'vp8x' - VP8
                {
                    meta.Codec = "VP8";
                }
                else if (format == 0x31707639) // 'vp09' - VP9
                {
                    meta.Codec = "VP9";
                }
                else if (format == 0x3167706A) // 'jpg ' - JPEG
                {
                    meta.Codec = "MJPEG";
                }
                else if (format == 0x6D783561) // 'ax5m' - MPEG-5 AV1
                {
                    meta.Codec = "AV1";
                }

                // Move to the next entry
                file.Position = entryStart + entrySize;
            }
        }

        // Helper methods for reading big-endian values
        private uint ReadUInt32BE (IBinaryStream file)
        {
            byte[] buffer = file.ReadBytes (4);
            return (uint)((buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3]);
        }

        private ulong ReadUInt64BE (IBinaryStream file)
        {
            byte[] buffer = file.ReadBytes (8);
            return ((ulong)buffer[0] << 56) | ((ulong)buffer[1] << 48) |
                   ((ulong)buffer[2] << 40) | ((ulong)buffer[3] << 32) |
                   ((ulong)buffer[4] << 24) | ((ulong)buffer[5] << 16) |
                   ((ulong)buffer[6] << 8) | buffer[7];
        }
    }
}