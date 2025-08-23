using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using GameRes.Formats.DiscImages;
using GameRes.Utility;

namespace GameRes.Formats.Nero
{
    [Export(typeof(ArchiveFormat))]
    public class NrgOpener : DiscImageOpener
    {
        public override string         Tag { get { return "NRG"; } }
        public override string Description { get { return "Nero Burning ROM disc image"; } }
        public override uint     Signature { get { return  0; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct NrgDaoHeader
        {
            public uint Dummy1;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 13)]
            public byte[] Mcn;
            public byte Dummy2;
            public byte SessionType;
            public byte NumSessions;
            public byte FirstTrack;
            public byte LastTrack;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct NrgDaoBlock
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            public byte[] Isrc;
            public ushort SectorSize;
            public byte ModeCode;
            public byte Dummy1;
            public ushort Dummy2;
            // 32-bit or 64-bit offsets follow
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct NrgEtnBlock
        {
            // 32-bit or 64-bit offset and size
            public byte Mode;
            public uint Sector;
        }

        private static readonly byte[] NeroSignature = Encoding.ASCII.GetBytes ("NERO");
        private static readonly byte[] Ner5Signature = Encoding.ASCII.GetBytes ("NER5");

        public NrgOpener()
        {
            Extensions = new string[] { "nrg" };
            Signatures = new uint[] { 0 };
        }

        protected override DiscInfo ParseDiscImage (ArcView file)
        {
            // Detect format
            bool isOldFormat;
            long trailerOffset;

            if (!DetectNrgFormat (file, out isOldFormat, out trailerOffset))
                return null;

            // Parse blocks
            var blocks = ParseBlocks (file, trailerOffset, isOldFormat);
            if (blocks == null || blocks.Count == 0)
                return null;

            var discInfo = new DiscInfo();

            // Determine medium type
            var mtypBlock = blocks.FirstOrDefault (b => b.Id == "MTYP");
            if (mtypBlock != null)
            {
                var mtyp = Binary.BigEndian (file.View.ReadUInt32 (mtypBlock.Offset + 8));
                discInfo.MediumType = GetMediumType (mtyp);
            }

            // Parse sessions
            ParseSessions (file, blocks, isOldFormat, discInfo);

            return discInfo;
        }

        private bool DetectNrgFormat (ArcView file, out bool isOldFormat, out long trailerOffset)
        {
            isOldFormat = false;
            trailerOffset = 0;

            // Check new format (NER5)
            if (file.MaxOffset >= 12)
            {
                var sig = file.View.ReadBytes (file.MaxOffset - 12, 4);
                if (sig.SequenceEqual (Ner5Signature))
                {
                    trailerOffset = file.View.ReadInt64 (file.MaxOffset - 8);
                    trailerOffset = Binary.BigEndian (trailerOffset);
                    isOldFormat = false;
                    return true;
                }
            }

            // Check old format (NERO)
            if (file.MaxOffset >= 8)
            {
                var sig = file.View.ReadBytes (file.MaxOffset - 8, 4);
                if (sig.SequenceEqual (NeroSignature))
                {
                    trailerOffset = file.View.ReadUInt32 (file.MaxOffset - 4);
                    trailerOffset = Binary.BigEndian ((uint)trailerOffset);
                    isOldFormat = true;
                    return true;
                }
            }

            return false;
        }

        private class BlockInfo
        {
            public string   Id { get; set; }
            public long Offset { get; set; }
            public uint Length { get; set; }
        }

        private List<BlockInfo> ParseBlocks (ArcView file, long trailerOffset, bool isOldFormat)
        {
            var blocks = new List<BlockInfo>();
            long currentOffset = trailerOffset;
            long trailerEnd = isOldFormat ? file.MaxOffset - 8 : file.MaxOffset - 12;

            while (currentOffset < trailerEnd)
            {
                var blockId = Encoding.ASCII.GetString (file.View.ReadBytes (currentOffset, 4));
                var length = Binary.BigEndian (file.View.ReadUInt32 (currentOffset + 4));

                blocks.Add (new BlockInfo
                {
                    Id = blockId,
                    Offset = currentOffset,
                    Length = length
                });

                currentOffset += 8 + length;

                if (blockId == "END!")
                    break;
            }

            return blocks;
        }

        private void ParseSessions (ArcView file, List<BlockInfo> blocks, bool isOldFormat, DiscInfo discInfo)
        {
            int sessionNum = 0;

            // DAO sessions
            var daoBlocks = blocks.Where (b => b.Id == (isOldFormat ? "DAOI" : "DAOX")).ToList();
            var cueBlocks = blocks.Where (b => b.Id == (isOldFormat ? "CUES" : "CUEX")).ToList();

            for (int i = 0; i < daoBlocks.Count && i < cueBlocks.Count; i++)
            {
                var session = ParseDaoSession (file, daoBlocks[i], cueBlocks[i], isOldFormat);
                if (session != null)
                {
                    session.Number = ++sessionNum;
                    discInfo.Sessions.Add (session);
                }
            }

            // TAO tracks
            var etnBlocks = blocks.Where (b => b.Id == (isOldFormat ? "ETNF" : "ETN2")).ToList();
            foreach (var etnBlock in etnBlocks)
            {
                var session = ParseTaoSession (file, etnBlock, isOldFormat);
                if (session != null)
                {
                    session.Number = ++sessionNum;
                    discInfo.Sessions.Add (session);
                }
            }
        }

        private SessionInfo ParseDaoSession (ArcView file, BlockInfo daoBlock, BlockInfo cueBlock, bool isOldFormat)
        {
            var session = new SessionInfo();

            // Parse DAO header
            var headerData = file.View.ReadBytes (daoBlock.Offset + 8, Marshal.SizeOf<NrgDaoHeader>());
            var header = BytesToStruct<NrgDaoHeader>(headerData);

            if (header.Mcn[0] != 0)
                session.Mcn = Encoding.ASCII.GetString (header.Mcn).TrimEnd('\0');

            int trackInfoSize = isOldFormat ? 30 : 42;
            int numTracks = ((int)daoBlock.Length - 22) / trackInfoSize;

            long trackOffset = daoBlock.Offset + 8 + 22;

            for (int i = 0; i < numTracks; i++)
            {
                var trackData = file.View.ReadBytes (trackOffset, 18); // Common part
                var daoTrack = BytesToStruct<NrgDaoBlock>(trackData);

                var track = new TrackInfo
                {
                    Number = i + 1,
                    Session = session.Number,
                    SectorSize = Binary.BigEndian (daoTrack.SectorSize),
                    Mode = DecodeTrackMode (daoTrack.ModeCode)
                };

                if (daoTrack.Isrc[0] != 0)
                    track.Isrc = Encoding.ASCII.GetString (daoTrack.Isrc).TrimEnd('\0');

                if (isOldFormat)
                {
                    track.ImageOffset = Binary.BigEndian (file.View.ReadUInt32 (trackOffset + 22));
                    track.EndSector = Binary.BigEndian (file.View.ReadUInt32 (trackOffset + 26)) / track.SectorSize;
                }
                else
                {
                    track.ImageOffset = (long)Binary.BigEndian (file.View.ReadUInt64 (trackOffset + 26));
                    track.EndSector = (long)Binary.BigEndian (file.View.ReadUInt64 (trackOffset + 34)) / track.SectorSize;
                }

                DecodeSectorComponents (track);
                session.Tracks.Add (track);

                trackOffset += trackInfoSize;
            }

            return session;
        }

        private SessionInfo ParseTaoSession (ArcView file, BlockInfo etnBlock, bool isOldFormat)
        {
            var session = new SessionInfo();

            int blockSize = isOldFormat ? 20 : 32;
            int numTracks = (int)(etnBlock.Length / blockSize);

            long offset = etnBlock.Offset + 8;

            for (int i = 0; i < numTracks; i++)
            {
                var track = new TrackInfo
                {
                    Number = i + 1,
                    Session = session.Number
                };

                if (isOldFormat)
                {
                    track.ImageOffset = Binary.BigEndian (file.View.ReadUInt32 (offset));
                    long size = Binary.BigEndian (file.View.ReadUInt32 (offset + 4));
                    track.Mode = DecodeTrackMode (file.View.ReadByte (offset + 11));
                    track.StartSector = Binary.BigEndian (file.View.ReadUInt32 (offset + 12));

                    DecodeSectorComponents (track);
                    track.Length = size / track.MainDataSize;
                }
                else
                {
                    track.ImageOffset = (long)Binary.BigEndian (file.View.ReadUInt64 (offset));
                    long size = (long)Binary.BigEndian (file.View.ReadUInt64 (offset + 8));
                    track.Mode = DecodeTrackMode (file.View.ReadByte (offset + 19));
                    track.StartSector = Binary.BigEndian (file.View.ReadUInt32 (offset + 20));

                    DecodeSectorComponents (track);
                    track.Length = size / track.MainDataSize;
                }

                track.EndSector = track.StartSector + track.Length - 1;
                session.Tracks.Add (track);

                offset += blockSize;
            }

            return session;
        }

        private string GetMediumType (uint mtyp)
        {
            if ((mtyp & 0x00001) != 0 || (mtyp & 0x00400) != 0)
                return "CD";
            if ((mtyp & 0x0001C) != 0 || (mtyp & 0x00200) != 0)
                return "DVD";
            if ((mtyp & 0x700000) != 0)
                return "BD";
            return "CD";
        }

        private TrackMode DecodeTrackMode (byte mode)
        {
            switch (mode)
            {
                case 0x00: // Mode 1, user data only
                case 0x05: // Mode 1, full sector
                case 0x0F: // Mode 1, full sector with subchannel
                    return TrackMode.Mode1;

                case 0x02: // Mode 2 Form 1, user data only
                    return TrackMode.Mode2Form1;

                case 0x03: // Mode 2 Form 2, user data only
                    return TrackMode.Mode2Form2;

                case 0x06: // Mode 2, full sector
                case 0x11: // Mode 2, full sector with subchannel
                    return TrackMode.Mode2Mixed;

                case 0x07: // Audio, full sector
                case 0x10: // Audio, full sector with subchannel
                    return TrackMode.Audio;

                default:
                    return TrackMode.Audio;
            }
        }

        private void DecodeSectorComponents (TrackInfo track)
        {
            switch (track.Mode)
            {
                case TrackMode.Mode1:
                    if (track.SectorSize == 2048)
                    {
                        track.MainDataSize = 2048;
                        track.SubchannelSize = 0;
                        track.DataOffset = 0;
                    }
                    else if (track.SectorSize == 2352)
                    {
                        track.MainDataSize = 2352;
                        track.SubchannelSize = 0;
                        track.DataOffset = 16;
                    }
                    else if (track.SectorSize == 2448)
                    {
                        track.MainDataSize = 2352;
                        track.SubchannelSize = 96;
                        track.DataOffset = 16;
                    }
                    break;

                case TrackMode.Audio:
                    track.MainDataSize = 2352;
                    track.SubchannelSize = track.SectorSize - 2352;
                    track.DataOffset = 0;
                    break;

                default:
                    track.MainDataSize = track.SectorSize;
                    track.SubchannelSize = 0;
                    track.DataOffset = 16;
                    break;
            }
        }
    }
}