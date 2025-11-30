using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using GameRes.Formats.DiscImages;
using GameRes.Utility;

namespace GameRes.Formats.Alcohol120
{
    [Export (typeof (ArchiveFormat))]
    [ExportMetadata ("Priority", 50)]
    public class MdfOpener : DiscImageOpener
    {
        public override string         Tag { get { return "MDF/MDS"; } }
        public override string Description { get { return "Alcohol 120% disc image"; } }
        public override uint     Signature { get { return  0; } }

        #region MDS structures 

        [StructLayout (LayoutKind.Sequential, Pack = 1)]
        internal struct MdsHeader
        {
            [MarshalAs (UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] Signature;
            public byte VersionMajor;
            public byte VersionMinor;
            public ushort MediumType;
            public ushort NumSessions;
            public uint Dummy1;
            public ushort BcaLen;
            [MarshalAs (UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] Dummy2;
            public uint BcaDataOffset;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
            public byte[] Dummy3;
            public uint DiscStructuresOffset;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            public byte[] Dummy4;
            public uint SessionsBlocksOffset;
            public uint DpmBlocksOffset;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct MdsSessionBlock
        {
            public int SessionStart;
            public int SessionEnd;
            public ushort SessionNumber;
            public byte NumAllBlocks;
            public byte NumNonTrackBlocks;
            public ushort FirstTrack;
            public ushort LastTrack;
            public uint Dummy1;
            public uint TracksBlocksOffset;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct MdsTrackBlock
        {
            public byte Mode;
            public byte Subchannel;
            public byte AdrCtl;
            public byte Tno;
            public byte Point;
            public byte Min;
            public byte Sec;
            public byte Frame;
            public byte Zero;
            public byte PMin;
            public byte PSec;
            public byte PFrame;
            public uint ExtraOffset;
            public ushort SectorSize;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 18)]
            public byte[] Dummy4;
            public uint StartSector;
            public ulong StartOffset;
            public uint NumberOfFiles;
            public uint FooterOffset;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
            public byte[] Dummy6;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct MdsTrackExtraBlock
        {
            public uint Pregap;
            public uint Length;
        }

        private static readonly byte[] MdsSignature = Encoding.ASCII.GetBytes ("MEDIA DESCRIPTOR");

        #endregion

        public MdfOpener()
        {
            Extensions = new string[] { "mdf", "mds" };
        }

        protected override DiscInfo ParseDiscImage (ArcView file)
        {
            string filePath = file.Name;

            if (filePath.EndsWith (".mdf", StringComparison.OrdinalIgnoreCase))
            {
                string mdsPath = Path.ChangeExtension (filePath, ".mds");
                if (!VFS.FileExists (mdsPath))
                    return null;

                var mdsEntry = VFS.FindFile (mdsPath);
                using (var mdsStream = VFS.OpenStream (mdsEntry))
                {
                    return ParseMds (mdsStream, file); // Pass the MDF ArcView
                }
            }
            else if (filePath.EndsWith (".mds", StringComparison.OrdinalIgnoreCase))
            {
                var sig = file.View.ReadBytes (0, 16);
                if (!sig.SequenceEqual (MdsSignature))
                    return null;

                string mdfPath = Path.ChangeExtension (filePath, ".mdf");
                if (!VFS.FileExists (mdfPath))
                    return null;

                using (var mdsStream = file.CreateStream())
                {
                    var mdfEntry = VFS.FindFile (mdfPath);
                    using (var mdfView = VFS.OpenView (mdfEntry))
                    {
                        return ParseMds (mdsStream, mdfView);
                    }
                }
            }

            return null;
        }

        protected override ArcFile CreateArchive (ArcView file, DiscInfo discInfo)
        {
            ArcView dataFile = file;

            if (file.Name.EndsWith (".mds", StringComparison.OrdinalIgnoreCase))
            {
                string mdfPath = Path.ChangeExtension (file.Name, ".mdf");
                var mdfEntry   = VFS.FindFile (mdfPath);
                dataFile       = VFS.OpenView (mdfEntry);

                var result = CreateAudioArchive (dataFile, discInfo);

                if (file != dataFile)
                    file.Dispose();

                return result;
            }
            else if (file.Name.EndsWith (".mdf", StringComparison.OrdinalIgnoreCase))
            {
                return CreateDataArchive (dataFile, discInfo);
            }

            return null;
        }

        private ArcFile CreateAudioArchive (ArcView file, DiscInfo discInfo)
        {
            bool hasAudio = discInfo.Sessions.Any (s => s.Tracks.Any (t => t.IsAudio));
            bool hasData = discInfo.Sessions.Any (s => s.Tracks.Any (t => !t.IsAudio));

            var entries = new List<Entry>();

            if (hasAudio)
            {
                string cueContent = GenerateMdsCueSheet (file.Name, discInfo);
                var cueEntry = new CueEntry
                {
                    Name = Path.GetFileNameWithoutExtension (file.Name) + ".cue",
                    Type = "audio",
                    Offset = 0,
                    Size = (uint)Encoding.UTF8.GetByteCount (cueContent),
                    CueContent = cueContent
                };
                entries.Add (cueEntry);

                foreach (var session in discInfo.Sessions)
                {
                    foreach (var track in session.Tracks.Where (t => t.IsAudio))
                    {
                        var wavEntry = new Entry
                        {
                            Name = $"Track{track.Number:D2}.wav",
                            Type = "audio",
                            Offset = track.ImageOffset,
                            Size = (uint)(track.Length * 2352 + 44)
                        };
                        entries.Add (wavEntry);
                    }
                }

                return new DiscImageArchive (file, this, entries, discInfo);
            }
            else if (hasData)
            {
                return CreateDataArchive (file, discInfo);
            }

            return null;
        }

        private ArcFile CreateDataArchive (ArcView file, DiscInfo discInfo)
        {
            var dataTracks = discInfo.Sessions
                .SelectMany (s => s.Tracks)
                .Where (t => !t.IsAudio)
                .OrderBy (t => t.StartSector)
                .ToList();

            //Debug.WriteLine ($"CreateDataArchive: Found {dataTracks.Count} data tracks");

            if (dataTracks.Count == 0)
            {
                //Debug.WriteLine ("No data tracks found, returning null");
                return null;
            }

            /*
            foreach (var track in dataTracks)
            {
                Debug.WriteLine ($"  Data track {track.Number}: StartSector={track.StartSector}, " +
                                $"Length={track.Length}, ImageOffset=0x{track.ImageOffset:X}, " +
                                $"SectorSize={track.SectorSize}, DataOffset={track.DataOffset}");
            }
            */

            // Parse ISO filesystem directly from data tracks
            var entries = ParseMdfIsoFileSystem (file, dataTracks);

            if (entries != null && entries.Count > 0)
            {
                //Debug.WriteLine ($"ParseMdfIsoFileSystem returned {entries.Count} entries");
                return new DiscImageArchive (file, this, entries, discInfo);
            }

            //Debug.WriteLine ("ParseMdfIsoFileSystem returned null or empty, returning null");
            return null;
        }

        /// <summary>
        /// Parse ISO filesystem from MDF data tracks, handling absolute LBAs correctly
        /// </summary>
        private List<Entry> ParseMdfIsoFileSystem (ArcView file, List<TrackInfo> dataTracks)
        {
            if (dataTracks.Count == 0)
                return null;

            var firstTrack = dataTracks[0];
            long baseAbsoluteLba = firstTrack.StartSector;

            //Debug.WriteLine ($"ParseMdfIsoFileSystem: baseAbsoluteLba = {baseAbsoluteLba}");

            // Read PVD from sector 16 relative to track start
            byte[] pvd = null;
            int pvdRelativeSector = -1;

            for (int sector = 16; sector < 32; sector++)
            {
                var testSector = ReadMdfSector (file, firstTrack, sector);
                if (testSector == null)
                    break;

                // Check for PVD (type 0x01)
                if (testSector[0] == 0x01 && CheckIsoSignature (testSector, 1))
                {
                    pvd = testSector;
                    pvdRelativeSector = sector;
                    //Debug.WriteLine ($"Found PVD at relative sector {sector}");
                    break;
                }

                // Check for terminator
                if (testSector[0] == 0xFF)
                    break;
            }

            if (pvd == null)
            {
                //Debug.WriteLine ("PVD not found");
                return null;
            }

            // Get root directory location (absolute LBA in ISO)
            uint rootDirAbsoluteLba = BitConverter.ToUInt32 (pvd, 158);
            uint rootDirSize = BitConverter.ToUInt32 (pvd, 166);

            //Debug.WriteLine ($"Root dir absolute LBA: {rootDirAbsoluteLba}, size: {rootDirSize}");

            // Check for Joliet SVD
            Encoding encoding = FileNameEncoding.Get<Encoding>();

            for (int sector = pvdRelativeSector + 1; sector < 32; sector++)
            {
                var svd = ReadMdfSector (file, firstTrack, sector);
                if (svd == null)
                    break;

                if (svd[0] == 0xFF)
                    break;

                if (svd[0] == 0x02 && CheckIsoSignature (svd, 1))
                {
                    // Check for Joliet escape sequences
                    if (svd[88] == 0x25 && svd[89] == 0x2F &&
                        (svd[90] == 0x40 || svd[90] == 0x43 || svd[90] == 0x45))
                    {
                        rootDirAbsoluteLba = BitConverter.ToUInt32 (svd, 158);
                        rootDirSize = BitConverter.ToUInt32 (svd, 166);
                        encoding = Encoding.BigEndianUnicode;
                        //Debug.WriteLine ($"Using Joliet: Root dir LBA: {rootDirAbsoluteLba}");
                        break;
                    }
                }
            }

            var entries = new List<Entry>();
            ParseMdfIsoDirectory (file, dataTracks, baseAbsoluteLba, rootDirAbsoluteLba, rootDirSize, "", entries, encoding);

            return entries;
        }

        private void ParseMdfIsoDirectory(
            ArcView file, List<TrackInfo> tracks, long baseAbsoluteLba,
            uint dirAbsoluteLba, uint dirSize, string path, List<Entry> entries, Encoding encoding)
        {
            var dirData = ReadMdfSectors (file, tracks, baseAbsoluteLba, dirAbsoluteLba, dirSize);
            if (dirData == null)
            {
                //Debug.WriteLine ($"Failed to read directory at LBA {dirAbsoluteLba}");
                return;
            }

            int offset = 0;
            while (offset < dirData.Length)
            {
                byte recordLength = dirData[offset];
                if (recordLength == 0)
                {
                    offset = ((offset / 2048) + 1) * 2048;
                    if (offset >= dirData.Length)
                        break;
                    continue;
                }

                if (offset + recordLength > dirData.Length)
                    break;

                byte nameLength = dirData[offset + 32];
                uint fileLba = BitConverter.ToUInt32 (dirData, offset + 2);
                uint fileSize = BitConverter.ToUInt32 (dirData, offset + 10);
                byte fileFlags = dirData[offset + 25];

                string name;
                if (encoding == Encoding.BigEndianUnicode)
                    name = encoding.GetString (dirData, offset + 33, nameLength);
                else
                    name = DecodeIsoFilename (dirData, offset + 33, nameLength, encoding);

                // Skip . and .. entries
                if (nameLength == 1 && (dirData[offset + 33] == 0x00 || dirData[offset + 33] == 0x01))
                {
                    offset += recordLength;
                    continue;
                }

                name = RemoveIsoVersionSuffix (name);

                string fullPath = string.IsNullOrEmpty (path) ? name : path + "/" + name;

                if ((fileFlags & 0x02) != 0) // Directory
                {
                    ParseMdfIsoDirectory (file, tracks, baseAbsoluteLba, fileLba, fileSize, fullPath, entries, encoding);
                }
                else // File
                {
                    var entry = new MdfIsoFileEntry
                    {
                        Name = fullPath,
                        Type = FormatCatalog.Instance.GetTypeFromName (name),
                        Offset = 0,
                        Size = fileSize,
                        AbsoluteLba = fileLba,
                        BaseAbsoluteLba = baseAbsoluteLba,
                        DataTracks = tracks
                    };
                    entries.Add (entry);
                }

                offset += recordLength;
            }
        }

        private byte[] ReadMdfSector (ArcView file, TrackInfo track, long relativeSector)
        {
            if (relativeSector < 0 || relativeSector >= track.Length)
                return null;

            long offset = track.ImageOffset + relativeSector * track.SectorSize + track.DataOffset;

            if (offset + 2048 > file.MaxOffset)
                return null;

            return file.View.ReadBytes (offset, 2048);
        }

        private byte[] ReadMdfSectorAbsolute (ArcView file, List<TrackInfo> tracks, long baseAbsoluteLba, long absoluteLba)
        {
            foreach (var track in tracks)
            {
                if (absoluteLba >= track.StartSector && absoluteLba < track.StartSector + track.Length)
                {
                    long relativeSector = absoluteLba - track.StartSector;
                    return ReadMdfSector (file, track, relativeSector);
                }
            }

            //Debug.WriteLine ($"Sector at absolute LBA {absoluteLba} not found in any track");
            return null;
        }

        private byte[] ReadMdfSectors (ArcView file, List<TrackInfo> tracks, long baseAbsoluteLba, uint absoluteStartLba, uint size)
        {
            int numSectors = (int)((size + 2047) / 2048);
            var data = new byte[numSectors * 2048];

            for (int i = 0; i < numSectors; i++)
            {
                var sector = ReadMdfSectorAbsolute (file, tracks, baseAbsoluteLba, absoluteStartLba + i);
                if (sector == null)
                {
                    //Debug.WriteLine ($"Failed to read sector at absolute LBA {absoluteStartLba + i}");
                    return null;
                }
                Array.Copy (sector, 0, data, i * 2048, 2048);
            }

            return data;
        }

        private string RemoveIsoVersionSuffix (string name)
        {
            int semicolon = name.LastIndexOf(';');
            if (semicolon >= 0 && semicolon < name.Length - 1)
            {
                bool isVersion = true;
                for (int i = semicolon + 1; i < name.Length; i++)
                {
                    if (!char.IsDigit (name[i]))
                    {
                        isVersion = false;
                        break;
                    }
                }
                if (isVersion)
                    return name.Substring (0, semicolon);
            }
            return name;
        }

        private new string DecodeIsoFilename (byte[] data, int offset, int length, Encoding encoding)
        {
            bool isAscii = true;
            for (int i = 0; i < length; i++)
            {
                if (data[offset + i] >= 0x80)
                {
                    isAscii = false;
                    break;
                }
            }

            if (isAscii)
                return Encoding.ASCII.GetString (data, offset, length);
            else
                return encoding.GetString (data, offset, length);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var mdfEntry = entry as MdfIsoFileEntry;
            if (mdfEntry != null)
            {
                return new MdfIsoFileStream (arc.File, mdfEntry);
            }

            return base.OpenEntry (arc, entry);
        }

        #region CUE Sheet Generation

        private string GenerateMdsCueSheet (string imagePath, DiscInfo discInfo)
        {
            var sb = new StringBuilder();
            string fileName = Path.GetFileName (Path.ChangeExtension (imagePath, ".mdf"));

            sb.AppendLine ($"REM GENRE \"Unknown\"");
            sb.AppendLine ($"REM DATE \"{DateTime.Now.Year}\"");
            sb.AppendLine ($"REM COMMENT \"Generated by GARbro {Tag} plugin\"");

            if (!string.IsNullOrEmpty (discInfo.Title))
                sb.AppendLine ($"TITLE \"{discInfo.Title}\"");

            sb.AppendLine ($"FILE \"{fileName}\" BINARY");

            foreach (var session in discInfo.Sessions)
            {
                if (!string.IsNullOrEmpty (session.Mcn))
                    sb.AppendLine ($"CATALOG {session.Mcn}");

                foreach (var track in session.Tracks)
                {
                    string trackType = GetCueTrackType (track);
                    sb.AppendLine ($"  TRACK {track.Number:D2} {trackType}");

                    if (!string.IsNullOrEmpty (track.Title))
                        sb.AppendLine ($"    TITLE \"{track.Title}\"");

                    if (!string.IsNullOrEmpty (track.Isrc))
                        sb.AppendLine ($"    ISRC {track.Isrc}");

                    if ((track.Ctl & 0x01) != 0 || (track.Flags & 0x01) != 0)
                        sb.AppendLine ("    FLAGS PRE");
                    if ((track.Ctl & 0x04) != 0 || (track.Flags & 0x04) != 0)
                        sb.AppendLine ("    FLAGS DCP");
                    if ((track.Ctl & 0x08) != 0 || (track.Flags & 0x08) != 0)
                        sb.AppendLine ("    FLAGS 4CH");

                    if (track.Pregap > 0)
                    {
                        long pregapLba = track.StartSector - track.Pregap;
                        if (pregapLba >= 0)
                        {
                            AppendMsf (sb, "INDEX 00", pregapLba);
                        }
                        else
                        {
                            int pregapFrames = (int)(-pregapLba);
                            int pregapMins = pregapFrames / 75 / 60;
                            int pregapSecs = (pregapFrames / 75) % 60;
                            int pregapFrms = pregapFrames % 75;
                            sb.AppendLine ($"    PREGAP {pregapMins:D2}:{pregapSecs:D2}:{pregapFrms:D2}");
                        }
                    }

                    AppendMsf (sb, "INDEX 01", track.StartSector);

                    if (track.Indices != null && track.Indices.Count > 1)
                    {
                        long currentPos = track.StartSector;
                        for (int i = 1; i < track.Indices.Count; i++)
                        {
                            currentPos += track.Indices[i];
                            AppendMsf (sb, $"INDEX {i + 1:D2}", currentPos);
                        }
                    }
                }
            }

            return sb.ToString();
        }

        private void AppendMsf (StringBuilder sb, string prefix, long lba)
        {
            int totalFrames = (int)lba;
            int minutes = totalFrames / 75 / 60;
            int seconds = (totalFrames / 75) % 60;
            int frames = totalFrames % 75;
            sb.AppendLine ($"    {prefix} {minutes:D2}:{seconds:D2}:{frames:D2}");
        }

        private new string GetCueTrackType (TrackInfo track)
        {
            if (track.IsAudio)
                return "AUDIO";

            switch (track.Mode)
            {
                case TrackMode.Mode1:
                    return track.SectorSize == 2048 ? "MODE1/2048" : "MODE1/2352";
                case TrackMode.Mode2:
                case TrackMode.Mode2Mixed:
                case TrackMode.Mode2Form1:
                case TrackMode.Mode2Form2:
                    return "MODE2/2352";
                default:
                    return "MODE1/2352";
            }
        }

        #endregion

        #region MDS Parsing

        private DiscInfo ParseMds (Stream mdsStream, ArcView mdfFile)
        {
            var headerBytes = new byte[Marshal.SizeOf<MdsHeader>()];
            if (mdsStream.Read (headerBytes, 0, headerBytes.Length) != headerBytes.Length)
                return null;

            var header = BytesToStruct<MdsHeader>(headerBytes);

            if (!header.Signature.SequenceEqual (MdsSignature))
                return null;

            if (header.VersionMajor != 0x01)
                return null;

            //Debug.WriteLine ($"MDS Version: {header.VersionMajor}.{header.VersionMinor}");
            //Debug.WriteLine ($"Medium Type: 0x{header.MediumType:X4}");
            //Debug.WriteLine ($"Num Sessions: {header.NumSessions}");
            //Debug.WriteLine ($"Sessions Offset: 0x{header.SessionsBlocksOffset:X}");

            mdsStream.Position = 0;
            var mdsData = new byte[mdsStream.Length];
            mdsStream.Read (mdsData, 0, mdsData.Length);

            var discInfo = new DiscInfo();
            discInfo.MediumType = GetMediumType (header.MediumType);

            bool isCD = header.MediumType <= 0x02;

            // Parse sessions
            int sessionOffset = (int)header.SessionsBlocksOffset;

            for (int i = 0; i < header.NumSessions; i++)
            {
                if (sessionOffset + Marshal.SizeOf<MdsSessionBlock>() > mdsData.Length)
                    break;

                var sessionBlock = BytesToStruct<MdsSessionBlock>(mdsData, sessionOffset);
                sessionOffset += Marshal.SizeOf<MdsSessionBlock>();

                //Debug.WriteLine ($"Session {sessionBlock.SessionNumber}:");
                //Debug.WriteLine ($"  Start: {sessionBlock.SessionStart}, End: {sessionBlock.SessionEnd}");
                //Debug.WriteLine ($"  NumAllBlocks: {sessionBlock.NumAllBlocks}");
                //Debug.WriteLine ($"  TracksOffset: 0x{sessionBlock.TracksBlocksOffset:X}");

                var session = new SessionInfo
                {
                    Number = sessionBlock.SessionNumber
                };

                // Parse tracks
                int trackOffset = (int)sessionBlock.TracksBlocksOffset;
                var sessionTracks = new List<TrackInfo>();

                if (trackOffset + Marshal.SizeOf<MdsTrackBlock>() > mdsData.Length)
                {
                    discInfo.Sessions.Add (session);
                    continue;
                }

                for (int j = 0; j < sessionBlock.NumAllBlocks; j++)
                {
                    if (trackOffset + Marshal.SizeOf<MdsTrackBlock>() > mdsData.Length)
                        break;

                    var trackBlock = BytesToStruct<MdsTrackBlock>(mdsData, trackOffset);
                    trackOffset += Marshal.SizeOf<MdsTrackBlock>();

                    //Debug.WriteLine ($"  Block {j}: Point=0x{trackBlock.Point:X2}, Mode=0x{trackBlock.Mode:X2}, " +
                                    //$"SectorSize={trackBlock.SectorSize}, StartSector={trackBlock.StartSector}");

                    if (trackBlock.Point < 1 || trackBlock.Point > 99)
                        continue;

                    if (trackBlock.SectorSize == 0)
                        continue;

                    var track = new TrackInfo
                    {
                        Number = trackBlock.Point,
                        Session = sessionBlock.SessionNumber,
                        StartSector = trackBlock.StartSector,
                        ImageOffset = (long)trackBlock.StartOffset,
                        SectorSize = trackBlock.SectorSize,
                        Mode = ConvertTrackMode (trackBlock.Mode),
                        Ctl = (byte)((trackBlock.AdrCtl >> 4) & 0x0F)
                    };

                    if (isCD && trackBlock.ExtraOffset != 0 &&
                        trackBlock.ExtraOffset != 0xFFFFFFFF &&
                        trackBlock.ExtraOffset + 8 <= mdsData.Length)
                    {
                        var extraBlock = BytesToStruct<MdsTrackExtraBlock>(mdsData, (int)trackBlock.ExtraOffset);
                        track.Pregap = (int)extraBlock.Pregap;
                        track.Length = extraBlock.Length;
                    }
                    else if (!isCD && trackBlock.ExtraOffset != 0xFFFFFFFF)
                    {
                        track.Length = trackBlock.ExtraOffset;
                    }

                    DecodeSectorSize (track);
                    SetDataOffset (track);

                    sessionTracks.Add (track);
                    //Debug.WriteLine ($"    Added track {track.Number}: Mode={track.Mode}, IsAudio={track.IsAudio}");
                }

                for (int t = 0; t < sessionTracks.Count; t++)
                {
                    var track = sessionTracks[t];

                    if (track.Length == 0)
                    {
                        if (t + 1 < sessionTracks.Count)
                        {
                            var nextTrack = sessionTracks[t + 1];
                            track.Length = nextTrack.StartSector - track.StartSector;
                        }
                        else
                        {
                            track.Length = (uint)Math.Max (0, sessionBlock.SessionEnd - (int)track.StartSector + 1);
                        }
                    }

                    if (track.Length < 0)
                        track.Length = 0;

                    track.EndSector = track.StartSector + track.Length - 1;
                }

                session.Tracks.AddRange (sessionTracks);
                discInfo.Sessions.Add (session);
            }

            discInfo.TotalSize = mdfFile.MaxOffset;

            int totalTracks = discInfo.Sessions.Sum (s => s.Tracks.Count);
            if (totalTracks == 0)
                return null;

            //Debug.WriteLine ($"=== Summary: {totalTracks} tracks ===");

            return discInfo;
        }

        private void SetDataOffset (TrackInfo track)
        {
            if (track.Mode == TrackMode.Audio) track.DataOffset = 0;
            else if (track.SectorSize == 2048) track.DataOffset = 0;
            else if (track.SectorSize == 2352) track.DataOffset = 16;
            else if (track.SectorSize == 2336) track.DataOffset = 8;
            else if (track.SectorSize == 2448) track.DataOffset = 16;
        }

        private string GetMediumType (ushort type)
        {
            switch (type)
            {
                case 0x00: return "CD-ROM";
                case 0x01: return "CD-R";
                case 0x02: return "CD-RW";
                case 0x10: return "DVD-ROM";
                case 0x12: return "DVD-R";
                default: return "Unknown";
            }
        }

        private TrackMode ConvertTrackMode (byte mode)
        {
            switch (mode)
            {
                case 0x00: return TrackMode.Mode2;
                case 0xA9: return TrackMode.Audio;
                case 0xAA: return TrackMode.Mode1;
                case 0xAB: return TrackMode.Mode2;
                case 0xAC: return TrackMode.Mode2Form1;
                case 0xAD: return TrackMode.Mode2Form2;
                case 0xEC: return TrackMode.Mode2Form1;
                default: return TrackMode.Mode1;
            }
        }

        private void DecodeSectorSize (TrackInfo track)
        {
            switch (track.SectorSize)
            {
                case 2448:
                    track.MainDataSize = 2352;
                    track.SubchannelSize = 96;
                    break;
                case 2368:
                    track.MainDataSize = 2352;
                    track.SubchannelSize = 16;
                    break;
                case 2352:
                    track.MainDataSize = 2352;
                    track.SubchannelSize = 0;
                    break;
                case 2336:
                    track.MainDataSize = 2336;
                    track.SubchannelSize = 0;
                    break;
                case 2056:
                    track.MainDataSize = 2048;
                    track.SubchannelSize = 8;
                    break;
                case 2048:
                    track.MainDataSize = 2048;
                    track.SubchannelSize = 0;
                    break;
                default:
                    track.MainDataSize = track.SectorSize;
                    track.SubchannelSize = 0;
                    break;
            }
        }

        #endregion
    }

    #region MDF ISO Entry and Stream

    public class MdfIsoFileEntry : Entry
    {
        public uint AbsoluteLba { get; set; }
        public long BaseAbsoluteLba { get; set; }
        public List<DiscImageOpener.TrackInfo> DataTracks { get; set; }
    }

    public class MdfIsoFileStream : Stream
    {
        private readonly ArcView m_file;
        private readonly MdfIsoFileEntry m_entry;
        private readonly long m_length;
        private long m_position;

        public MdfIsoFileStream (ArcView file, MdfIsoFileEntry entry)
        {
            m_file = file;
            m_entry = entry;
            m_length = entry.Size;
            m_position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => m_length;
        public override long Position
        {
            get => m_position;
            set => m_position = Math.Max (0, Math.Min (value, m_length));
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            if (m_position >= m_length)
                return 0;

            count = (int)Math.Min (count, m_length - m_position);
            int totalRead = 0;

            while (count > 0)
            {
                long fileSector = m_position / 2048;
                int sectorOffset = (int)(m_position % 2048);
                long absoluteLba = m_entry.AbsoluteLba + fileSector;

                // Find track containing this absolute LBA
                DiscImageOpener.TrackInfo track = null;
                foreach (var t in m_entry.DataTracks)
                {
                    if (absoluteLba >= t.StartSector && absoluteLba < t.StartSector + t.Length)
                    {
                        track = t;
                        break;
                    }
                }

                if (track == null)
                    break;

                long relativeSector = absoluteLba - track.StartSector;
                long fileOffset = track.ImageOffset + relativeSector * track.SectorSize + track.DataOffset + sectorOffset;

                int available = 2048 - sectorOffset;
                int toRead = Math.Min (count, available);

                int read = m_file.View.Read (fileOffset, buffer, offset, (uint)toRead);

                offset += read;
                count -= read;
                m_position += read;
                totalRead += read;

                if (read < toRead)
                    break;
            }

            return totalRead;
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            switch (origin)
            {
            case SeekOrigin.Begin:
                Position = offset;
                break;
            case SeekOrigin.Current:
                Position = m_position + offset;
                break;
            case SeekOrigin.End:
                Position = m_length + offset;
                break;
            }
            return m_position;
        }

        public override void Flush () { }
        public override void SetLength (long value) { throw new NotSupportedException(); }
        public override void Write (byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }

    #endregion
}