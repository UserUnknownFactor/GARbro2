using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using GameRes.Formats.DiscImages;

namespace GameRes.Formats.DiscJuggler
{
    [Export(typeof(ArchiveFormat))]
    public class CdiOpener : DiscImageOpener
    {
        public override string         Tag { get { return "CDI"; } }
        public override string Description { get { return "DiscJuggler disc image"; } }
        public override uint     Signature { get { return  0; } }

        const int SessionDescriptorSize = 15;

        public CdiOpener()
        {
            Extensions = new string[] { "cdi" };
            Signatures = new   uint[] { 0 };
        }

        protected override DiscInfo ParseDiscImage (ArcView file)
        {
            // CDI files have no signature, check extension
            if (!file.Name.EndsWith (".cdi", StringComparison.OrdinalIgnoreCase))
                return null;

            // Read descriptor length from last 4 bytes
            if (file.MaxOffset < 4)
                return null;

            uint descriptorLength = file.View.ReadUInt32 (file.MaxOffset - 4);
            if (descriptorLength == 0 || descriptorLength > file.MaxOffset)
                return null;

            // Read descriptor
            long descriptorOffset = file.MaxOffset - descriptorLength;
            var descriptorData = file.View.ReadBytes (descriptorOffset, descriptorLength - 4);

            // Parse descriptor
            return ParseDescriptor (descriptorData);
        }

        private DiscInfo ParseDescriptor (byte[] data)
        {
            var discInfo = new DiscInfo();
            int offset = 0;

            try
            {
                // First byte is number of sessions
                int numSessions = data[offset++];

                long currentImageOffset = 0;

                // Parse sessions
                for (int i = 0; i <= numSessions; i++) // <= to handle last empty session
                {
                    var session = ParseSession (data, ref offset, ref currentImageOffset);
                    if (session != null && session.Tracks.Count > 0)
                    {
                        session.Number = i + 1;
                        discInfo.Sessions.Add (session);
                    }
                }

                ParseDiscDescriptor (data, ref offset, discInfo);

                return discInfo;
            }
            catch
            {
                return null;
            }
        }

        private SessionInfo ParseSession (byte[] data, ref int offset, ref long currentImageOffset)
        {
            if (offset + SessionDescriptorSize > data.Length)
                return null;

            var session = new SessionInfo();

            // Parse session descriptor
            int numTracks = data[offset + 1];
            offset += SessionDescriptorSize;

            if (numTracks == 0)
                return null;

            // Parse tracks
            int trackNumber = 1;
            for (int i = 0; i < numTracks; i++)
            {
                var track = ParseTrack (data, ref offset, trackNumber++, currentImageOffset);
                if (track != null)
                {
                    session.Tracks.Add (track);
                    currentImageOffset += (track.MainDataSize + track.SubchannelSize) * track.Length;
                }
            }

            return session;
        }

        private TrackInfo ParseTrack (byte[] data, ref int offset, int trackNumber, long imageOffset)
        {
            var track = new TrackInfo
            {
                Number = trackNumber,
                ImageOffset = imageOffset
            };

            // Skip header
            offset += 16; // Fixed pattern
            int filenameLength = data[offset++];
            offset += filenameLength + 31; // Skip filename and post-filename data

            // Parse indices
            int numIndices = BitConverter.ToUInt16 (data, offset);
            offset += 2;

            for (int i = 0; i < numIndices; i++)
            {
                track.Indices.Add (BitConverter.ToInt32 (data, offset));
                offset += 4;
            }

            // Skip CD-TEXT
            int numCdTextBlocks = BitConverter.ToInt32 (data, offset);
            offset += 4;

            for (int i = 0; i < numCdTextBlocks; i++)
            {
                for (int j = 0; j < 18; j++)
                {
                    int length = data[offset++];
                    offset += length;
                }
            }

            offset += 2; // Skip 2 bytes

            // Track data
            track.Mode = ConvertTrackMode (BitConverter.ToInt32 (data, offset));
            offset += 8; // Mode + 4 unknown bytes

            track.Session = BitConverter.ToInt32 (data, offset) + 1;
            offset += 8; // Session + track index

            track.StartSector = BitConverter.ToInt32 (data, offset);
            offset += 4;

            track.Length = BitConverter.ToInt32 (data, offset);
            offset += 4;

            track.EndSector = track.StartSector + track.Length - 1;

            offset += 16; // Skip 16 bytes

            int readMode = BitConverter.ToInt32 (data, offset);
            offset += 4;

            track.Ctl = (byte)BitConverter.ToInt32 (data, offset);
            offset += 4;

            offset += 9; // Skip 9 bytes

            // ISRC
            byte[] isrcBytes = new byte[12];
            Array.Copy (data, offset, isrcBytes, 0, 12);
            offset += 12;

            bool isrcValid = BitConverter.ToInt32 (data, offset) != 0;
            offset += 4;

            if (isrcValid)
            {
                track.Isrc = Encoding.ASCII.GetString (isrcBytes).TrimEnd('\0');
            }

            offset += 99; // Skip remaining bytes

            // Set track start from first index
            if (track.Indices.Count > 0)
            {
                track.Pregap = track.Indices[0];
            }

            // Decode sector sizes based on read mode
            DecodeReadMode (track, readMode);

            return track;
        }

        private void ParseDiscDescriptor (byte[] data, ref int offset, DiscInfo discInfo)
        {
            // Skip header
            offset += 16; // Fixed pattern
            int filenameLength = data[offset++];
            offset += filenameLength + 31;

            // Skip disc data
            offset += 4; // Disc length

            int volumeIdLength = data[offset++];
            if (volumeIdLength > 0 && offset + volumeIdLength <= data.Length)
            {
                discInfo.VolumeId = Encoding.ASCII.GetString (data, offset, volumeIdLength);
                offset += volumeIdLength;
            }

            offset += 9; // Skip 9 bytes

            // MCN
            if (offset + 13 <= data.Length)
            {
                byte[] mcnBytes = new byte[13];
                Array.Copy (data, offset, mcnBytes, 0, 13);
                offset += 13;

                if (offset + 4 <= data.Length)
                {
                    bool mcnValid = BitConverter.ToInt32 (data, offset) != 0;
                    if (mcnValid && discInfo.Sessions.Count > 0)
                    {
                        discInfo.Sessions[0].Mcn = Encoding.ASCII.GetString (mcnBytes).TrimEnd('\0');
                    }
                }
            }
        }

        private TrackMode ConvertTrackMode (int mode)
        {
            switch (mode)
            {
                case 0:
                    return TrackMode.Audio;
                case 1:
                    return TrackMode.Mode1;
                case 2:
                    return TrackMode.Mode2Mixed;
                default:
                    return TrackMode.Audio;
            }
        }

        private void DecodeReadMode (TrackInfo track, int readMode)
        {
            switch (readMode)
            {
                case 0: // 2048-byte sectors (Mode 1 data)
                    track.MainDataSize = 2048;
                    track.SubchannelSize = 0;
                    track.DataOffset = 0;
                    track.SectorSize = 2048;
                    break;
                    
                case 1: // 2336-byte sectors (Mode 2 data)
                    track.MainDataSize = 2336;
                    track.SubchannelSize = 0;
                    track.DataOffset = 0;
                    track.SectorSize = 2336;
                    break;
                    
                case 2: // 2352-byte sectors (Raw)
                    track.MainDataSize = 2352;
                    track.SubchannelSize = 0;
                    track.DataOffset = track.Mode == TrackMode.Audio ? 0 : 16;
                    track.SectorSize = 2352;
                    break;
                    
                case 3: // 2352+16-byte sectors (Raw + Q)
                    track.MainDataSize = 2352;
                    track.SubchannelSize = 16;
                    track.DataOffset = track.Mode == TrackMode.Audio ? 0 : 16;
                    track.SectorSize = 2368;
                    break;
                    
                case 4: // 2352+96-byte sectors (Raw + PW)
                    track.MainDataSize = 2352;
                    track.SubchannelSize = 96;
                    track.DataOffset = track.Mode == TrackMode.Audio ? 0 : 16;
                    track.SectorSize = 2448;
                    break;
                    
                default:
                    track.MainDataSize = 2352;
                    track.SubchannelSize = 0;
                    track.DataOffset = 0;
                    track.SectorSize = 2352;
                    break;
            }
        }
    }
}