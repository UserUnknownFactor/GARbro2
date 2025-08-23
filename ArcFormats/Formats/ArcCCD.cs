using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GameRes.Formats.DiscImages;
using GameRes.Utility;

namespace GameRes.Formats.CloneCD
{
    [Export (typeof(ArchiveFormat))]
    public class CcdOpener : DiscImageOpener
    {
        public override string         Tag { get { return "IMG/CCD"; } }
        public override string Description { get { return "CloneCD disc image"; } }
        public override uint     Signature { get { return 0; } }

        public CcdOpener()
        {
            Extensions = new string[] { "ccd", "img" };
            Signatures = new uint[] { 0 };
        }

        protected override DiscInfo ParseDiscImage (ArcView file)
        {
            string ccdPath = file.Name;
            string imgPath = file.Name;
            string subPath = file.Name;

            if (ccdPath.EndsWith (".img", StringComparison.OrdinalIgnoreCase))
            {
                ccdPath = Path.ChangeExtension (ccdPath, ".ccd");
                subPath = Path.ChangeExtension (imgPath, ".sub");

                // If no CCD file, try to parse as raw image
                if (!VFS.FileExists (ccdPath))
                {
                    return ParseRawImage (file);
                }
            }
            else if (ccdPath.EndsWith (".ccd", StringComparison.OrdinalIgnoreCase))
            {
                imgPath = Path.ChangeExtension (imgPath, ".img");
                subPath = Path.ChangeExtension (imgPath, ".sub");

                // IMG file must exist
                if (!VFS.FileExists (imgPath))
                    return null;
            }
            else
            {
                return null;
            }

            var ccdEntry = VFS.FindFile (ccdPath);
            using (var ccdStream = VFS.OpenStream (ccdEntry))
            {
                var ccdData = ParseCcdFile (ccdStream);
                if (ccdData == null)
                    return null;

                // Open IMG file for track mode detection
                var imgEntry = VFS.FindFile (imgPath);
                var imgView = VFS.OpenView (imgEntry);

                return BuildDiscInfo (ccdData, imgView, VFS.FileExists (subPath));
            }
        }

        private DiscInfo ParseRawImage (ArcView file)
        {
            // Try to detect if it's a raw CD image
            long fileSize = file.MaxOffset;

            // Check if file size is divisible by common sector sizes
            bool is2352 = (fileSize % 2352) == 0;
            bool is2448 = (fileSize % 2448) == 0;

            if (!is2352 && !is2448)
                return null;

            int sectorSize = is2448 ? 2448 : 2352;
            long numSectors = fileSize / sectorSize;

            // Detect track mode by checking first sector
            var firstSector = file.View.ReadBytes (0, Math.Min (sectorSize, 2352));
            var trackMode = DetermineTrackModeFromSector (firstSector);

            // Create a simple single-track disc
            var discInfo = new DiscInfo();
            var session = new SessionInfo
            {
                Number = 1,
                Type = trackMode == TrackMode.Audio ? SessionType.CDDA : SessionType.CDROM
            };

            var track = new TrackInfo
            {
                Number = 1,
                Session = 1,
                StartSector = 0,
                EndSector = numSectors - 1,
                Length = numSectors,
                ImageOffset = 0,
                SectorSize = sectorSize,
                MainDataSize = 2352,
                SubchannelSize = sectorSize - 2352,
                DataOffset = trackMode == TrackMode.Audio ? 0 : 16,
                Mode = trackMode,
                Ctl = (byte)(trackMode == TrackMode.Audio ? 0x00 : 0x04)
            };

            session.Tracks.Add (track);
            discInfo.Sessions.Add (session);

            return discInfo;
        }

        private TrackMode DetermineTrackModeFromSector (byte[] sectorData)
        {
            if (sectorData.Length < 16)
                return TrackMode.Audio;

            bool hasSync = sectorData[0] == 0x00 && sectorData[11] == 0x00 &&
                          Enumerable.Range (1, 10).All (i => sectorData[i] == 0xFF);

            if (!hasSync)
                return TrackMode.Audio;

            byte mode = sectorData[15];
            switch (mode)
            {
                case 1:
                    return TrackMode.Mode1;
                case 2:
                    // Check for XA signature
                    if (sectorData.Length >= 24 &&
                        sectorData[16] == 0x00 && sectorData[17] == 0x00 &&
                        sectorData[18] == 0x08 && sectorData[19] == 0x00 &&
                        sectorData[20] == 0x00 && sectorData[21] == 0x00 &&
                        sectorData[22] == 0x08 && sectorData[23] == 0x00)
                    {
                        return TrackMode.Mode2Mixed;
                    }
                    return TrackMode.Mode2;
                default:
                    return TrackMode.Audio;
            }
        }

        private class CcdData
        {
            public class Entry
            {
                public int Session { get; set; }
                public int Point { get; set; }
                public int ADR { get; set; }
                public int Control { get; set; }
                public int TrackNo { get; set; }
                public int AMin { get; set; }
                public int ASec { get; set; }
                public int AFrame { get; set; }
                public int ALBA { get; set; }
                public int Zero { get; set; }
                public int PMin { get; set; }
                public int PSec { get; set; }
                public int PFrame { get; set; }
                public int PLBA { get; set; }
                public int Mode { get; set; }
                public int Index0 { get; set; }
                public int Index1 { get; set; }
                public string ISRC { get; set; }
            }

            public int TocEntries { get; set; }
            public int Sessions { get; set; }
            public int DataTracksScrambled { get; set; }
            public int CDTextLength { get; set; }
            public string Catalog { get; set; }
            public List<Entry> Entries { get; set; } = new List<Entry>();
            public byte[] CDTextData { get; set; }
        }

        private CcdData ParseCcdFile (Stream stream)
        {
            var data = new CcdData();
            var currentEntry = (CcdData.Entry)null;
            var entryMap = new Dictionary<int, CcdData.Entry>();

            using (var reader = new StreamReader (stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (string.IsNullOrEmpty (line))
                        continue;

                    // Parse sections
                    var sectionMatch = Regex.Match (line, @"^\[(.+)\]\s*$");
                    if (sectionMatch.Success)
                    {
                        var section = sectionMatch.Groups[1].Value;

                        var entryMatch = Regex.Match (section, @"Entry\s+(\d+)");
                        if (entryMatch.Success)
                        {
                            int entryNum = int.Parse (entryMatch.Groups[1].Value);
                            currentEntry = new CcdData.Entry();
                            entryMap[entryNum] = currentEntry;
                            data.Entries.Add (currentEntry);
                        }

                        var trackMatch = Regex.Match (section, @"TRACK\s+(\d+)");
                        if (trackMatch.Success)
                        {
                            int trackNum = int.Parse (trackMatch.Groups[1].Value);
                            // Find corresponding entry by Point
                            currentEntry = data.Entries.FirstOrDefault (e => e.Point == trackNum);
                        }

                        continue;
                    }

                    // Parse key=value pairs
                    var kvMatch = Regex.Match (line, @"^(\w+)\s*=\s*(.+)$");
                    if (kvMatch.Success)
                    {
                        var key = kvMatch.Groups[1].Value;
                        var value = kvMatch.Groups[2].Value.Trim();

                        // Disc section
                        if (key == "TocEntries")
                            data.TocEntries = int.Parse (value);
                        else if (key == "Sessions")
                            data.Sessions = int.Parse (value);
                        else if (key == "DataTracksScrambled")
                            data.DataTracksScrambled = int.Parse (value);
                        else if (key == "CDTextLength")
                            data.CDTextLength = int.Parse (value);
                        else if (key == "CATALOG")
                            data.Catalog = value;

                        // Entry section
                        else if (currentEntry != null)
                        {
                            if (key == "Session")
                                currentEntry.Session = int.Parse (value);
                            else if (key == "Point")
                                currentEntry.Point = ParseHexOrDec (value);
                            else if (key == "ADR")
                                currentEntry.ADR = ParseHexOrDec (value);
                            else if (key == "Control")
                                currentEntry.Control = ParseHexOrDec (value);
                            else if (key == "TrackNo")
                                currentEntry.TrackNo = int.Parse (value);
                            else if (key == "AMin")
                                currentEntry.AMin = int.Parse (value);
                            else if (key == "ASec")
                                currentEntry.ASec = int.Parse (value);
                            else if (key == "AFrame")
                                currentEntry.AFrame = int.Parse (value);
                            else if (key == "ALBA")
                                currentEntry.ALBA = int.Parse (value);
                            else if (key == "Zero")
                                currentEntry.Zero = int.Parse (value);
                            else if (key == "PMin")
                                currentEntry.PMin = int.Parse (value);
                            else if (key == "PSec")
                                currentEntry.PSec = int.Parse (value);
                            else if (key == "PFrame")
                                currentEntry.PFrame = int.Parse (value);
                            else if (key == "PLBA")
                                currentEntry.PLBA = int.Parse (value);
                            else if (key == "MODE")
                                currentEntry.Mode = int.Parse (value);
                            else if (key == "INDEX 0")
                                currentEntry.Index0 = int.Parse (value);
                            else if (key == "INDEX 1")
                                currentEntry.Index1 = int.Parse (value);
                            else if (key == "ISRC")
                                currentEntry.ISRC = value;
                        }
                    }
                }
            }

            return data;
        }

        private int ParseHexOrDec (string value)
        {
            if (value.StartsWith ("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt32 (value, 16);
            return int.Parse (value);
        }

        private DiscInfo BuildDiscInfo (CcdData ccdData, ArcView imgFile, bool hasSubchannel)
        {
            var discInfo = new DiscInfo();

            var trackEntries = ccdData.Entries
                .Where (e => e.Point > 0 && e.Point < 99)
                .OrderBy (e => e.Session)
                .ThenBy (e => e.Point)
                .ToList();

            var sessionGroups = trackEntries.GroupBy (e => e.Session);

            long currentOffset = 0;

            foreach (var sessionGroup in sessionGroups)
            {
                var session = new SessionInfo
                {
                    Number = sessionGroup.Key,
                    Mcn = ccdData.Catalog
                };

                // Get session type from 0xA0 entry
                var a0Entry = ccdData.Entries.FirstOrDefault (e => e.Session == sessionGroup.Key && e.Point == 0xA0);
                if (a0Entry != null)
                {
                    switch (a0Entry.PSec)
                    {
                        case 0x00:
                            session.Type = SessionType.CDROM;
                            break;
                        case 0x10:
                            session.Type = SessionType.CDROM; // CDI
                            break;
                        case 0x20:
                            session.Type = SessionType.CDROMXA;
                            break;
                        default:
                            session.Type = SessionType.CDROM;
                            break;
                    }
                }

                var entries = sessionGroup.ToList();
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var nextEntry = i + 1 < entries.Count ? entries[i + 1] : 
                                   ccdData.Entries.FirstOrDefault (e => e.Session == sessionGroup.Key && e.Point == 0xA2);

                    var track = new TrackInfo
                    {
                        Number = entry.Point,
                        Session = entry.Session,
                        StartSector = entry.PLBA,
                        Ctl = (byte)entry.Control,
                        Isrc = entry.ISRC,
                        ImageOffset = currentOffset
                    };

                    // Set pregap from indices
                    if (entry.Index0 > 0 && entry.Index1 > 0)
                        track.Pregap = entry.Index1 - entry.Index0;
                    else if (entry.Index1 > 0)
                        track.Pregap = entry.Index1;

                    // Calculate track length
                    if (nextEntry != null)
                    {
                        int nextPregap = 0;
                        if (nextEntry.Index0 > 0 && nextEntry.Index1 > 0)
                            nextPregap = nextEntry.Index1 - nextEntry.Index0;

                        track.Length = nextEntry.PLBA - entry.PLBA - nextPregap + track.Pregap;
                        track.EndSector = track.StartSector + track.Length - 1;
                    }

                    // Determine track mode by reading first sector
                    track.Mode = DetermineTrackMode (imgFile, currentOffset);

                    // Set sector size
                    track.SectorSize = 2352;
                    track.MainDataSize = 2352;
                    track.SubchannelSize = hasSubchannel ? 96 : 0;
                    track.DataOffset = track.Mode == TrackMode.Audio ? 0 : 16;

                    session.Tracks.Add (track);

                    // Update offset for next track
                    currentOffset += track.Length * track.SectorSize;
                }

                discInfo.Sessions.Add (session);
            }

            return discInfo;
        }

        private TrackMode DetermineTrackMode (ArcView imgFile, long offset)
        {
            // Read first sector of track
            if (imgFile.MaxOffset < offset + 2352)
                return TrackMode.Audio;

            var sectorData = imgFile.View.ReadBytes (offset, 2352);

            bool hasSync = sectorData[0] == 0x00 && sectorData[11] == 0x00 &&
                          Enumerable.Range (1, 10).All (i => sectorData[i] == 0xFF);

            if (!hasSync)
                return TrackMode.Audio;

            // Check mode byte at offset 15
            byte mode = sectorData[15];
            switch (mode)
            {
                case 1:
                    return TrackMode.Mode1;
                case 2:
                    // Check for XA signature
                    if (sectorData[16] == 0x00 && sectorData[17] == 0x00 &&
                        sectorData[18] == 0x08 && sectorData[19] == 0x00 &&
                        sectorData[20] == 0x00 && sectorData[21] == 0x00 &&
                        sectorData[22] == 0x08 && sectorData[23] == 0x00)
                    {
                        return TrackMode.Mode2Mixed;
                    }
                    return TrackMode.Mode2;
                default:
                    return TrackMode.Audio;
            }
        }

        protected override ArcFile CreateArchive (ArcView file, DiscInfo discInfo)
        {
            // For CCD, we need to handle the .sub file specially
            string imgPath = file.Name.EndsWith (".ccd", StringComparison.OrdinalIgnoreCase) 
                ? Path.ChangeExtension (file.Name, ".img") 
                : file.Name;

            // Open the IMG file if we're looking at CCD
            if (!file.Name.Equals (imgPath, StringComparison.OrdinalIgnoreCase))
            {
                var imgEntry = VFS.FindFile (imgPath);
                file = VFS.OpenView (imgEntry);
            }

            return base.CreateArchive (file, discInfo);
        }
    }
}