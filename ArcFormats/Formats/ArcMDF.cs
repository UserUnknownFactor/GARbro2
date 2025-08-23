using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using GameRes.Formats.DiscImages;
using GameRes.Utility;

namespace GameRes.Formats.Alcohol120
{
[Export(typeof(ArchiveFormat))]
public class MdfOpener : DiscImageOpener
{
    public override string Tag { get { return "MDF/MDS"; } }
    public override string Description { get { return "Alcohol 120% disc image"; } }
    public override uint Signature { get { return 0; } }

        #region MDS structures

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct MdsHeader
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] Signature;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public byte[] Version;
            public ushort MediumType;
            public ushort NumSessions;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] Dummy1;
            public ushort BcaLen;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
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
            string mdsPath = file.Name;
            string mdfPath = file.Name;

            if (mdsPath.EndsWith (".mdf", StringComparison.OrdinalIgnoreCase))
            {
                mdsPath = Path.ChangeExtension (mdsPath, ".mds");
                if (!VFS.FileExists (mdsPath))
                    return null;

                var mdsEntry = VFS.FindFile (mdsPath);
                using (var mdsStream = VFS.OpenStream (mdsEntry))
                {
                    return ParseMds (mdsStream, file); // Pass the MDF ArcView
                }
            }
            else if (mdsPath.EndsWith (".mds", StringComparison.OrdinalIgnoreCase))
            {
                mdfPath = Path.ChangeExtension (mdfPath, ".mdf");
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

                var result = base.CreateArchive (dataFile, discInfo);

                // Clean up the original MDS view since we're not using it
                if (file != dataFile)
                    file.Dispose();

                return result;
            }

            return base.CreateArchive (dataFile, discInfo);
        }

        private DiscInfo ParseMds (Stream mdsStream, ArcView mdfFile)
        {
            var headerBytes = new byte[Marshal.SizeOf<MdsHeader>()];
            if (mdsStream.Read (headerBytes, 0, headerBytes.Length) != headerBytes.Length)
                return null;

            var header = BytesToStruct<MdsHeader>(headerBytes);

            if (!header.Signature.SequenceEqual (MdsSignature))
                return null;
            if (header.Version[0] != 0x01)
                return null;

            mdsStream.Position = 0;
            var mdsData = new byte[mdsStream.Length];
            mdsStream.Read (mdsData, 0, mdsData.Length);

            var discInfo = new DiscInfo();
            discInfo.MediumType = GetMediumType (header.MediumType);

            bool isCD = header.MediumType <= 0x02;

            // Parse sessions
            int offset = (int)header.SessionsBlocksOffset;
            for (int i = 0; i < header.NumSessions; i++)
            {
                var sessionBlock = BytesToStruct<MdsSessionBlock>(mdsData, offset);
                offset += Marshal.SizeOf<MdsSessionBlock>();

                var session = new SessionInfo
                {
                    Number = sessionBlock.SessionNumber
                };

                // Parse tracks
                int trackOffset = (int)sessionBlock.TracksBlocksOffset;
                var sessionTracks = new List<TrackInfo>();

                for (int j = 0; j < sessionBlock.NumAllBlocks; j++)
                {
                    var trackBlock = BytesToStruct<MdsTrackBlock>(mdsData, trackOffset);
                    trackOffset += Marshal.SizeOf<MdsTrackBlock>();

                    // Skip non-track entries (0xA0, 0xA1, 0xA2, etc.)
                    if (trackBlock.Point < 1 || trackBlock.Point > 99)
                        continue;

                    var track = new TrackInfo
                    {
                        Number = trackBlock.Point,
                        Session = sessionBlock.SessionNumber,
                        StartSector = trackBlock.StartSector,
                        ImageOffset = (long)trackBlock.StartOffset,
                        SectorSize = trackBlock.SectorSize,
                        Mode = ConvertTrackMode (trackBlock.Mode),
                        Ctl = (byte)(trackBlock.AdrCtl & 0x0F)
                    };

                    // For CDs, parse extra block if it exists
                    if (isCD && trackBlock.ExtraOffset != 0)
                    {
                        var extraBlock = BytesToStruct<MdsTrackExtraBlock>(mdsData, (int)trackBlock.ExtraOffset);
                        track.Pregap = (int)extraBlock.Pregap;
                        track.Length = extraBlock.Length;
                    }
                    else if (!isCD)
                    {
                        // For DVDs, extra_offset contains track length
                        track.Length = trackBlock.ExtraOffset;
                    }

                    DecodeSectorSize (track);

                    // Set data offset based on mode and sector size
                    if (track.SectorSize == 2048)  // Cooked sector - no sync/header
                        track.DataOffset = 0;
                    else if (track.MainDataSize >= 2352)  // Raw sector
                        track.DataOffset = track.Mode == TrackMode.Audio ? 0 : 16;

                    sessionTracks.Add (track);
                }

                // Calculate track lengths if not set (fallback)
                for (int t = 0; t < sessionTracks.Count; t++)
                {
                    var track = sessionTracks[t];

                    if (track.Length == 0)
                    {
                        // Try to calculate from next track
                        if (t + 1 < sessionTracks.Count)
                        {
                            var nextTrack = sessionTracks[t + 1];
                            track.Length = nextTrack.StartSector - track.StartSector;
                        }
                        else
                        {
                            // Last track - calculate from session end
                            track.Length = sessionBlock.SessionEnd - (int)track.StartSector + 1;
                        }
                    }

                    track.EndSector = track.StartSector + track.Length - 1;
                }

                session.Tracks.AddRange (sessionTracks);
                discInfo.Sessions.Add (session);
            }

            discInfo.TotalSize = mdfFile.MaxOffset;

            return discInfo;
        }

        private string GetMediumType (ushort type)
        {
            switch (type)
            {
                case 0x00:
                case 0x01:
                case 0x02:
                    return "CD";
                case 0x10:
                case 0x12:
                    return "DVD";
                default:
                    return "Unknown";
            }
        }

        private TrackMode ConvertTrackMode (byte mode)
        {
            // MDS uses specific values for track modes
            switch (mode & 0x0F)
            {
                case 0x00:
                case 0x08:
                    return TrackMode.Mode2;
                case 0x01:
                case 0x09:
                    return TrackMode.Audio;
                case 0x02:
                case 0x0A:
                    return TrackMode.Mode1;
                case 0x03:
                case 0x0B:
                    return TrackMode.Mode2;
                case 0x04:
                case 0x0C:
                    return TrackMode.Mode2Form1;
                case 0x05:
                case 0x0D:
                    return TrackMode.Mode2Form2;
                case 0x07:
                case 0x0F:
                    return TrackMode.Mode2;
                default:
                    return TrackMode.Mode1; // Default fallback
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
            case 2352:
                track.MainDataSize = 2352;
                track.SubchannelSize = 0;
                break;
            case 2048:
                // Cooked data
                track.MainDataSize = 2048;
                track.SubchannelSize = 0;
                break;
            default:
                track.MainDataSize = track.SectorSize;
                track.SubchannelSize = 0;
                break;
            }

        }
    }
}