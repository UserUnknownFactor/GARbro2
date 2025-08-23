using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Formats.DiscImages;

namespace GameRes.Formats.Iso
{
    /// <summary>
    /// Pure ISO 9660/Joliet format handler
    /// </summary>
    [Export(typeof(ArchiveFormat))]
    [ExportMetadata("Priority", 50)]
    public class IsoOpener : DiscImageOpener
    {
        public override string         Tag { get { return "ISO"; } }
        public override string Description { get { return "ISO 9660 CD-ROM Image"; } }
        public override uint     Signature { get { return  0; } }
        public override bool      CanWrite { get { return  true; } }

        private readonly (int sectorSize, int dataOffset)[] ValidSectorSizes = new[]
        {
            (2048,  0), // 2048-byte Mode 1 / Mode 2 Form 1 sector; offset: 0
            (2332,  8), // 2332-byte Mode 2 Form 1 sector; offset: 8 (sub-header)
            (2336,  8), // 2336-byte Mode 2 Form 1 sector: offset: 8 (sub-header)
            (2352, 16), // 2352-byte Mode 1 sector; offset: 16 (sync+header)
            (2352, 24), // 2352-byte Mode 2 Form 1 sector; offset: 24 (sync+header+sub-header)
        };

        private static readonly byte[] PATTERN_GAMECUBE = { 0xC2, 0x33, 0x9F, 0x3D };
        private static readonly byte[]      PATTERN_WII = { 0x5D, 0x1C, 0x9E, 0xA3 };

        public IsoOpener()
        {
            Extensions = new string[] { "iso", "bin", "img" };
        }

    protected override ArcFile CreateArchive (ArcView file, DiscInfo discInfo)
    {
        var dataTracks = discInfo.Sessions
            .SelectMany (s => s.Tracks)
            .Where (t => !t.IsAudio)
            .ToList();

        if (dataTracks.Count == 0)
        {
            // No data tracks - check for audio
            if (discInfo.Sessions.Any (s => s.Tracks.Any (t => t.IsAudio)))
                return base.CreateArchive (file, discInfo); // Let base handle it
            return null;
        }

        var entries = ParseIsoFileSystem (file, dataTracks);

        if (entries == null || entries.Count == 0)
            return null;

        return new DiscImageArchive (file, this, entries, discInfo);
    }

        protected override DiscInfo ParseDiscImage (ArcView file)
        {
            long fileLength = file.MaxOffset;

            // Need at least 4 seconds worth of data (minimum track size per INF8090)
            if (fileLength < 4 * 75 * 2048)
                return null;

            var format = DetermineExactFormat (file);
            if (!format.HasValue)
                return null;

            var (sectorSize, subchannelSize, dataOffset, mode) = format.Value;
            long numSectors = fileLength / (sectorSize + subchannelSize);

            var discInfo = new DiscInfo
            {
                MediumType = "CD"
            };

            var track = new TrackInfo
            {
                Number = 1,
                Session = 1,
                StartSector = 0,
                EndSector = numSectors - 1,
                Length = numSectors,
                ImageOffset = 0,
                SectorSize = sectorSize + subchannelSize,
                MainDataSize = sectorSize,
                SubchannelSize = subchannelSize,
                DataOffset = dataOffset,
                Mode = mode,
                Ctl = (byte)(mode == TrackMode.Audio ? 0x00 : 0x04)
            };

            var session = new SessionInfo
            {
                Number = 1,
                Type = mode == TrackMode.Audio ? SessionType.CDDA : SessionType.CDROM
            };

            session.Tracks.Add (track);
            discInfo.Sessions.Add (session);
            discInfo.TotalSize = fileLength;

            return discInfo;
        }

        private (int sectorSize, int subchannelSize, int dataOffset, TrackMode mode)? DetermineExactFormat (ArcView file)
        {
            long fileLength = file.MaxOffset;
            int[] subchannelSizes = { 0, 16, 96 };

            if (fileLength % 2048 == 0 && CheckNintendoFormat (file))
            {
                return (2048, 0, 0, TrackMode.Mode1);
            }

            foreach (int subchannelSize in subchannelSizes)
            {
                foreach (var (sectorSize, dataOffset) in ValidSectorSizes)
                {
                    int fullSectorSize = sectorSize + subchannelSize;

                    if (fileLength % fullSectorSize != 0)
                        continue;

                    long offset = 16L * fullSectorSize + dataOffset;
                    if (offset + 8 > fileLength)
                        continue;

                    var buffer = new byte[8];
                    file.View.Read (offset, buffer, 0, 8);

                    // Check for ISO/UDF signatures using base class method
                    if (CheckIsoSignature (buffer, 0) ||
                        (buffer[0] <= 0x02 && CheckIsoSignature (buffer, 1)))
                    {
                        TrackMode mode = DetermineTrackMode (file, sectorSize, dataOffset, fullSectorSize);
                        return (sectorSize, subchannelSize, dataOffset, mode);
                    }
                }
            }

            // Check if it's raw audio (2352-byte sectors)
            if (fileLength % 2352 == 0 && fileLength >= 4 * 75 * 2352)
                return (2352, 0, 0, TrackMode.Audio);

            return null;
        }

        private bool CheckNintendoFormat (ArcView file)
        {
            var buffer = new byte[4];

            file.View.Read (0x1C, buffer, 0, 4);
            if (buffer.SequenceEqual (PATTERN_GAMECUBE))
                return true;

            file.View.Read (0x18, buffer, 0, 4);
            if (buffer.SequenceEqual (PATTERN_WII))
                return true;

            return false;
        }

        private TrackMode DetermineTrackMode (ArcView file, int sectorSize, int dataOffset, int fullSectorSize)
        {
            switch (sectorSize)
            {
                case 2048:
                    return TrackMode.Mode1;

                case 2332:
                case 2336:
                    return TrackMode.Mode2Mixed;

                case 2352:
                    // Read sector 16 to determine exact mode
                    var buffer = new byte[16];
                    long offset = 16L * fullSectorSize;

                    if (file.View.Read (offset, buffer, 0, 16) == 16)
                    {
                        bool hasSync = buffer[0] == 0x00 && buffer[11] == 0x00 &&
                                      Enumerable.Range (1, 10).All (i => buffer[i] == 0xFF);

                        if (hasSync && buffer.Length > 15)
                        {
                            // Check mode byte at offset 15
                            switch (buffer[15])
                            {
                                case 0: return TrackMode.Audio;
                                case 1: return TrackMode.Mode1;
                                case 2: return TrackMode.Mode2Mixed;
                            }
                        }

                        // No sync pattern - likely audio
                        if (!hasSync)
                            return TrackMode.Audio;
                    }
                    return TrackMode.Mode1;

                default:
                    return TrackMode.Mode1;
            }
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                   EntryCallback callback)
        {
            var encoding = FileNameEncoding.Get<Encoding>();
            var builder = new IsoBuilder (output, encoding);

            int fileCount = 0;
            int totalFiles = list.Count();

            foreach (var entry in list)
            {
                if (callback != null)
                    callback (fileCount++, entry, "Adding file");

                using (var input = VFS.OpenStream (entry))
                {
                    builder.AddFile (entry.Name, input, (uint)entry.Size);
                }
            }

            builder.WriteIso();
        }

        /// <summary>
        /// ISO 9660/Joliet builder for creating ISO files
        /// </summary>
        internal class IsoBuilder
        {
            private Stream m_output;
            private Encoding m_encoding;
            private List<IsoFileInfo> m_files = new List<IsoFileInfo>();
            private Dictionary<string, IsoDirectoryInfo> m_directories = new Dictionary<string, IsoDirectoryInfo>();
            private IsoDirectoryInfo m_root;
            private uint m_currentSector = 18; // Start after system area and volume descriptors

            private class IsoFileInfo
            {
                public string Path { get; set; }
                public string Name { get; set; }
                public Stream Stream { get; set; }
                public uint Size { get; set; }
                public uint StartSector { get; set; }
                public IsoDirectoryInfo Parent { get; set; }
            }

            private class IsoDirectoryInfo
            {
                public string Path { get; set; }
                public string Name { get; set; }
                public uint StartSector { get; set; }
                public uint Size { get; set; }
                public IsoDirectoryInfo Parent { get; set; }
                public List<IsoDirectoryInfo> Subdirectories { get; set; } = new List<IsoDirectoryInfo>();
                public List<IsoFileInfo> Files { get; set; } = new List<IsoFileInfo>();

                public uint RecordSize
                {
                    get
                    {
                        uint size = 0;
                        // Self and parent entries
                        size += 34 * 2;

                        // Subdirectories
                        foreach (var dir in Subdirectories)
                        {
                            size += (uint)(33 + dir.Name.Length);
                            if (dir.Name.Length % 2 == 0) size++; // Padding
                        }

                        // Files
                        foreach (var file in Files)
                        {
                            size += (uint)(33 + file.Name.Length);
                            if (file.Name.Length % 2 == 0) size++; // Padding
                        }

                        // Round up to sector boundary
                        return ((size + 2047) / 2048) * 2048;
                    }
                }
            }

            public IsoBuilder (Stream output, Encoding encoding)
            {
                m_output = output;
                m_encoding = encoding ?? Encoding.GetEncoding (932);
                m_root = new IsoDirectoryInfo { Path = "", Name = "", StartSector = 18 };
                m_directories[""] = m_root;
            }

            public void AddFile (string path, Stream stream, uint size)
            {
                path = path.Replace('\\', '/');

                int lastSlash = path.LastIndexOf('/');
                string dirPath = lastSlash >= 0 ? path.Substring (0, lastSlash) : "";
                string fileName = lastSlash >= 0 ? path.Substring (lastSlash + 1) : path;

                var dir = EnsureDirectory (dirPath);

                var fileInfo = new IsoFileInfo
                {
                    Path = path,
                    Name = fileName,
                    Stream = stream,
                    Size = size,
                    Parent = dir
                };

                m_files.Add (fileInfo);
                dir.Files.Add (fileInfo);
            }

            private IsoDirectoryInfo EnsureDirectory (string path)
            {
                if (m_directories.ContainsKey (path))
                    return m_directories[path];

                // Create parent directories
                var parts = path.Split (new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                string currentPath = "";
                IsoDirectoryInfo parent = m_root;

                foreach (var part in parts)
                {
                    if (!string.IsNullOrEmpty (currentPath))
                        currentPath += "/";
                    currentPath += part;

                    if (!m_directories.ContainsKey (currentPath))
                    {
                        var dir = new IsoDirectoryInfo
                        {
                            Path = currentPath,
                            Name = part,
                            Parent = parent
                        };

                        m_directories[currentPath] = dir;
                        parent.Subdirectories.Add (dir);
                    }

                    parent = m_directories[currentPath];
                }

                return parent;
            }

            public void WriteIso()
            {
                // Reserve space for system area (16 sectors)
                WriteZeros (16 * 2048);

                // Assign sectors to directories and files
                AssignSectors();

                // Write Primary Volume Descriptor
                WritePrimaryVolumeDescriptor();

                // Write Joliet Supplementary Volume Descriptor
                WriteJolietVolumeDescriptor();

                // Write Volume Descriptor Set Terminator
                WriteVolumeDescriptorTerminator();

                // Write directories
                WriteDirectories (m_root);

                // Write files
                WriteFiles();

                // Pad to complete size
                long currentSize = m_output.Position;
                long finalSize = ((currentSize + 2047) / 2048) * 2048;
                WriteZeros ((int)(finalSize - currentSize));
            }

            private void AssignSectors()
            {
                // Assign sectors to directories first (depth-first)
                AssignDirectorySectors (m_root);

                // Assign sectors to files
                foreach (var file in m_files)
                {
                    file.StartSector = m_currentSector;
                    uint sectors = (file.Size + 2047) / 2048;
                    m_currentSector += sectors;
                }
            }

            private void AssignDirectorySectors (IsoDirectoryInfo dir)
            {
                dir.StartSector = m_currentSector;
                dir.Size = dir.RecordSize;
                m_currentSector += dir.Size / 2048;

                foreach (var subdir in dir.Subdirectories)
                {
                    AssignDirectorySectors (subdir);
                }
            }

            private void WritePrimaryVolumeDescriptor()
            {
                var pvd = new byte[2048];

                pvd[0] = 0x01; // Type
                Encoding.ASCII.GetBytes ("CD001").CopyTo (pvd, 1);
                pvd[6] = 0x01; // Version

                // System identifier
                PadAsciiString ("WIN32", 32).CopyTo (pvd, 8);

                // Volume identifier
                PadAsciiString ("CDROM", 32).CopyTo (pvd, 40);

                // Volume space size
                WriteBothEndian32 (pvd, 80, m_currentSector);

                // Volume set size
                WriteBothEndian16 (pvd, 120, 1);

                // Volume sequence number
                WriteBothEndian16 (pvd, 124, 1);

                // Logical block size
                WriteBothEndian16 (pvd, 128, 2048);

                // Root directory record
                WriteDirectoryRecord (pvd, 156, m_root, true, false);

                // Dates
                var now = DateTime.Now;
                WriteVolumeDate (pvd, 813, now);
                WriteVolumeDate (pvd, 830, now);

                pvd[881] = 0x01; // File structure version

                m_output.Write (pvd, 0, 2048);
            }

            private void WriteJolietVolumeDescriptor()
            {
                var svd = new byte[2048];

                svd[0] = 0x02; // Supplementary
                Encoding.ASCII.GetBytes ("CD001").CopyTo (svd, 1);
                svd[6] = 0x01; // Version

                // Joliet escape sequences
                svd[88] = 0x25;
                svd[89] = 0x2F;
                svd[90] = 0x45; // Level 3

                // Most fields similar to PVD but with UCS-2 encoding
                WriteBothEndian32 (svd, 80, m_currentSector);
                WriteBothEndian16 (svd, 120, 1);
                WriteBothEndian16 (svd, 124, 1);
                WriteBothEndian16 (svd, 128, 2048);

                WriteDirectoryRecord (svd, 156, m_root, true, true);

                var now = DateTime.Now;
                WriteVolumeDate (svd, 813, now);
                WriteVolumeDate (svd, 830, now);

                svd[881] = 0x01;

                m_output.Write (svd, 0, 2048);
            }

            private void WriteVolumeDescriptorTerminator()
            {
                var term = new byte[2048];
                term[0] = 0xFF;
                Encoding.ASCII.GetBytes ("CD001").CopyTo (term, 1);
                term[6] = 0x01;
                m_output.Write (term, 0, 2048);
            }

            private void WriteDirectories (IsoDirectoryInfo dir)
            {
                m_output.Seek (dir.StartSector * 2048, SeekOrigin.Begin);

                var buffer = new byte[dir.Size];
                int offset = 0;

                // Self entry
                offset += WriteDirectoryRecord (buffer, offset, dir, true, false);

                // Parent entry
                offset += WriteDirectoryRecord (buffer, offset, dir.Parent ?? dir, true, false);

                // Subdirectories
                foreach (var subdir in dir.Subdirectories.OrderBy (d => d.Name))
                {
                    offset += WriteDirectoryRecord (buffer, offset, subdir, false, false);
                }

                // Files
                foreach (var file in dir.Files.OrderBy (f => f.Name))
                {
                    offset += WriteFileRecord (buffer, offset, file);
                }

                m_output.Write (buffer, 0, buffer.Length);

                // Recursively write subdirectories
                foreach (var subdir in dir.Subdirectories)
                {
                    WriteDirectories (subdir);
                }
            }

            private void WriteFiles()
            {
                foreach (var file in m_files)
                {
                    m_output.Seek (file.StartSector * 2048, SeekOrigin.Begin);

                    file.Stream.Seek (0, SeekOrigin.Begin);
                    file.Stream.CopyTo (m_output);

                    // Pad to sector boundary
                    long currentPos = m_output.Position;
                    long nextSector = ((currentPos + 2047) / 2048) * 2048;
                    WriteZeros ((int)(nextSector - currentPos));
                }
            }

            private int WriteDirectoryRecord (byte[] buffer, int offset, IsoDirectoryInfo dir,
                                           bool isSelfOrParent, bool useJoliet)
            {
                int start = offset;
                int lengthPos = offset++;

                buffer[offset++] = 0; // Extended attribute length

                WriteBothEndian32 (buffer, offset, dir.StartSector);
                offset += 8;

                WriteBothEndian32 (buffer, offset, dir.Size);
                offset += 8;

                WriteDirectoryDate (buffer, offset, DateTime.Now);
                offset += 7;

                buffer[offset++] = 0x02; // Directory flag
                buffer[offset++] = 0; // File unit size
                buffer[offset++] = 0; // Interleave gap

                WriteBothEndian16 (buffer, offset, 1);
                offset += 4;

                // Write name
                if (isSelfOrParent)
                {
                    buffer[offset++] = 1;
                    buffer[offset++] = (byte)(dir.Parent == null ? 0 : 1);
                }
                else
                {
                    var nameBytes = useJoliet ?
                        Encoding.BigEndianUnicode.GetBytes (dir.Name) :
                        Encoding.ASCII.GetBytes (dir.Name.ToUpperInvariant());

                    buffer[offset++] = (byte)nameBytes.Length;
                    Array.Copy (nameBytes, 0, buffer, offset, nameBytes.Length);
                    offset += nameBytes.Length;
                }

                // Padding
                if ((offset - start) % 2 != 0)
                    offset++;

                buffer[lengthPos] = (byte)(offset - start);
                return offset - start;
            }

            private int WriteFileRecord (byte[] buffer, int offset, IsoFileInfo file)
            {
                int start = offset;
                int lengthPos = offset++;

                buffer[offset++] = 0; // Extended attribute length

                WriteBothEndian32 (buffer, offset, file.StartSector);
                offset += 8;

                WriteBothEndian32 (buffer, offset, file.Size);
                offset += 8;

                WriteDirectoryDate (buffer, offset, DateTime.Now);
                offset += 7;

                buffer[offset++] = 0; // File flag
                buffer[offset++] = 0; // File unit size
                buffer[offset++] = 0; // Interleave gap

                WriteBothEndian16 (buffer, offset, 1);
                offset += 4;

                // Convert filename for ISO 9660
                string isoName = ConvertToIso9660Name (file.Name);
                var nameBytes = Encoding.ASCII.GetBytes (isoName);

                buffer[offset++] = (byte)nameBytes.Length;
                Array.Copy (nameBytes, 0, buffer, offset, nameBytes.Length);
                offset += nameBytes.Length;

                // Padding
                if ((offset - start) % 2 != 0)
                    offset++;

                buffer[lengthPos] = (byte)(offset - start);
                return offset - start;
            }

            private string ConvertToIso9660Name (string name)
            {
                name = name.ToUpperInvariant();

                int dotIndex = name.LastIndexOf('.');
                string baseName, extension;

                if (dotIndex >= 0)
                {
                    baseName = name.Substring (0, dotIndex);
                    extension = name.Substring (dotIndex + 1);
                }
                else
                {
                    baseName = name;
                    extension = "";
                }

                // Limit lengths
                if (baseName.Length > 8) baseName = baseName.Substring (0, 8);
                if (extension.Length > 3) extension = extension.Substring (0, 3);

                return string.IsNullOrEmpty (extension) ?
                    baseName + ";1" :
                    baseName + "." + extension + ";1";
            }

            private void WriteBothEndian16 (byte[] buffer, int offset, ushort value)
            {
                buffer[offset    ] = (byte)(value & 0xFF);
                buffer[offset + 1] = (byte)(value >> 8);
                buffer[offset + 2] = (byte)(value >> 8);
                buffer[offset + 3] = (byte)(value & 0xFF);
            }

            private void WriteBothEndian32 (byte[] buffer, int offset, uint value)
            {
                buffer[offset    ] = (byte)(value & 0xFF);
                buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
                buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
                buffer[offset + 3] = (byte)(value >> 24);
                buffer[offset + 4] = (byte)(value >> 24);
                buffer[offset + 5] = (byte)((value >> 16) & 0xFF);
                buffer[offset + 6] = (byte)((value >> 8) & 0xFF);
                buffer[offset + 7] = (byte)(value & 0xFF);
            }

            private void WriteVolumeDate (byte[] buffer, int offset, DateTime date)
            {
                var dateStr = date.ToString ("yyyyMMddHHmmss") + "00";
                Encoding.ASCII.GetBytes (dateStr).CopyTo (buffer, offset);
                buffer[offset + 16] = 0; // GMT offset
            }

            private void WriteDirectoryDate (byte[] buffer, int offset, DateTime date)
            {
                buffer[offset    ] = (byte)(date.Year - 1900);
                buffer[offset + 1] = (byte)date.Month;
                buffer[offset + 2] = (byte)date.Day;
                buffer[offset + 3] = (byte)date.Hour;
                buffer[offset + 4] = (byte)date.Minute;
                buffer[offset + 5] = (byte)date.Second;
                buffer[offset + 6] = 0; // GMT offset
            }

            private byte[] PadAsciiString (string text, int length)
            {
                if (text.Length > length)
                    text = text.Substring (0, length);
                else
                    text = text.PadRight (length);
                return Encoding.ASCII.GetBytes (text);
            }

            private void WriteZeros (int count)
            {
                var zeros = new byte[Math.Min (count, 4096)];
                while (count > 0)
                {
                    int toWrite = Math.Min (count, zeros.Length);
                    m_output.Write (zeros, 0, toWrite);
                    count -= toWrite;
                }
            }
        }
    }
}