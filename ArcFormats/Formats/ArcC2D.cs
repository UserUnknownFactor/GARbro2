using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using GameRes.Formats.DiscImages;

namespace GameRes.Formats.WinOnCD
{
    [Export(typeof(ArchiveFormat))]
    public class C2dOpener : DiscImageOpener
    {
        public override string         Tag { get { return "C2D"; } }
        public override string Description { get { return "WinOnCD/Roxio disc image"; } }
        public override uint     Signature { get { return  0; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct C2dHeaderBlock
        {
            [MarshalAs (UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] Signature;
            public ushort HeaderSize;
            public ushort HasUpcEan;
            [MarshalAs (UnmanagedType.ByValArray, SizeConst = 13)]
            public byte[] UpcEan;
            public byte Dummy1;
            public ushort NumTrackBlocks;
            public uint SizeCdtext;
            public uint OffsetTracks;
            public uint Dummy2;
            [MarshalAs (UnmanagedType.ByValArray, SizeConst = 128)]
            public byte[] Description;
            public uint OffsetC2ck;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct C2dTrackBlock
        {
            public uint BlockSize;
            public uint FirstSector;
            public uint LastSector;
            public ulong ImageOffset;
            public uint SectorSize;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            public byte[] Isrc;
            public byte Flags;
            public byte Session;
            public byte Point;
            public byte Index;
            public byte Mode;
            public byte Compressed;
            public ushort Dummy;
        }

        private static readonly byte[] Signature1 = Encoding.ASCII.GetBytes ("Adaptec CeQuadrat VirtualCD File");
        private static readonly byte[] Signature2 = Encoding.ASCII.GetBytes ("Roxio Image File Format 3.0");

        public C2dOpener()
        {
            Extensions = new string[] { "c2d" };
            Signatures = new uint[] { 0 };
        }

        protected override DiscInfo ParseDiscImage (ArcView file)
        {
            // Check signature
            var sig = file.View.ReadBytes (0, 32);
            if (!IsValidSignature (sig))
                return null;

            // Read header
            var headerData = file.View.ReadBytes (0, Marshal.SizeOf<C2dHeaderBlock>());
            var header = BytesToStruct<C2dHeaderBlock>(headerData);

            if (header.HeaderSize < Marshal.SizeOf<C2dHeaderBlock>())
                return null;

            var discInfo = new DiscInfo();
            
            // Set MCN if present
            if (header.HasUpcEan != 0)
            {
                var mcn = Encoding.ASCII.GetString (header.UpcEan).TrimEnd('\0');
                if (discInfo.Sessions.Count == 0)
                    discInfo.Sessions.Add (new SessionInfo { Number = 1 });
                discInfo.Sessions[0].Mcn = mcn;
            }

            // Parse tracks
            int trackOffset = (int)header.OffsetTracks;
            var sessions = new Dictionary<int, SessionInfo>();
            
            for (int i = 0; i < header.NumTrackBlocks; i++)
            {
                var trackData = file.View.ReadBytes (trackOffset, Marshal.SizeOf<C2dTrackBlock>());
                var trackBlock = BytesToStruct<C2dTrackBlock>(trackData);
                trackOffset += Marshal.SizeOf<C2dTrackBlock>();

                if (trackBlock.Compressed != 0)
                    continue; // Skip compressed tracks

                // Get or create session
                if (!sessions.ContainsKey (trackBlock.Session))
                {
                    sessions[trackBlock.Session] = new SessionInfo { Number = trackBlock.Session };
                }

                var track = new TrackInfo
                {
                    Number = trackBlock.Point,
                    Session = trackBlock.Session,
                    StartSector = trackBlock.FirstSector,
                    EndSector = trackBlock.LastSector,
                    Length = trackBlock.LastSector - trackBlock.FirstSector + 1,
                    ImageOffset = (long)trackBlock.ImageOffset,
                    SectorSize = (int)trackBlock.SectorSize,
                    Mode = ConvertTrackMode (trackBlock.Mode),
                    Flags = trackBlock.Flags,
                    DataOffset = 0
                };

                // Set ISRC
                if (trackBlock.Index == 1 && trackBlock.Isrc[0] != 0)
                {
                    track.Isrc = Encoding.ASCII.GetString (trackBlock.Isrc).TrimEnd('\0');
                }

                // Decode sector components
                DecodeSectorSize (track);

                sessions[trackBlock.Session].Tracks.Add (track);
            }

            // Add sessions in order
            foreach (var session in sessions.Values.OrderBy (s => s.Number))
            {
                session.Tracks = session.Tracks.OrderBy (t => t.Number).ToList();
                discInfo.Sessions.Add (session);
            }

            return discInfo;
        }

        private bool IsValidSignature (byte[] sig)
        {
            if (sig.Length < 32)
                return false;

            bool sig1Match = true, sig2Match = true;
            
            for (int i = 0; i < Math.Min (Signature1.Length, 32); i++)
            {
                if (sig[i] != Signature1[i])
                    sig1Match = false;
            }

            for (int i = 0; i < Math.Min (Signature2.Length, 27); i++)
            {
                if (sig[i] != Signature2[i])
                    sig2Match = false;
            }

            return sig1Match || sig2Match;
        }

        private TrackMode ConvertTrackMode (byte mode)
        {
            switch (mode)
            {
                case 0x00:
                case 0xFF:
                    return TrackMode.Audio;
                case 0x01:
                    return TrackMode.Mode1;
                case 0x02:
                    return TrackMode.Mode2;
                default:
                    return TrackMode.Audio;
            }
        }

        private void DecodeSectorSize (TrackInfo track)
        {
            if (track.SectorSize == 2448)
            {
                track.MainDataSize = 2352;
                track.SubchannelSize = 96;
            }
            else
            {
                track.MainDataSize = track.SectorSize;
                track.SubchannelSize = 0;
            }

            // Set data offset for data tracks
            if (track.Mode != TrackMode.Audio && track.MainDataSize > 2048)
            {
                track.DataOffset = 16;
            }
        }
    }
}