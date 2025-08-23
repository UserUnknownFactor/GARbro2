using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using GameRes.Formats;
using GameRes.Utility;

namespace GameRes.Formats.DiscImages
{
    /// <summary>
    /// Base class for disc image format plugins
    /// </summary>
    public abstract class DiscImageOpener : ArchiveFormat
    {
        public override bool IsHierarchic { get { return true; } }
        public override bool     CanWrite { get { return false; } }

        // Add encoding settings like 7-Zip
        protected EncodingSetting FileNameEncoding;

        public DiscImageOpener()
        {
            // Default to CP932 (Japanese Shift-JIS) for compatibility
            FileNameEncoding = new EncodingSetting ("DiscImageFileNameCP", "ISO filename encoding",
                                                  Encoding.GetEncoding (932));

            // Add to settings if derived class doesn't override
            if (Settings == null)
                Settings = new[] { FileNameEncoding };
            else
            {
                var list = Settings.ToList();
                list.Add (FileNameEncoding);
                Settings = list.ToArray();
            }
        }

        #region Common Enums and Classes

        public enum TrackMode
        {
            Audio,
            Mode1,
            Mode2,
            Mode2Form1,
            Mode2Form2,
            Mode2Mixed
        }

        public enum SessionType
        {
            CDDA,
            CDROM,
            CDROMXA
        }

        public class TrackInfo
        {
            public int         Number { get; set; }
            public int        Session { get; set; }
            public long   StartSector { get; set; }
            public long     EndSector { get; set; }
            public long        Length { get; set; }
            public long   ImageOffset { get; set; }
            public int     SectorSize { get; set; }
            public int   MainDataSize { get; set; }
            public int SubchannelSize { get; set; }
            public int     DataOffset { get; set; } // Offset within sector to actual data
            public TrackMode     Mode { get; set; }
            public byte         Flags { get; set; }
            public byte           Ctl { get; set; }
            public string        Isrc { get; set; }
            public int         Pregap { get; set; }
            public List<int>  Indices { get; set; } = new List<int>();
            public string       Title { get; set; }

            public bool       IsAudio => Mode == TrackMode.Audio;
            public bool HasSubchannel => SubchannelSize > 0;
        }

        public class SessionInfo
        {
            public int             Number { get; set; }
            public SessionType       Type { get; set; }
            public List<TrackInfo> Tracks { get; set; } = new List<TrackInfo>();
            public string             Mcn { get; set; }
            public int      LeadoutLength { get; set; }
        }

        public class DiscInfo
        {
            public string          MediumType { get; set; } = "CD";
            public string            VolumeId { get; set; }
            public string               Title { get; set; }
            public List<SessionInfo> Sessions { get; set; } = new List<SessionInfo>();
            public byte[]          CdTextData { get; set; }
            public long             TotalSize { get; set; }
        }

        public class CueEntry : Entry
        {
            public override string Type { get { return "audio"; } }
            public string    CueContent { get; set; }
        }

        public class IsoFileEntry : Entry
        {
            public long        IsoStartSector { get; set; }
            public List<TrackInfo> DataTracks { get; set; }
        }

        public class IsoStreamEntry : Entry
        {
            public List<TrackInfo> DataTracks { get; set; }
        }

        #endregion

        #region Abstract Methods

        /// <summary>
        /// Parse disc image and extract disc information
        /// </summary>
        protected abstract DiscInfo ParseDiscImage (ArcView file);

        #endregion

        #region Common Implementation

        public override ArcFile TryOpen (ArcView file)
        {
            try
            {
                var discInfo = ParseDiscImage (file);
                if (discInfo == null || discInfo.Sessions.Count == 0)
                    return null;

                return CreateArchive (file, discInfo);
            }
            catch
            {
                return null;
            }
        }

        protected virtual ArcFile CreateArchive (ArcView file, DiscInfo discInfo)
        {
            bool hasAudio = discInfo.Sessions.Any (s => s.Tracks.Any (t => t.IsAudio));
            bool hasData = discInfo.Sessions.Any (s => s.Tracks.Any (t => !t.IsAudio));

            var entries = new List<Entry>();

            if (hasAudio)
            {
                // Generate CUE file
                string cueContent = GenerateCueSheet (file.Name, discInfo);
                var cueEntry = new CueEntry
                {
                    Name = Path.GetFileNameWithoutExtension (file.Name) + ".cue",
                    Type = "audio",
                    Offset = 0,
                    Size = (uint)Encoding.UTF8.GetByteCount (cueContent),
                    CueContent = cueContent
                };
                entries.Add (cueEntry);

                // Add audio tracks
                foreach (var session in discInfo.Sessions)
                {
                    foreach (var track in session.Tracks.Where (t => t.IsAudio))
                    {
                        var wavEntry = new Entry
                        {
                            Name = $"Track{track.Number:D2}.wav",
                            Type = "audio",
                            Offset = track.ImageOffset,
                            Size = (uint)(track.Length * 2352 + 44) // Audio data + WAV header
                        };
                        entries.Add (wavEntry);
                    }
                }

                return new DiscImageArchive (file, this, entries, discInfo);
            }
            else if (hasData)
            {
                return ConvertToIso (file, discInfo);
            }

            return null;
        }

        protected virtual string GenerateCueSheet (string imagePath, DiscInfo discInfo)
        {
            var sb = new StringBuilder();
            string fileName = Path.GetFileName (imagePath);

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

                    AppendCueFlags (sb, track);
                    AppendCueIndices (sb, track);
                }
            }

            return sb.ToString();
        }

        protected virtual string GetCueTrackType (TrackInfo track)
        {
            if (track.IsAudio)
                return "AUDIO";

            switch (track.Mode)
            {
                case TrackMode.Mode1:
                    return "MODE1/2352";
                case TrackMode.Mode2:
                case TrackMode.Mode2Mixed:
                    return "MODE2/2352";
                default:
                    return "MODE1/2352";
            }
        }

        protected virtual void AppendCueFlags (StringBuilder sb, TrackInfo track)
        {
            if ((track.Ctl & 0x01) != 0 || (track.Flags & 0x01) != 0)
                sb.AppendLine ("    FLAGS PRE");
            if ((track.Ctl & 0x04) != 0 || (track.Flags & 0x04) != 0)
                sb.AppendLine ("    FLAGS DCP");
            if ((track.Ctl & 0x08) != 0 || (track.Flags & 0x08) != 0)
                sb.AppendLine ("    FLAGS 4CH");
        }

        protected virtual void AppendCueIndices (StringBuilder sb, TrackInfo track)
        {
            // INDEX 00 if there's pregap
            if (track.Pregap > 0)
            {
                long pregapLba = track.StartSector - track.Pregap;
                AppendCueMsf (sb, "INDEX 00", pregapLba);
            }

            // INDEX 01
            AppendCueMsf (sb, "INDEX 01", track.StartSector);

            // Additional indices
            if (track.Indices.Count > 1)
            {
                long currentPos = track.StartSector;
                for (int i = 1; i < track.Indices.Count; i++)
                {
                    currentPos += track.Indices[i];
                    AppendCueMsf (sb, $"INDEX {i + 1:D2}", currentPos);
                }
            }
        }

        protected void AppendCueMsf (StringBuilder sb, string prefix, long lba)
        {
            int minutes = (int)(lba / 75 / 60);
            int seconds = (int)((lba / 75) % 60);
            int frames = (int)(lba % 75);
            sb.AppendLine ($"    {prefix} {minutes:D2}:{seconds:D2}:{frames:D2}");
        }

        protected virtual ArcFile ConvertToIso (ArcView file, DiscInfo discInfo)
        {
            var dataTracks = discInfo.Sessions
                .SelectMany (s => s.Tracks)
                .Where (t => !t.IsAudio)
                .OrderBy (t => t.StartSector)
                .ToList();

            if (dataTracks.Count == 0)
                return null;

            // Check multiple possible locations for ISO signature
            byte[] testSector = null;
            bool foundIso = false;

            // Try sector 16 first (standard ISO location)
            testSector = ReadIsoSector (file, dataTracks, 16);
            if (testSector != null)
            {
                // Check for Volume Descriptor type and signature
                if ((testSector[0] == 0x01 || testSector[0] == 0x00) && CheckIsoSignature (testSector, 1))
                {
                    foundIso = true;
                }
            }

            if (foundIso)
            {
                // It's an ISO filesystem, parse it directly
                var entries = ParseIsoFileSystem (file, dataTracks);
                if (entries != null && entries.Count > 0)
                    return new DiscImageArchive (file, this, entries, discInfo);
            }

            // Not an ISO or parsing failed - present as raw disc image
            var isoEntry = new IsoStreamEntry
            {
                Name = Path.GetFileNameWithoutExtension (file.Name) + ".iso",
                Type = "data",
                Offset = 0,
                Size = (uint)Math.Min (dataTracks.Sum (t => t.Length * 2048L), uint.MaxValue),
                DataTracks = dataTracks
            };

            return new DiscImageArchive (file, this, new List<Entry> { isoEntry }, discInfo);
        }

        protected List<Entry> ParseIsoFileSystem (ArcView file, List<TrackInfo> dataTracks)
        {
            // For UDF bridge format (BEA01), PVD might be at sector 256 or later
            byte[] pvd = null;
            int pvdSector = -1;

            // Check if this is UDF (BEA01 at sector 16)
            var sector16 = ReadIsoSector (file, dataTracks, 16);
            if (sector16 != null && CheckIsoSignature (sector16, 1))
            {
                // Unsupported here if it's UDF
                if (sector16[1] == 'B' && sector16[2] == 'E' && sector16[3] == 'A' &&
                        sector16[4] == '0' && sector16[5] == '1') 
                    return null;
            }

            // Standard ISO - search from sector 16
            for (int sector = 16; sector < 32; sector++)
            {
                var testSector = ReadIsoSector (file, dataTracks, sector);
                if (testSector == null)
                    break;

                // Check for PVD (type 0x01)
                if (testSector[0] == 0x01 && CheckIsoSignature (testSector, 1))
                {
                    pvd = testSector;
                    pvdSector = sector;
                    break;
                }

                // Check for Boot Volume Descriptor (type 0x00)
                if (testSector[0] == 0x00 && CheckIsoSignature (testSector, 1))
                {
                    // Continue searching for PVD
                    continue;
                }

                // Check for terminator
                if (testSector[0] == 0xFF)
                    break;
            }

            if (pvd == null)
                return null;

            // Get root directory location
            uint rootDirLba = BitConverter.ToUInt32 (pvd, 158);
            uint rootDirSize = BitConverter.ToUInt32 (pvd, 166);

            // Check for Joliet SVD (after PVD)
            Encoding encoding = FileNameEncoding.Get<Encoding>();

            int searchStart = pvdSector + 1;
            int searchEnd = 32;

            for (int sector = searchStart; sector < searchEnd; sector++)
            {
                var svd = ReadIsoSector (file, dataTracks, sector);
                if (svd == null)
                    break;

                if (svd[0] == 0xFF) // Volume descriptor terminator
                    break;

                if (svd[0] == 0x02 && CheckIsoSignature (svd, 1))
                {
                    // Check for Joliet escape sequences
                    if ((svd[88] == 0x25 && svd[89] == 0x2F &&
                        (svd[90] == 0x40 || svd[90] == 0x43 || svd[90] == 0x45)))
                    {
                        // Use Joliet directory structure instead
                        rootDirLba = BitConverter.ToUInt32 (svd, 158);
                        rootDirSize = BitConverter.ToUInt32 (svd, 166);
                        // Joliet uses UCS-2 (UTF-16BE)
                        encoding = Encoding.BigEndianUnicode;
                        break;
                    }
                }
            }

            var entries = new List<Entry>();
            ParseIsoDirectory (file, dataTracks, rootDirLba, rootDirSize, "", entries, encoding);

            return entries;
        }

        protected void ParseIsoDirectory(
            ArcView file, List<TrackInfo> tracks, uint dirLba, uint dirSize, 
            string path, List<Entry> entries, Encoding encoding )
        {
            var dirData = ReadIsoSectors (file, tracks, dirLba, dirSize);
            if (dirData == null)
                return;

            int offset = 0;
            while (offset < dirData.Length)
            {
                byte recordLength = dirData[offset];
                if (recordLength == 0)
                {
                    // Move to next sector boundary
                    offset = ((offset / 2048) + 1) * 2048;
                    if (offset >= dirData.Length)
                        break;
                    continue;
                }

                // Parse directory record
                byte nameLength = dirData[offset + 32];
                uint fileLba    = BitConverter.ToUInt32 (dirData, offset + 2);
                uint fileSize   = BitConverter.ToUInt32 (dirData, offset + 10);
                byte fileFlags  = dirData[offset + 25];

                string name;
                if (encoding == Encoding.BigEndianUnicode) // Joliet, names are UTF-16BE
                    name = encoding.GetString (dirData, offset + 33, nameLength);
                else                                       // Regular ISO 9660 - use configured encoding
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
                    ParseIsoDirectory (file, tracks, fileLba, fileSize, fullPath, entries, encoding);
                else                         // File
                {
                    var entry = new IsoFileEntry
                    {
                        Name = fullPath,
                        Type = FormatCatalog.Instance.GetTypeFromName (name),
                        Offset = 0, // Not used
                        Size = fileSize,
                        IsoStartSector = fileLba,
                        DataTracks = tracks
                    };
                    entries.Add (entry);
                }

                offset += recordLength;
            }
        }

        private string RemoveIsoVersionSuffix (string name)
        {
            // ISO 9660 version numbers are in format ";nnnnn" where n is decimal digit
            // The version number is always at the end of the filename
            int semicolon = name.LastIndexOf(';');

            if (semicolon >= 0 && semicolon < name.Length - 1)
            {
                // Check if everything after semicolon is digits (version number)
                bool isVersion = true;
                for (int i = semicolon + 1; i < name.Length; i++)
                {
                    if (!char.IsDigit (name[i]))
                    {
                        isVersion = false;
                        break;
                    }
                }

                if (isVersion) // Remove the version suffix
                    return name.Substring (0, semicolon);
            }

            return name;
        }

        protected string DecodeIsoFilename (byte[] data, int offset, int length, Encoding encoding)
        {
            // ISO 9660 filenames can have special encoding
            // Check if it's pure ASCII first
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
            else // Use configured encoding for non-ASCII names
                return encoding.GetString (data, offset, length);
        }

        protected byte[] ReadIsoSector (ArcView file, List<TrackInfo> tracks, long lba)
        {
            // Find which track contains this LBA
            long currentLba = 0;
            foreach (var track in tracks)
            {
                if (lba >= currentLba && lba < currentLba + track.Length)
                {
                    long sectorInTrack = lba - currentLba;
                    long offset = track.ImageOffset + sectorInTrack * track.SectorSize + track.DataOffset;

                    using (var frame = file.CreateFrame())
                    {
                        return frame.ReadBytes (offset, 2048);
                    }
                }
                currentLba += track.Length;
            }
            return null;
        }

        protected byte[] ReadIsoSectors (ArcView file, List<TrackInfo> tracks, uint startLba, uint size)
        {
            int numSectors = (int)((size + 2047) / 2048);
            var data = new byte[numSectors * 2048];

            for (int i = 0; i < numSectors; i++)
            {
                var sector = ReadIsoSector (file, tracks, startLba + i);
                if (sector == null)
                    return null;
                Array.Copy (sector, 0, data, i * 2048, 2048);
            }

            return data;
        }

        protected static readonly byte[] CD001_SIGNATURE = { (byte)'C', (byte)'D', (byte)'0', (byte)'0', (byte)'1' };
        protected static readonly byte[] BEA01_SIGNATURE = { (byte)'B', (byte)'E', (byte)'A', (byte)'0', (byte)'1' };

        protected bool CheckIsoSignature (byte[] sector, int offset)
        {
            if (sector == null || sector.Length < offset + 5)
                return false;

            for (int i = 0; i < 5; i++)
            {
                if (sector[offset + i] != CD001_SIGNATURE[i])
                    return false;
            }

            return true;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var isoStreamEntry = entry as IsoStreamEntry;
            if (isoStreamEntry != null)
                return new DiscImageToIsoStream (arc.File, isoStreamEntry.DataTracks);

            var cueEntry = entry as CueEntry;
            if (cueEntry != null)
            {
                var bytes = Encoding.UTF8.GetBytes (cueEntry.CueContent);
                return new MemoryStream (bytes);
            }

            var discArc = arc as DiscImageArchive;
            if (discArc != null && entry.Type == "audio" && entry.Name.EndsWith (".wav"))
            {
                string trackNumStr = Path.GetFileNameWithoutExtension (entry.Name).Replace ("Track", "");
                if (int.TryParse (trackNumStr, out int trackNum))
                {
                    var track = discArc.DiscInfo.Sessions
                        .SelectMany (s => s.Tracks)
                        .FirstOrDefault (t => t.Number == trackNum && t.IsAudio);

                    if (track != null)
                        return new DiscImageAudioStream (discArc.File, track);
                }
            }

            // Handle ISO file entries (files within ISO filesystem)
            var isoEntry = entry as IsoFileEntry;
            if (isoEntry != null)
                return new IsoFileStream(
                    arc.File, isoEntry.DataTracks, isoEntry.IsoStartSector, isoEntry.Size);

            return base.OpenEntry (arc, entry);
        }

        #endregion

        #region Helper Methods

        protected static byte Bcd2Hex (byte bcd)
        {
            return (byte)(((bcd >> 4) * 10) + (bcd & 0x0F));
        }

        protected static int Msf2Lba (byte m, byte s, byte f, bool includeLeadin = false)
        {
            int lba = (m * 60 + s) * 75 + f;
            return includeLeadin ? lba : lba - 150;
        }

        protected static void LbaToMsf (long lba, out byte m, out byte s, out byte f)
        {
            lba += 150; // Add lead-in
            f = (byte)(lba % 75);
            lba /= 75;
            s = (byte)(lba % 60);
            m = (byte)(lba / 60);
        }

        protected T BytesToStruct<T>(byte[] data, int offset = 0) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            if (offset + size > data.Length)
                throw new ArgumentException ("Not enough data to read structure");

            IntPtr ptr = Marshal.AllocHGlobal (size);
            try
            {
                Marshal.Copy (data, offset, ptr, size);
                return Marshal.PtrToStructure<T>(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal (ptr);
            }
        }

        public static byte[] CreateWavHeader (long dataSize)
        {
            var header = new byte[44];

            // RIFF header
            Encoding.ASCII.GetBytes ("RIFF").CopyTo (header, 0);
            LittleEndian.Pack ((uint)(dataSize + 36), header, 4);
            Encoding.ASCII.GetBytes ("WAVE").CopyTo (header, 8);

            // fmt chunk
            Encoding.ASCII.GetBytes ("fmt ").CopyTo (header, 12);
            LittleEndian.Pack (16u,        header, 16);      // Chunk size
            LittleEndian.Pack ((ushort)1,  header, 20);      // PCM format
            LittleEndian.Pack ((ushort)2,  header, 22);      // 2 channels (stereo)
            LittleEndian.Pack (44100u,     header, 24);      // Sample rate
            LittleEndian.Pack (44100u * 2 * 2, header, 28);  // Byte rate
            LittleEndian.Pack ((ushort)4,  header, 32);      // Block align
            LittleEndian.Pack ((ushort)16, header, 34);      // Bits per sample

            // data chunk
            Encoding.ASCII.GetBytes ("data").CopyTo (header, 36);
            LittleEndian.Pack ((uint)dataSize, header, 40);

            return header;
        }

        #endregion
    }

    /// <summary>
    /// Archive implementation for disc images
    /// </summary>
    public class DiscImageArchive : ArcFile
    {
        public DiscImageOpener.DiscInfo DiscInfo { get; private set; }

        public DiscImageArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir,
                                DiscImageOpener.DiscInfo discInfo)
            : base (arc, impl, dir)
        {
            DiscInfo = discInfo;
        }
    }

    /// <summary>
    /// Stream that converts disc image data tracks to ISO format
    /// </summary>
    public class DiscImageToIsoStream : Stream
    {
        private readonly ArcView m_file;
        private readonly List<DiscImageOpener.TrackInfo> m_dataTracks;
        private readonly long m_length;
        private long m_position;

        public DiscImageToIsoStream (ArcView file, List<DiscImageOpener.TrackInfo> dataTracks)
        {
            m_file = file;
            m_dataTracks = dataTracks;
            m_length = dataTracks.Sum (t => t.Length * 2048L);
            m_position = 0;
        }

        public override long Length => m_length;
        public override long Position
        {
            get => m_position;
            set => m_position = Math.Max (0, Math.Min (value, Length));
        }

        public override bool  CanRead => true;
        public override bool  CanSeek => true;
        public override bool CanWrite => false;

        public override int Read (byte[] buffer, int offset, int count)
        {
            int totalRead = 0;

            while (count > 0 && m_position < Length)
            {
                // Find current track and sector
                long currentIsoSector = m_position / 2048;
                int sectorOffset = (int)(m_position % 2048);

                // Find track containing this ISO sector
                long isoSectorCount = 0;
                DiscImageOpener.TrackInfo currentTrack = null;
                long trackIsoStart = 0;

                foreach (var track in m_dataTracks)
                {
                    if (currentIsoSector < isoSectorCount + track.Length)
                    {
                        currentTrack = track;
                        trackIsoStart = isoSectorCount;
                        break;
                    }
                    isoSectorCount += track.Length;
                }

                if (currentTrack == null)
                    break;

                // Calculate file position
                long trackSector = currentIsoSector - trackIsoStart;
                long fileOffset = currentTrack.ImageOffset +
                                 trackSector * currentTrack.SectorSize +
                                 currentTrack.DataOffset +
                                 sectorOffset;

                // Read data
                int available = 2048 - sectorOffset;
                int toRead = Math.Min (count, available);

                using (var frame = m_file.CreateFrame())
                {
                    frame.Reserve (fileOffset, toRead);
                    int read = frame.Read (fileOffset, buffer, offset, toRead);

                    offset += read;
                    count -= read;
                    m_position += read;
                    totalRead += read;

                    if (read < toRead)
                        break;
                }
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
                    Position = Length + offset;
                    break;
            }
            return m_position;
        }

        public override void Flush() { }
        public override void SetLength (long value) { throw new NotSupportedException(); }
        public override void Write (byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }

    /// <summary>
    /// Stream that represents an audio track as a WAV file
    /// </summary>
    public class DiscImageAudioStream : Stream
    {
        private readonly ArcView m_file;
        private readonly DiscImageOpener.TrackInfo m_track;
        private readonly byte[]  m_wavHeader;
        private long             m_position;
        private readonly long    m_dataSize;
        private readonly long    m_totalSize;

        public DiscImageAudioStream (ArcView file, DiscImageOpener.TrackInfo track)
        {
            m_file = file;
            m_track = track;
            m_position = 0;

            m_dataSize  = track.Length * 2352;
            m_totalSize = m_dataSize + 44; // WAV header => 44 bytes

            m_wavHeader = DiscImageOpener.CreateWavHeader (m_dataSize);
        }

        public override bool  CanRead => true;
        public override bool  CanSeek => true;
        public override bool CanWrite => false;
        public override long   Length => m_totalSize;
        public override long Position
        {
            get => m_position;
            set => m_position = Math.Max (0, Math.Min (value, Length));
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int totalRead = 0;

            // Read WAV header if needed
            if (m_position < 44)
            {
                int headerBytes = Math.Min (count, (int)(44 - m_position));
                Array.Copy (m_wavHeader, (int)m_position, buffer, offset, headerBytes);
                m_position += headerBytes;
                offset += headerBytes;
                count -= headerBytes;
                totalRead += headerBytes;
            }

            // Read audio data
            if (count > 0 && m_position >= 44 && m_position < m_totalSize)
            {
                long audioPosition = m_position - 44;
                long remainingData = m_dataSize - audioPosition;
                int toRead = (int)Math.Min (count, remainingData);

                // Calculate which sector we're in
                long sectorIndex = audioPosition / 2352;
                int sectorOffset = (int)(audioPosition % 2352);

                // Read from file
                long fileOffset = m_track.ImageOffset +
                                 sectorIndex * m_track.SectorSize +
                                 m_track.DataOffset +
                                 sectorOffset;

                using (var frame = m_file.CreateFrame())
                {
                    frame.Reserve (fileOffset, toRead);
                    int read = frame.Read (fileOffset, buffer, offset, toRead);

                    m_position += read;
                    totalRead += read;
                }
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
                    Position = Length + offset;
                    break;
            }
            return m_position;
        }

        public override void Flush() { }
        public override void SetLength (long value) { throw new NotSupportedException(); }
        public override void Write (byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }

    /// <summary>
    /// Stream for reading files from ISO filesystem
    /// </summary>
    public class IsoFileStream : Stream
    {
        private readonly ArcView m_file;
        private readonly List<DiscImageOpener.TrackInfo> m_tracks;
        private readonly long    m_startSector;
        private readonly long    m_length;
        private long             m_position;

        public IsoFileStream (ArcView file, List<DiscImageOpener.TrackInfo> tracks,
                           long startSector, long length)
        {
            m_file        = file;
            m_tracks      = tracks;
            m_startSector = startSector;
            m_length      = length;
            m_position    = 0;
        }

        public override bool  CanRead => true;
        public override bool  CanSeek => true;
        public override bool CanWrite => false;
        public override long   Length => m_length;
        public override long Position
        {
            get => m_position;
            set => m_position = Math.Max (0, Math.Min (value, Length));
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            if (m_position >= m_length)
                return 0;

            count = (int)Math.Min (count, m_length - m_position);
            int totalRead = 0;

            while (count > 0)
            {
                // Calculate current sector and offset within sector
                long fileSector = m_position / 2048;
                int sectorOffset = (int)(m_position % 2048);
                long isoSector = m_startSector + fileSector;

                // Find track containing this sector
                long currentLba = 0;
                DiscImageOpener.TrackInfo track = null;
                long trackStartLba = 0;

                foreach (var t in m_tracks)
                {
                    if (isoSector >= currentLba && isoSector < currentLba + t.Length)
                    {
                        track = t;
                        trackStartLba = currentLba;
                        break;
                    }
                    currentLba += t.Length;
                }

                if (track == null)
                    break;

                // Calculate file offset
                long sectorInTrack = isoSector - trackStartLba;
                long fileOffset = track.ImageOffset +
                                sectorInTrack * track.SectorSize +
                                track.DataOffset +
                                sectorOffset;

                // Read data
                int available = 2048 - sectorOffset;
                int toRead = Math.Min (count, available);

                using (var frame = m_file.CreateFrame())
                {
                    frame.Reserve (fileOffset, toRead);
                    int read = frame.Read (fileOffset, buffer, offset, toRead);

                    offset += read;
                    count -= read;
                    m_position += read;
                    totalRead += read;

                    if (read < toRead)
                        break;
                }
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
                    Position = Length + offset;
                    break;
            }
            return m_position;
        }

        public override void Flush () { }
        public override void SetLength (long value) { throw new NotSupportedException(); }
        public override void Write (byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
}