using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Deployment.Compression;
using Microsoft.Deployment.Compression.Cab;

using GameRes.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace GameRes.Formats.MSFormats
{
    internal sealed class CabEntry : Entry
    {
        public              CFFile FileInfo { get; set; }
        public              CFFolder Folder { get; set; }
        public              int VolumeIndex { get; set; }
        public        int FolderStartVolume { get; set; }
        public CFFolder FolderStartInstance { get; set; }

        public CabEntry (CFFile fileInfo, CFFolder folder, int volumeIndex)
        {
            FileInfo            = fileInfo ?? throw new ArgumentNullException (nameof(fileInfo));
            Folder              = folder   ?? throw new ArgumentNullException (nameof(folder));
            VolumeIndex         = volumeIndex;
            FolderStartVolume   = volumeIndex;
            FolderStartInstance = folder;
            Name                = fileInfo.FileName;
            Type                = FormatCatalog.Instance.GetTypeFromName (fileInfo.FileName);
            Size                = fileInfo.uiFileSize;
            Offset              = fileInfo.uiFolderOffset;
        }
    }

    public class CabOptions : ResourceOptions
    {
        public CompressionType CompressionType { get; set; }
        public            int CompressionLevel { get; set; }

        public CabOptions()
        {
            CompressionType = CompressionType.LZX;
            CompressionLevel = 21;
        }
    }

    public enum CompressionType
    {
        None = 0,
        MSZIP = 1,
        LZX = 3
    }


    [Export(typeof(ArchiveFormat))]
    [ExportMetadata("Priority", 1)]
    public sealed class CabOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CAB"; } }
        public override string Description { get { return "Microsoft cabinet archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  true; } }

        public static readonly byte[] CAB_HEADER = { (byte)'M', (byte)'S', (byte)'C', (byte)'F' };

        public CabOpener()
        {
            Extensions = new[] { "cab", "exe" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            long base_offset = 0;
            if (file.View.ReadUInt32 (0) != 0x4643534D)
            {
                base_offset = ExeFile.FindSignature (file, CAB_HEADER);
                if (base_offset < 0)
                    return null;
            }

            try
            {
                var volumes = new List<CabVolume> ();
                var processedFiles = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
                var cabHeader = new CFHeader();
                using (var stream = file.CreateStream())
                {
                    stream.Position = base_offset;
                    if (cabHeader.FromStream (stream) != 0)
                        return null;

                    cabHeader.Load (stream, base_offset);
                }

                volumes.Add (new CabVolume { File = file, Header = cabHeader, BaseOffset = base_offset });
                processedFiles.Add (file.Name);

                string currentDir = VFS.GetDirectoryName (file.Name);

                if (cabHeader.HasPrevCab && !string.IsNullOrEmpty (cabHeader.PrevCabinet))
                {
                    return null; // FIXME: skip multipart cabs for now
                    //LoadPreviousCabinets (currentDir, cabHeader, volumes, processedFiles);
                }

                if (cabHeader.HasNextCab && !string.IsNullOrEmpty (cabHeader.NextCabinet))
                {
                    return null;
                    //LoadNextCabinets (currentDir, cabHeader, volumes, processedFiles);
                }

                volumes.Sort ((a, b) => a.Header.usCabIndex.CompareTo (b.Header.usCabIndex));

                var dir = new List<Entry>();
                var addedFiles = new HashSet<string> (StringComparer.OrdinalIgnoreCase);

                for (int v = 0; v < volumes.Count; v++)
                {
                    var volume = volumes[v];
                    int folderIndex = 0;

                    foreach (var folder in volume.Header.cfFolders)
                    {

                        bool isContinuation = (folderIndex == 0 && v > 0 && IsContinuedFolder (folder));

                        // Skip continuation folders - files were already added from the original folder
                        if (isContinuation)
                        {
                            folderIndex++;
                            continue;
                        }

                        foreach (var fileInfo in folder.GetFiles ())
                        {
                            if (fileInfo.ContinuesFromPrev)
                                continue;

                            if (addedFiles.Contains (fileInfo.FileName))
                                continue;

                            dir.Add (new CabEntry (fileInfo, folder, v));
                            addedFiles.Add (fileInfo.FileName);
                        }
                        folderIndex++;

                    }
                }

                if (!dir.Any ())
                    return null;


                return new CabArcFile (file, this, dir, cabHeader, base_offset, volumes);
            }
            catch
            {
                return null;
            }
        }

        private void LoadPreviousCabinets (string directory, CFHeader currentHeader,
            List<CabVolume> volumes, HashSet<string> processedFiles)
        {
            string prevName = currentHeader.PrevCabinet;
            while (!string.IsNullOrEmpty (prevName))
            {
                if (processedFiles.Contains (prevName))
                    break;

                string fullPath = VFS.CombinePath (directory, prevName);
                if (!VFS.FileExists (fullPath))
                    break;

                processedFiles.Add (prevName);

                try
                {
                    var prevFile = VFS.OpenView (fullPath);
                    var prevHeader = new CFHeader ();
                    using (var stream = prevFile.CreateStream ())
                    {
                        if (prevHeader.FromStream (stream) != 0)
                            break;
                        prevHeader.Load (stream, 0);
                    }

                    if (prevHeader.usSetID != currentHeader.usSetID)
                        break;

                    volumes.Insert (0, new CabVolume { File = prevFile, Header = prevHeader, BaseOffset = 0 });
                    prevName = prevHeader.PrevCabinet;
                }
                catch
                {
                    break;
                }
            }
        }

        private void LoadNextCabinets (string directory, CFHeader currentHeader,
            List<CabVolume> volumes, HashSet<string> processedFiles)
        {
            string nextName = currentHeader.NextCabinet;
            while (!string.IsNullOrEmpty (nextName))
            {
                if (processedFiles.Contains (nextName))
                    break;

                string fullPath = VFS.CombinePath (directory, nextName);
                if (!VFS.FileExists (fullPath))
                    break;

                processedFiles.Add (nextName);

                try
                {
                    var nextFile = VFS.OpenView (fullPath);
                    var nextHeader = new CFHeader ();
                    using (var stream = nextFile.CreateStream ())
                    {
                        if (nextHeader.FromStream (stream) != 0)
                            break;
                        nextHeader.Load (stream, 0);
                    }

                    if (nextHeader.usSetID != currentHeader.usSetID)
                        break;

                    volumes.Add (new CabVolume { File = nextFile, Header = nextHeader, BaseOffset = 0 });
                    nextName = nextHeader.NextCabinet;
                }
                catch
                {
                    break;
                }
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var cabEntry = entry as CabEntry
                ?? throw new ArgumentException ("Entry must be CabEntry");
            var cabArc = arc as CabArcFile
                ?? throw new InvalidOperationException ("Archive must be CabArcFile");

            var baseStream = cabArc.File.CreateStream();
            return cabEntry.Folder.OpenFile (baseStream, cabEntry.FileInfo);
        }

        /* TODO: We don't have a working compressor implemented in pure C#
        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options, EntryCallback callback)
        {
            if (output == null)
                throw new ArgumentNullException (nameof (output));
            if (list == null)
                throw new ArgumentNullException (nameof (list));

            var cabOptions = options as CabOptions ?? new CabOptions();
            var entries = list.ToList();

            if (!entries.Any())
                throw new ArgumentException ("Files list cannot be empty", nameof (list));

            var writer = new CabWriter (output, cabOptions);

            int i = 0;
            foreach (var entry in entries)
            {
                callback?.Invoke (i * 100 / entries.Count, entry, Localization.Format ("PackingFileN", i);

                using (var input = VFS.OpenStream (entry))
                {
                    writer.AddFile (entry.Name, input);
                }
                i++;
            }

            writer.WriteCab();
            callback?.Invoke (100, null, Localization._T ("PackingComplete"));
        }*/

        // Use limited native compressor for now
        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options, EntryCallback callback)
        {
            if (output == null)
                throw new ArgumentNullException (nameof (output));

            if (list == null)
                throw new ArgumentNullException (nameof (list));

            var cabOptions = options as CabOptions ?? new CabOptions();
            var entries = list.ToList();

            if (!entries.Any())
                throw new ArgumentException ("Files list cannot be empty", nameof (list));

            using (var tempFileManager = new TempFileManager())
            {
                var tempCabFile = tempFileManager.CreateTempFile();
                var cabInfo     = new CabInfo (tempCabFile);
                var filesToPack = new List<string>();
                var packEntries = new List<string>();
                var entryLookup = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                string currentDirectory = Directory.GetCurrentDirectory(); // NOTE: it's set by the GUI

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    int progressPercent = (int)((i + 1) * 10.0 / entries.Count);
                    callback?.Invoke (progressPercent, entry, Localization._T ("PreparingFiles"));

                    string sourceFile;
                    string fullPath = Path.GetFullPath (entry.Name);

                    if (!string.IsNullOrEmpty (entry.Name) && File.Exists (fullPath))
                        sourceFile = fullPath;
                    else
                    {
                        sourceFile = tempFileManager.CreateTempFile(); // NOTE: native compressor doesn't know Streams

                        using (var inputStream = VFS.OpenStream (entry))
                        using (var outputStream = File.Create (sourceFile))
                        {
                            inputStream.CopyTo (outputStream);
                        }
                    }

                    filesToPack.Add (sourceFile);
                    var relativePath = fullPath.Replace (currentDirectory, "");
                    if (relativePath.StartsWith(@"\"))
                        relativePath = relativePath.Substring (1);
                    packEntries.Add (relativePath);
                    entryLookup[relativePath] = entry;
                }

                callback?.Invoke (10, null, Localization._T ("PackingFiles"));

                Entry currentEntry = null;
                var progressHandler = new EventHandler<ArchiveProgressEventArgs>((sender, e) =>
                {
                    double packingProgress = 0;

                    if (e.TotalFileBytes > 0)
                    {
                        packingProgress = (double)e.FileBytesProcessed / e.TotalFileBytes;
                    }
                    else if (e.TotalFiles > 0 && e.CurrentFileNumber > 0)
                    {
                        double fileProgress = 0;
                        if (e.CurrentFileTotalBytes > 0)
                            fileProgress = (double)e.CurrentFileBytesProcessed / e.CurrentFileTotalBytes;
                        packingProgress = (double)(e.CurrentFileNumber - 1) / e.TotalFiles;
                    }

                    int overallProgress = 10 + (int)(packingProgress * 80);

                    overallProgress = Math.Max (10, Math.Min (90, overallProgress));

                    string statusMessage = Localization.Format ("PackingFileN", "");
                    if (!string.IsNullOrEmpty (e.CurrentFileName))
                    {
                        statusMessage = Localization.Format ("PackingFileN", Path.GetFileName (e.CurrentFileName));

                        if (e.CurrentFileTotalBytes > 0)
                        {
                            long percentComplete = (e.CurrentFileBytesProcessed * 100) / e.CurrentFileTotalBytes;
                            statusMessage += $" ({percentComplete}%)";
                        }

                        if (e.TotalFiles > 0)
                            statusMessage += Localization.Format ("FileIofN", e.CurrentFileNumber, e.TotalFiles);
                    }

                    callback?.Invoke (overallProgress, currentEntry, statusMessage);
                });

                try
                {
                    cabInfo.PackFiles (null, filesToPack, packEntries,
                        Microsoft.Deployment.Compression.CompressionLevel.Max, 
                        progressHandler);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException (Localization.Format ("PackingTagFailed", Tag), ex);
                }

                callback?.Invoke (90, null, Localization._T ("FinalizingArchive"));

                using (var cabStream = File.OpenRead (tempCabFile))
                {
                    long totalBytes = cabStream.Length;
                    long bytesCopied = 0;
                    byte[] buffer = new byte[81920]; // 80KB buffer
                    int bytesRead;

                    while ((bytesRead = cabStream.Read (buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write (buffer, 0, bytesRead);
                        bytesCopied += bytesRead;

                        if (totalBytes > 0)
                        {
                            double copyProgress = (double)bytesCopied / totalBytes;
                            int overallProgress = 90 + (int)(copyProgress * 10);

                            string sizeInfo = $"{Localization.FormatFileSize(bytesCopied)} / {Localization.FormatFileSize(totalBytes)}";
                            callback?.Invoke (overallProgress, null, Localization.Format ("WritingArchive", sizeInfo));
                        }
                    }
                }

                callback?.Invoke (100, null, Localization._T ("PackingComplete"));
            }
        }

        private bool IsContinuedFolder (CFFolder folder)
        {
            var files = folder.GetFiles ();
            return files.Count > 0 && files[0].ContinuesFromPrev;
        }

        public override ResourceOptions GetDefaultOptions()
        {
            return new CabOptions
            {
                CompressionType = CompressionType.LZX,
                CompressionLevel = 21
            };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            return GetDefaultOptions();
        }
    }

    internal sealed class TempFileManager : IDisposable
    {
        private readonly List<string> _tempFiles = new List<string>();
        private bool _disposed;

        public string CreateTempFile()
        {
            var tempFile = Path.GetTempFileName();
            _tempFiles.Add (tempFile);
            return tempFile;
        }

        public void AddFile (string filePath)
        {
            if (!string.IsNullOrEmpty (filePath))
                _tempFiles.Add (filePath);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            foreach (var file in _tempFiles)
            {
                try
                {
                    if (File.Exists (file))
                        File.Delete (file);
                }
                catch { }
            }

            _tempFiles.Clear();
            _disposed = true;
        }
    }

    internal class CabWriter
    {
        private         Stream m_output;
        private     CabOptions m_options;
        private List<FileData> m_files = new List<FileData>();

        private class FileData
        {
            public string Name { get; set; }
            public byte[] Data { get; set; }
        }

        public CabWriter (Stream output, CabOptions options)
        {
            m_output   = output;
            m_options = options;
        }

        public void AddFile (string name, Stream data)
        {
            var ms = new MemoryStream();
            data.CopyTo (ms);
            m_files.Add (new FileData { Name = name, Data = ms.ToArray() });
        }

        public void WriteCab()
        {
            var writer = new BinaryWriter (m_output, Encoding.ASCII, true);

            int headerSize = 36;
            int folderSize = 8;
            int filesSize = m_files.Sum (f => 16 + Encoding.ASCII.GetByteCount (f.Name) + 1);
            int dataOffset = headerSize + folderSize + filesSize;

            writer.Write ((byte)'M');
            writer.Write ((byte)'S');
            writer.Write ((byte)'C');
            writer.Write ((byte)'F');
            writer.Write ((uint)0);
            writer.Write ((uint)0);
            writer.Write ((uint)0);
            writer.Write ((uint)(headerSize + folderSize));
            writer.Write ((uint)0);
            writer.Write ((byte)3);
            writer.Write ((byte)1);
            writer.Write ((ushort)1);
            writer.Write ((ushort)m_files.Count);
            writer.Write ((ushort)0);
            writer.Write ((ushort)1);
            writer.Write ((ushort)0);

            writer.Write ((uint)dataOffset);
            writer.Write ((ushort)0);
            writer.Write (GetCompressionType());

            uint folderOffset = 0;
            foreach (var file in m_files)
            {
                writer.Write ((uint)file.Data.Length);
                writer.Write (folderOffset);
                writer.Write ((ushort)0);
                writer.Write ((ushort)0);
                writer.Write ((ushort)0);
                writer.Write ((ushort)0x20);

                var nameBytes = Encoding.ASCII.GetBytes (file.Name);
                writer.Write (nameBytes);
                writer.Write ((byte)0);

                folderOffset += (uint)file.Data.Length;
            }

            var compressor = CreateCompressor();
            var allData = m_files.SelectMany (f => f.Data).ToArray();

            int numBlocks = 0;
            int offset = 0;
            var dataStart = m_output.Position;

            while (offset < allData.Length)
            {
                int blockSize = Math.Min (32768, allData.Length - offset);
                byte[] uncompressed = new byte[blockSize];
                Array.Copy (allData, offset, uncompressed, 0, blockSize);

                byte[] compressed = compressor.Compress (uncompressed, offset + blockSize >= allData.Length);

                writer.Write ((uint)0);
                writer.Write ((ushort)compressed.Length);
                writer.Write ((ushort)blockSize);
                writer.Write (compressed);

                offset += blockSize;
                numBlocks++;
            }

            var dataEnd = m_output.Position;

            m_output.Position = 8;
            writer.Write ((uint)dataEnd);

            m_output.Position = headerSize + 4;
            writer.Write ((ushort)numBlocks);

            m_output.Position = dataEnd;
        }

        private ushort GetCompressionType()
        {
            switch (m_options.CompressionType)
            {
                case CompressionType.MSZIP: return 1;
                case CompressionType.LZX: return (ushort)(3 | (m_options.CompressionLevel << 8));
                default: return 0;
            }
        }

        private Compressor CreateCompressor()
        {
            switch (m_options.CompressionType)
            {
                case CompressionType.MSZIP: return new MSZipCompressor();
                case CompressionType.LZX: return new LzxCabCompressor (m_options.CompressionLevel);
                default: return new NoCompressor();
            }
        }
    }

    internal class CabVolume
    {
        public ArcView File { get; set; }
        public CFHeader Header { get; set; }
        public long BaseOffset { get; set; }
    }
    internal class CabArcFile : ArcFile
    {
        public CFHeader Header { get; }
        public long BaseOffset { get; }
        public List<CabVolume> Volumes { get; }

        public CabArcFile (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir,
            CFHeader header, long baseOffset, List<CabVolume> volumes = null)
            : base (arc, impl, dir)
        {
            Header = header;
            BaseOffset = baseOffset;
            Volumes = volumes ?? new List<CabVolume> { new CabVolume { File = arc, Header = header, BaseOffset = baseOffset } };
        }
    }

    internal class CFHeader
    {
        public uint uiCabSize;
        public   uint uiFilesOffset;
        public   byte cVerMinor = 3;
        public   byte cVerMajor = 1;
        public ushort usNumFolders;
        public ushort usNumFiles;
        public ushort usFlags;
        public ushort usSetID;
        public ushort usCabIndex;
        public ushort usCabinetReservedBytes;
        public   byte bFolderReservedBytes;
        public   byte bBlockReservedBytes;

        public List<CFFolder> cfFolders = new List<CFFolder>();

        public string PrevCabinet { get; set; }
        public    string PrevDisk { get; set; }
        public string NextCabinet { get; set; }
        public    string NextDisk { get; set; }

        public   bool HasPrevCab => (usFlags & 0x0001) != 0;
        public   bool HasNextCab => (usFlags & 0x0002) != 0;
        public   bool HasReserve => (usFlags & 0x0004) != 0;

        public int FromStream (Stream input)
        {
            byte[] tmp = new byte[36];
            if (input.Read (tmp, 0, 36) != 36)
                return 1;

            if ((tmp[30] & 4) == 4)
            {
                Array.Resize (ref tmp, 40);
                if (input.Read (tmp, 36, 4) != 4)
                    return 1;
            }
            return FromBytes (tmp);
        }

        public int FromBytes (byte[] input)
        {
            if (input[0] != 'M' || input[1] != 'S' || input[2] != 'C' || input[3] != 'F')
                return 1;
            uiCabSize     = BitConverter.ToUInt32 (input, 8 );
            uiFilesOffset = BitConverter.ToUInt32 (input, 16);
            cVerMinor     = input[24];
            cVerMajor     = input[25];
            if (cVerMinor != 3 || cVerMajor != 1)
                return 1;
            usNumFolders  = BitConverter.ToUInt16 (input, 26);
            usNumFiles    = BitConverter.ToUInt16 (input, 28);
            usFlags       = BitConverter.ToUInt16 (input, 30);
            usSetID       = BitConverter.ToUInt16 (input, 32);
            usCabIndex    = BitConverter.ToUInt16 (input, 34);

            if ((usFlags & 4) == 4)
            {
                usCabinetReservedBytes = BitConverter.ToUInt16 (input, 36);
                bFolderReservedBytes   = input[38];
                bBlockReservedBytes    = input[39];
            }
            return 0;
        }

        public void Load (Stream srcCab, long baseOffset)
        {
            cfFolders = new List<CFFolder>();
            srcCab.Position = baseOffset + Length;
            if (HasPrevCab)
            {
                PrevCabinet = ReadNullTerminatedString (srcCab);
                PrevDisk    = ReadNullTerminatedString (srcCab);
            }
            if (HasNextCab)
            {
                NextCabinet = ReadNullTerminatedString (srcCab);
                NextDisk    = ReadNullTerminatedString (srcCab);
            }
            for (int i = 0; i < usNumFolders; i++)
                cfFolders.Add (new CFFolder (srcCab, bFolderReservedBytes));

            srcCab.Position = baseOffset + uiFilesOffset;
            for (uint i = 0, lastFolder = 0, lastOffset = 0; i < usNumFiles; i++)
            {
                CFFile cff = new CFFile (srcCab);
                if (cff.uiFolderOffset < lastOffset)
                    lastFolder++;
                lastOffset = cff.uiFolderOffset;
                if (lastFolder < cfFolders.Count)
                cfFolders[(int)lastFolder].AddFile (cff);
            }
        }

        private string ReadNullTerminatedString (Stream stream)
        {
            var bytes = new List<byte> ();
            int b;
            while ((b = stream.ReadByte()) != -1 && b != 0)
            {
                bytes.Add ((byte)b);
                if (bytes.Count > 255) break;
            }
            return Encoding.ASCII.GetString (bytes.ToArray());
        }
        public int Length
        {
            get
            {
                int z = 0;
                if ((usFlags & 4) == 4)
                    z = 4;
                return 36 + z + usCabinetReservedBytes;
            }
        }
    }

    internal class CFFolder
    {
        public   uint uiDataOffset;
        public ushort usBlocks;
        public ushort usCompression;

        private List<CFFile> cfFiles = new List<CFFile>();

        private byte[] decompressedData; // Cache for small folders only
        private readonly object decompressionLock = new object ();

        // Threshold: folders larger than this won't be cached
        private const long CACHE_THRESHOLD = 10 * 1024 * 1024; // 10MB

        public CFFolder (Stream source, byte bReserved = 0)
        {
            FromStream (source);
        }

        public int FromStream (Stream input)
        {
            byte[] tmp = new byte[8];
            input.Read (tmp, 0, 8);
            return FromBytes (tmp);
        }

        public int FromBytes (byte[] input)
        {
            uiDataOffset  = BitConverter.ToUInt32 (input, 0);
            usBlocks      = BitConverter.ToUInt16 (input, 4);
            usCompression = BitConverter.ToUInt16 (input, 6);
            return 0;
        }

        public void AddFile (CFFile cff) => cfFiles.Add (cff);

        public List<CFFile> GetFiles (string wildcard = null) => wildcard == null ? cfFiles : cfFiles.FindAll (c => c.FileName.Contains (wildcard));

        /// <summary>
        /// Calculate total uncompressed size of this folder
        /// </summary>
        private long GetTotalUncompressedSize()
        {
            if (cfFiles.Count == 0) return 0;

            // Find the last file in the folder
            CFFile lastFile = null;
            uint maxEndOffset = 0;

            foreach (var file in cfFiles)
            {
                uint endOffset = file.uiFolderOffset + file.uiFileSize;
                if (endOffset > maxEndOffset)
                {
                    maxEndOffset = endOffset;
                    lastFile = file;
                }
            }

            return maxEndOffset;
        }

        public Stream OpenFile (Stream input, CFFile file)
        {
            //Debug.WriteLine ($"[CFFolder.OpenFile] Called for {file.FileName} during {new System.Diagnostics.StackTrace()}");
            long totalSize = GetTotalUncompressedSize();

            if (totalSize <= CACHE_THRESHOLD)
            {
                //Debug.WriteLine ($"[CFFolder] Using cached decompression (folder size {totalSize} <= {CACHE_THRESHOLD})");
                EnsureDecompressed (input);
                return new MemoryStream (decompressedData, (int)file.uiFolderOffset, (int)file.uiFileSize, false);
            }
            else
            {
                //Debug.WriteLine ($"[CFFolder] Using streaming extraction (folder size {totalSize} > {CACHE_THRESHOLD})");
                return StreamExtractFile (input, file);
            }
        }

        /// <summary>
        /// Stream through decompression, keeping only the bytes we need for this specific file
        /// </summary>
        private Stream StreamExtractFile (Stream input, CFFile targetFile)
        {
            lock (decompressionLock)
            {
                input.Position = uiDataOffset;

                // Validate we're not reading past end of stream
                if (input.Position >= input.Length)
                {
                    //Debug.WriteLine ($"[StreamExtract] ERROR: Data offset {uiDataOffset} is past end of stream");
                    return null;
                }

                // Result buffer - only as large as the file we want
                var result = new MemoryStream ((int)targetFile.uiFileSize);

                long currentOffset = 0; // Track position in decompressed stream
                long fileStart = targetFile.uiFolderOffset;
                long fileEnd = targetFile.uiFolderOffset + targetFile.uiFileSize;

                //Debug.WriteLine ($"[StreamExtract] Target file range: {fileStart}-{fileEnd}");

                if ((usCompression & 0xFF) == 3) // LZX
                {
                    int windowBits = usCompression >> 8;
                    var settings = new LzxSettings { WindowBits = windowBits, IntelE8 = true };
                    using (var lzx = new LzxDecompressor (new MemoryStream(), settings))
                    {
                        for (int i = 0; i < usBlocks; i++)
                        {
                            var cfd = new CFData (input);
                            lzx.SetCompressedData (cfd.Payload);

                            var blockData = new byte[cfd.UncompressedSize];
                            int got = lzx.ReadDecompressed (blockData, 0, blockData.Length);

                            ProcessBlock (blockData, got, ref currentOffset, fileStart, fileEnd, result);

                            // Stop if we've got everything we need
                            if (currentOffset >= fileEnd)
                            {
                                //Debug.WriteLine ($"[StreamExtract] Got all data at block {i}, stopping");
                                break;
                            }
                        }
                    }
                }
                else if ((usCompression & 0xFF) == 1) // MSZIP
                {
                    var compressor = new MSZipCompressor();

                    for (int i = 0; i < usBlocks; i++)
                    {
                        var cfd = new CFData (input);
                        var blockData = compressor.Decompress(
                            cfd.Payload, cfd.CompressedSize, cfd.UncompressedSize, i == usBlocks - 1);

                        ProcessBlock (blockData, blockData.Length, ref currentOffset, fileStart, fileEnd, result);

                        // Stop if we've got everything we need
                        if (currentOffset >= fileEnd)
                        {
                            //Debug.WriteLine ($"[StreamExtract] Got all data at block {i}, stopping");
                            break;
                        }
                    }

                    compressor.Reset();
                }
                else if (usCompression == 0) // No compression
                {
                    for (int i = 0; i < usBlocks; i++)
                    {
                        var cfd = new CFData (input);

                        ProcessBlock (cfd.Payload, cfd.Payload.Length, ref currentOffset, fileStart, fileEnd, result);

                        if (currentOffset >= fileEnd)
                        {
                            //Debug.WriteLine ($"[StreamExtract] Got all data at block {i}, stopping");
                            break;
                        }
                    }
                }
                else
                    throw new NotSupportedException ($"Compression type {usCompression:X4} not supported");

                //if (result.Length != targetFile.uiFileSize)
                    //Debug.WriteLine ($"[StreamExtract] WARNING: Expected {targetFile.uiFileSize} bytes, got {result.Length}");

                result.Position = 0;
                return result;
            }
        }

        /// <summary>
        /// Process a decompressed block, extracting only the bytes that overlap with our target file
        /// </summary>
        private void ProcessBlock (byte[] blockData, int blockLength, ref long currentOffset,
                                  long fileStart, long fileEnd, MemoryStream result)
        {
            long blockEnd = currentOffset + blockLength;

            // Block is entirely before our file - skip it
            if (blockEnd <= fileStart)
            {
                //Debug.WriteLine ($"[ProcessBlock] Skipping block at offset {currentOffset}-{blockEnd} (before file)");
                currentOffset = blockEnd;
                return;
            }

            // Block is entirely after our file - we're done
            if (currentOffset >= fileEnd)
            {
                //Debug.WriteLine ($"[ProcessBlock] Block at offset {currentOffset} is past file end, ignoring");
                return;
            }

            // Block overlaps with our file - extract the relevant portion
            long copyStart = Math.Max (0, fileStart - currentOffset);
            long copyEnd   = Math.Min (blockLength, fileEnd - currentOffset);
            int copyLength = (int)(copyEnd - copyStart);

            //Debug.WriteLine ($"[ProcessBlock] Extracting {copyLength} bytes from block at offset {currentOffset}");
            result.Write (blockData, (int)copyStart, copyLength);

            currentOffset = blockEnd;
        }

        /// <summary>
        /// Original caching decompression for small folders
        /// </summary>
        private void EnsureDecompressed (Stream input)
        {
            lock (decompressionLock)
            {
                if (decompressedData != null)
                    return;

                using (var ms = new MemoryStream())
                {
                    input.Position = uiDataOffset;

                    if ((usCompression & 0xFF) == 3) // LZX
                    {
                        int windowBits = usCompression >> 8;
                        var settings = new LzxSettings { WindowBits = windowBits, IntelE8 = true };
                        using (var lzx = new LzxDecompressor (new MemoryStream(), settings))
                        {
                            for (int i = 0; i < usBlocks; i++)
                            {
                                var cfd = new CFData (input);
                                lzx.SetCompressedData (cfd.Payload);

                                var buf = new byte[cfd.UncompressedSize];
                                int got = lzx.ReadDecompressed (buf, 0, buf.Length);

                                if (got > 0)
                                    ms.Write (buf, 0, got);
                            }
                        }
                    }
                    else if ((usCompression & 0xFF) == 1) // MSZIP
                    {
                        var compressor = new MSZipCompressor();

                        for (int i = 0; i < usBlocks; i++)
                        {
                            var cfd = new CFData (input);
                            var decompressed = compressor.Decompress(
                                cfd.Payload, cfd.CompressedSize, cfd.UncompressedSize, i == usBlocks - 1);

                            ms.Write (decompressed, 0, decompressed.Length);
                        }

                        compressor.Reset();
                    }
                    else if (usCompression == 0) // No compression
                    {
                        for (int i = 0; i < usBlocks; i++)
                        {
                            var cfd = new CFData (input);
                            ms.Write (cfd.Payload, 0, cfd.Payload.Length);
                        }
                    }
                    else
                    {
                        throw new NotSupportedException ($"Compression type {usCompression:X4} not supported");
                    }

                    decompressedData = ms.ToArray();
                    Debug.WriteLine ($"[CFFolder] Cached {decompressedData.Length} bytes for folder");
                }
            }
        }
    }

    internal class CFFile
    {
        public   uint uiFileSize;
        public   uint uiFolderOffset;
        public ushort usFolderIndex;
        public ushort usAttribs;
        public string FileName = "";

        public CFFile (Stream src)
        {
            FromStream (src);
        }

        public int FromStream (Stream source)
        {
            byte[] tmp = new byte[17];
            source.Read (tmp, 0, 16);

            uiFileSize     = BitConverter.ToUInt32 (tmp, 0 );
            uiFolderOffset = BitConverter.ToUInt32 (tmp, 4 );
            usFolderIndex  = BitConverter.ToUInt16 (tmp, 8 );
            usAttribs      = BitConverter.ToUInt16 (tmp, 14);

            int i = 0;
            while (i++ < 256)
            {
                int b = source.ReadByte ();
                if (b <= 0) break;
                FileName += (char)b;
            }

            return 0;
        }

        public   bool ContinuesInNext => (usFolderIndex == 0xFFFE);
        public bool ContinuesFromPrev => (usFolderIndex == 0xFFFD);
        public     bool ContinuesBoth => (usFolderIndex == 0xFFFF);

        public  int GetFolderIndex () => (usFolderIndex &  0xFFF );
    }

    internal class CFData
    {
        public   uint Checksum;
        public ushort CompressedSize;
        public ushort UncompressedSize;
        public byte[] Payload;

        public CFData (Stream src, byte bReserved = 0, bool bLoadData = true)
        {
            FromStream (src, bLoadData);
        }

        public int FromStream (Stream input, bool bLoadData = true)
        {
            byte[] header = new byte[8];
            if (input.Read (header, 0, 8) != 8)
                throw new EndOfStreamException();

            Checksum = BitConverter.ToUInt32 (header, 0);
            CompressedSize = BitConverter.ToUInt16 (header, 4);
            UncompressedSize = BitConverter.ToUInt16 (header, 6);

            //Debug.WriteLine ($"[CFData.FromStream] Checksum={Checksum:X8}, Compressed={CompressedSize}, Uncompressed={UncompressedSize}");
            if (bLoadData)
            {
                Payload = new byte[CompressedSize];
                int totalRead = 0;
                while (totalRead < CompressedSize)
                {
                    int read = input.Read (Payload, totalRead, CompressedSize - totalRead);
                    if (read <= 0)
                        throw new EndOfStreamException ($"Expected {CompressedSize} bytes, got {totalRead}");

                    totalRead += read;
                }
            }
            return 8 + (bLoadData ? CompressedSize : 0);
        }
    }

    internal abstract class Compressor
    {
        public abstract byte[] Decompress (byte[] compressed, int compressedSize, int expectedUncompressed, bool isLast);
        public abstract byte[] Compress (byte[] data, bool isLast);
        public abstract   void Reset();

        public bool bInited = false;
    }

    internal class NoCompressor : Compressor
    {
        public override void Reset() { }

        public override byte[] Decompress (byte[] data, int compressedSize, int expectedUncompressed, bool isLast) => data;

        public override byte[] Compress (byte[] data, bool isLast) => data;
    }

    internal class MSZipCompressor : Compressor
    {
        public override void Reset()
        {
            // Nothing carried across blocks for MSZIP (each is independent Deflate stream)
        }

        public override byte[] Decompress (byte[] data, int compressedSize, int expectedUncompressed, bool isLast)
        {
            if (data.Length < 2 || data[0] != (byte)'C' || data[1] != (byte)'K')
            {
                Debug.WriteLine ("MSZIP: Missing CK signature at block start.");
                return new byte[expectedUncompressed];
            }

            // Fresh inflater per block (MSZIP resets dictionary each block)
            var inflater = new Inflater (noHeader: true);

            // Feed block data *after* CK signature
            inflater.SetInput (data, 2, compressedSize - 2);

            var output = new byte[expectedUncompressed];
            int total = 0;

            while (!inflater.IsFinished && !inflater.IsNeedingInput)
            {
                int got = inflater.Inflate (output, total, expectedUncompressed - total);
                if (got == 0)
                {
                    Debug.WriteLine ("MSZIP: Inflater returned 0, breaking");
                    break;
                }
                total += got;
                if (total >= expectedUncompressed) break;
            }

            if (total != expectedUncompressed)
                Debug.WriteLine ($"MSZIP: Decompress mismatch — expected {expectedUncompressed}, got {total}");
            //else
                //Debug.WriteLine ($"MSZIP: Decompressed {total} bytes OK");

            return output;
        }

        public override byte[] Compress (byte[] data, bool isLast)
        {
            Debug.WriteLine ("MSZIP compression not implemented.");
            return new byte[0];
        }
    }

    internal class LzxCabCompressor : Compressor
    {
        private int m_window_bits;
        private LzxDecompressor m_decompressor;

        public LzxCabCompressor (int windowBits)
        {
            m_window_bits = windowBits;
            if (m_window_bits < 15 || m_window_bits > 21)
                throw new ArgumentOutOfRangeException (nameof (windowBits), "Window bits 15..21 required");
        }

        public override void Reset()
        {
            bInited = false;
            m_decompressor?.Dispose();
            m_decompressor = null;
        }

        // In LzxCabCompressor.Decompress method, add more logging:
        public override byte[] Decompress (byte[] compressed, int compressedSize, int expectedUncompressed, bool isLast)
        {
            //Debug.WriteLine ($"[LzxCabCompressor.Decompress] Enter: compressedSize={compressedSize}, expectedUncompressed={expectedUncompressed}, isLast={isLast}");

            if (!bInited)
            {
                var settings = new LzxSettings
                {
                    WindowBits = m_window_bits,
                    IntelE8 = true
                };
                m_decompressor = new LzxDecompressor (new MemoryStream(), settings);
                bInited = true;
            }

            // Set the compressed data
            //Debug.WriteLine ($"[LzxCabCompressor.Decompress] Setting compressed data");
            m_decompressor.SetCompressedData (compressed);

            var buffer = new byte[expectedUncompressed];
            //Debug.WriteLine ($"[LzxCabCompressor.Decompress] Calling ReadDecompressed");
            int got = m_decompressor.ReadDecompressed (buffer, 0, expectedUncompressed);
            //Debug.WriteLine ($"[LzxCabCompressor.Decompress] ReadDecompressed returned {got} bytes");

            if (got < buffer.Length)
                Array.Resize (ref buffer, got);

            //Debug.WriteLine ($"[LzxCabCompressor.Decompress] Exit: returning {buffer.Length} bytes");
            return buffer;
        }

        public override byte[] Compress (byte[] data, bool isLast)
        {
            throw new NotSupportedException ("LZX compression not implemented");
        }
    }
}