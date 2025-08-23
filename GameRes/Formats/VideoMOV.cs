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
    public class MovFormat : VideoFormat
    {
        public override string Tag { get { return "MOV"; } }
        public override string Description { get { return "QuickTime Movie"; } }
        public override uint Signature { get { return 0x00000020; } } // Size field followed by type
        public override bool CanWrite { get { return false; } }

        // MOV atom types
        private static readonly uint FTYP_ATOM = 0x70797466; // 'ftyp'
        private static readonly uint MOOV_ATOM = 0x766F6F6D; // 'moov'
        private static readonly uint MDAT_ATOM = 0x7461646d; // 'mdat'
        private static readonly uint MVHD_ATOM = 0x6468766D; // 'mvhd'
        private static readonly uint TRAK_ATOM = 0x6B617274; // 'trak'
        private static readonly uint TKHD_ATOM = 0x64686B74; // 'tkhd'
        private static readonly uint MDIA_ATOM = 0x6169646D; // 'mdia'
        private static readonly uint MDHD_ATOM = 0x6468646D; // 'mdhd'
        private static readonly uint HDLR_ATOM = 0x726C6468; // 'hdlr'
        private static readonly uint MINF_ATOM = 0x666E696D; // 'minf'
        private static readonly uint STBL_ATOM = 0x6C627473; // 'stbl'
        private static readonly uint STSD_ATOM = 0x64737473; // 'stsd'
        private static readonly uint VIDE_HANDLER = 0x65646976; // 'vide'
        private static readonly uint SOUN_HANDLER = 0x6E756F73; // 'soun'

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
            if (file.Length < 12)
                return null;

            // Check for valid MOV file
            file.Position = 0;
            uint size = ReadUInt32BE (file);
            uint type = file.ReadUInt32();

            bool isValidMov = false;

            // MOV files can start with different atoms
            if (type == FTYP_ATOM || type == MOOV_ATOM || type == MDAT_ATOM)
            {
                isValidMov = true;
            }
            else
            {
                // Try to find moov atom in the first few KB
                file.Position = 0;
                isValidMov = FindAtom (file, MOOV_ATOM, 8192);
            }

            if (!isValidMov)
                return null;

            var meta = new VideoMetaData
            {
                Width = 0,
                Height = 0,
                Duration = 0,
                FrameRate = 0,
                Codec = "H.264", // Default codec for MOV
                CommonExtension = "mov",
                HasAudio = false
            };

            try
            {
                // Parse the file to extract metadata
                file.Position = 0;
                ParseMovAtoms (file, meta);
            }
            catch (Exception)
            {
                // If parsing fails, return basic metadata
            }

            return meta;
        }

        private void ParseMovAtoms (IBinaryStream file, VideoMetaData meta)
        {
            while (file.Position < file.Length - 8)
            {
                long atomStart = file.Position;
                uint atomSize = ReadUInt32BE (file);
                uint atomType = file.ReadUInt32();

                // Handle special case for 64-bit atom size
                if (atomSize == 1 && file.Position + 8 <= file.Length)
                {
                    ulong largeSize = ReadUInt64BE (file);
                    if (largeSize > uint.MaxValue || atomStart + (long)largeSize > file.Length)
                        break;

                    atomSize = (uint)largeSize;
                }

                if (atomSize < 8 || atomStart + atomSize > file.Length)
                    break;

                if (atomType == MOOV_ATOM)
                {
                    ParseMoovAtom (file, atomStart + 8, atomStart + atomSize, meta);
                }

                // Move to the next atom
                file.Position = atomStart + atomSize;
            }
        }

        private void ParseMoovAtom (IBinaryStream file, long start, long end, VideoMetaData meta)
        {
            file.Position = start;

            while (file.Position < end - 8)
            {
                long atomStart = file.Position;
                uint atomSize = ReadUInt32BE (file);
                uint atomType = file.ReadUInt32();

                if (atomSize < 8 || atomStart + atomSize > end)
                    break;

                if (atomType == MVHD_ATOM)
                {
                    ParseMvhdAtom (file, atomStart + 8, atomStart + atomSize, meta);
                }
                else if (atomType == TRAK_ATOM)
                {
                    ParseTrakAtom (file, atomStart + 8, atomStart + atomSize, meta);
                }

                file.Position = atomStart + atomSize;
            }
        }

        private void ParseMvhdAtom (IBinaryStream file, long start, long end, VideoMetaData meta)
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
                    meta.Duration = (long)(duration * 1000 / timeScale);
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

        private void ParseTrakAtom (IBinaryStream file, long start, long end, VideoMetaData meta)
        {
            file.Position = start;
            bool isVideoTrack = false;

            while (file.Position < end - 8)
            {
                long atomStart = file.Position;
                uint atomSize = ReadUInt32BE (file);
                uint atomType = file.ReadUInt32();

                if (atomSize < 8 || atomStart + atomSize > end)
                    break;

                if (atomType == TKHD_ATOM)
                {
                    ParseTkhdAtom (file, atomStart + 8, atomStart + atomSize, meta);
                }
                else if (atomType == MDIA_ATOM)
                {
                    // Check if this is a video track
                    isVideoTrack = IsMdiaVideo (file, atomStart + 8, atomStart + atomSize);

                    if (isVideoTrack)
                    {
                        ParseMdiaAtom (file, atomStart + 8, atomStart + atomSize, meta);
                    }
                    else
                    {
                        // Check if it's an audio track
                        bool isAudioTrack = IsMdiaAudio (file, atomStart + 8, atomStart + atomSize);
                        if (isAudioTrack)
                        {
                            meta.HasAudio = true;
                        }
                    }
                }

                file.Position = atomStart + atomSize;
            }
        }

        private bool IsMdiaVideo (IBinaryStream file, long start, long end)
        {
            long originalPos = file.Position;
            bool result = false;

            try
            {
                file.Position = start;

                while (file.Position < end - 8)
                {
                    long atomStart = file.Position;
                    uint atomSize = ReadUInt32BE (file);
                    uint atomType = file.ReadUInt32();

                    if (atomSize < 8 || atomStart + atomSize > end)
                        break;

                    if (atomType == HDLR_ATOM)
                    {
                        // Skip version and flags
                        file.Position += 4;

                        // Skip component type and subtype
                        file.Position += 8;

                        // Handler type
                        uint handlerType = file.ReadUInt32();

                        if (handlerType == VIDE_HANDLER)
                        {
                            result = true;
                            break;
                        }
                    }

                    file.Position = atomStart + atomSize;
                }
            }
            finally
            {
                file.Position = originalPos;
            }

            return result;
        }

        private bool IsMdiaAudio (IBinaryStream file, long start, long end)
        {
            long originalPos = file.Position;
            bool result = false;

            try
            {
                file.Position = start;

                while (file.Position < end - 8)
                {
                    long atomStart = file.Position;
                    uint atomSize = ReadUInt32BE (file);
                    uint atomType = file.ReadUInt32();

                    if (atomSize < 8 || atomStart + atomSize > end)
                        break;

                    if (atomType == HDLR_ATOM)
                    {
                        // Skip version and flags
                        file.Position += 4;

                        // Skip component type and subtype
                        file.Position += 8;

                        // Handler type
                        uint handlerType = file.ReadUInt32();

                        if (handlerType == SOUN_HANDLER)
                        {
                            result = true;
                            break;
                        }
                    }

                    file.Position = atomStart + atomSize;
                }
            }
            finally
            {
                file.Position = originalPos;
            }

            return result;
        }

        private void ParseTkhdAtom (IBinaryStream file, long start, long end, VideoMetaData meta)
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

        private void ParseMdiaAtom (IBinaryStream file, long start, long end, VideoMetaData meta)
        {
            file.Position = start;

            while (file.Position < end - 8)
            {
                long atomStart = file.Position;
                uint atomSize = ReadUInt32BE (file);
                uint atomType = file.ReadUInt32();

                if (atomSize < 8 || atomStart + atomSize > end)
                    break;

                if (atomType == MDHD_ATOM)
                {
                    ParseMdhdAtom (file, atomStart + 8, atomStart + atomSize, meta);
                }
                else if (atomType == MINF_ATOM)
                {
                    ParseMinfAtom (file, atomStart + 8, atomStart + atomSize, meta);
                }

                file.Position = atomStart + atomSize;
            }
        }

        private void ParseMdhdAtom (IBinaryStream file, long start, long end, VideoMetaData meta)
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

        private void ParseMinfAtom (IBinaryStream file, long start, long end, VideoMetaData meta)
        {
            file.Position = start;

            while (file.Position < end - 8)
            {
                long atomStart = file.Position;
                uint atomSize = ReadUInt32BE (file);
                uint atomType = file.ReadUInt32();

                if (atomSize < 8 || atomStart + atomSize > end)
                    break;

                if (atomType == STBL_ATOM)
                {
                    ParseStblAtom (file, atomStart + 8, atomStart + atomSize, meta);
                }

                file.Position = atomStart + atomSize;
            }
        }

        private void ParseStblAtom (IBinaryStream file, long start, long end, VideoMetaData meta)
        {
            file.Position = start;

            while (file.Position < end - 8)
            {
                long atomStart = file.Position;
                uint atomSize = ReadUInt32BE (file);
                uint atomType = file.ReadUInt32();

                if (atomSize < 8 || atomStart + atomSize > end)
                    break;

                if (atomType == STSD_ATOM)
                {
                    ParseStsdAtom (file, atomStart + 8, atomStart + atomSize, meta);
                }

                file.Position = atomStart + atomSize;
            }
        }

        private void ParseStsdAtom (IBinaryStream file, long start, long end, VideoMetaData meta)
        {
            file.Position = start;

            // Skip version and flags
            file.Position += 4;

            // Number of entries
            uint entryCount = ReadUInt32BE (file);

            for (uint i = 0; i < entryCount && file.Position < end - 8; i++)
            {
                long entryStart = file.Position;
                uint entrySize = ReadUInt32BE (file);
                uint format = file.ReadUInt32();

                if (entrySize < 8 || entryStart + entrySize > end)
                    break;

                // Determine codec from format
                switch (format)
                {
                    case 0x31637661: // 'avc1' - H.264
                        meta.Codec = "H.264";
                        break;
                    case 0x31766568: // 'hev1' - HEVC/H.265
                    case 0x31636568: // 'hec1' - HEVC/H.265
                        meta.Codec = "H.265";
                        break;
                    case 0x31707663: // 'vp31' - VP3
                        meta.Codec = "VP3";
                        break;
                    case 0x38707678: // 'vp8x' - VP8
                        meta.Codec = "VP8";
                        break;
                    case 0x39707678: // 'vp9x' - VP9
                        meta.Codec = "VP9";
                        break;
                    case 0x3167706A: // 'jpg ' - JPEG
                        meta.Codec = "MJPEG";
                        break;
                    case 0x31766D61: // 'av01' - AV1
                        meta.Codec = "AV1";
                        break;
                    case 0x6D783561: // 'ax5m' - MPEG-5
                        meta.Codec = "MPEG-5";
                        break;
                    case 0x31323633: // '263 ' - H.263
                        meta.Codec = "H.263";
                        break;
                    case 0x31323661: // 'a261' - MPEG-2
                        meta.Codec = "MPEG-2";
                        break;
                    case 0x31766964: // 'div1' - DivX
                        meta.Codec = "DivX";
                        break;
                    case 0x3234706D: // 'mp42' - MPEG-4 Part 2
                        meta.Codec = "MPEG-4";
                        break;
                    default:
                        // Convert format to string for unknown codecs
                        byte[] formatBytes = BitConverter.GetBytes (format);
                        meta.Codec = Encoding.ASCII.GetString (formatBytes).Trim('\0');
                        break;
                }

                // Move to the next entry
                file.Position = entryStart + entrySize;
            }
        }

        private bool FindAtom (IBinaryStream file, uint atomType, int searchLimit)
        {
            long originalPos = file.Position;
            bool result = false;

            try
            {
                file.Position = 0;
                long endPos = Math.Min (file.Length, searchLimit);

                while (file.Position < endPos - 8)
                {
                    long atomStart = file.Position;
                    uint atomSize = ReadUInt32BE (file);
                    uint type = file.ReadUInt32();

                    if (type == atomType)
                    {
                        result = true;
                        break;
                    }

                    if (atomSize < 8)
                        atomSize = 8;

                    file.Position = atomStart + atomSize;
                }
            }
            catch
            {
                result = false;
            }
            finally
            {
                file.Position = originalPos;
            }

            return result;
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