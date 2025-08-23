using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using Microsoft.Deployment.Compression;
using Microsoft.Deployment.Compression.Cab;

namespace GameRes.Formats.Microsoft
{
    internal sealed class CabEntry : Entry
    {
        public CabFileInfo Info { get; }

        public CabEntry (CabFileInfo fileInfo)
        {
            Info = fileInfo ?? throw new ArgumentNullException (nameof (fileInfo));
            Name = Info.Name;
            Type = FormatCatalog.Instance.GetTypeFromName (Info.Name);
            Size = (uint)Math.Min (Info.Length, uint.MaxValue);
            Offset = 0; // CAB format doesn't provide direct offset information
        }
    }

    public class CabOptions : ResourceOptions
    {
        public CompressionLevel CompressionLevel { get; set; }

        public CabOptions() { }
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

    [Export (typeof (ArchiveFormat))]
    [ExportMetadata ("Priority", 1)]
    public sealed class CabOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CAB"; } }
        public override string Description { get { return "Microsoft cabinet archive"; } }
        public override uint     Signature { get { return  0x4643534D; } } // 'MSCF'
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  true; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file == null)
                throw new ArgumentNullException (nameof (file));

            if (VFS.IsVirtual)
                throw new NotSupportedException ("Cabinet files inside archives are not supported");

            try
            {
                var cabInfo = new CabInfo (file.Name);
                var dir = GetEntries (cabInfo);
                if (!dir.Any())
                    return null;

                return new ArcFile (file, this, dir);
            }
            catch (CabException)
            {
                return null;
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static List<Entry> GetEntries (CabInfo cabInfo)
        {
            var entries = new List<Entry>();

            foreach (var fileInfo in cabInfo.GetFiles())
            {
                try
                {
                    entries.Add (new CabEntry (fileInfo));
                }
                catch (Exception ex) when (ex is ArgumentException || ex is PathTooLongException)
                {
                    continue;
                }
            }

            return entries;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (arc == null)
                throw new ArgumentNullException (nameof(arc));
            if (entry == null)
                throw new ArgumentNullException (nameof(entry));

            var cabEntry = entry as CabEntry;
            if (cabEntry == null)
                throw new ArgumentException ("Entry must be a CabEntry", nameof (entry));

            try
            {
                return cabEntry.Info.OpenRead();
            }
            catch (CabException ex)
            {
                throw new InvalidOperationException ($"Failed to open CAB entry '{entry.Name}'", ex);
            }
        }

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
                var cabInfo = new CabInfo (tempCabFile);
                var filesToPack = new List<string>();
                var packEntries = new List<string>();
                var entryLookup = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

                string commonBasePath = FindCommonBasePath (entries.Select (e => e.Name));

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    int progressPercent = (int)((i + 1) * 10.0 / entries.Count);
                    callback?.Invoke (progressPercent, entry, "Preparing files...");

                    string sourceFile;
                    string fullPath = Path.GetFullPath (entry.Name);

                    if (!string.IsNullOrEmpty (entry.Name) && File.Exists (fullPath))
                        sourceFile = fullPath;
                    else
                    {
                        sourceFile = tempFileManager.CreateTempFile();

                        using (var inputStream = VFS.OpenStream (entry))
                        using (var outputStream = File.Create (sourceFile))
                        {
                            inputStream.CopyTo (outputStream);
                        }
                    }

                    filesToPack.Add (sourceFile);
                    string relativePath = GetRelativePath (commonBasePath, entry.Name);
                    packEntries.Add (relativePath);
                    entryLookup[relativePath] = entry;
                }

                callback?.Invoke (10, null, "Packing files...");

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

                    string statusMessage = "Packing";
                    if (!string.IsNullOrEmpty (e.CurrentFileName))
                    {
                        statusMessage = $"Packing: {Path.GetFileName (e.CurrentFileName)}";

                        if (e.CurrentFileTotalBytes > 0)
                        {
                            long percentComplete = (e.CurrentFileBytesProcessed * 100) / e.CurrentFileTotalBytes;
                            statusMessage += $" ({percentComplete}%)";
                        }

                        if (e.TotalFiles > 0)
                            statusMessage += $" - File {e.CurrentFileNumber}/{e.TotalFiles}";
                    }

                    callback?.Invoke (overallProgress, currentEntry, statusMessage);
                });

                try
                {
                    cabInfo.PackFiles (null, filesToPack, packEntries, cabOptions.CompressionLevel, progressHandler);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException ("Failed to pack files into CAB archive", ex);
                }

                callback?.Invoke (90, null, "Finalizing archive...");

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

                            string sizeInfo = $"{FormatFileSize (bytesCopied)} / {FormatFileSize (totalBytes)}";
                            callback?.Invoke (overallProgress, null, $"Writing archive... {sizeInfo}");
                        }
                    }
                }

                callback?.Invoke (100, null, "Complete");
            }
        }

        private static string FormatFileSize (long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private string FindCommonBasePath (IEnumerable<string> paths)
        {
            var pathList = paths.Where (p => !string.IsNullOrEmpty (p)).ToList();
            if (!pathList.Any())
                return string.Empty;

            if (pathList.Count == 1)
                return Path.GetDirectoryName (pathList[0]) ?? string.Empty;

            var firstPath = Path.GetDirectoryName (pathList[0]) ?? string.Empty;
            var commonPath = firstPath;

            foreach (var path in pathList.Skip (1))
            {
                var currentDir = Path.GetDirectoryName (path) ?? string.Empty;

                while (!string.IsNullOrEmpty (commonPath) &&
                       !currentDir.StartsWith (commonPath, StringComparison.OrdinalIgnoreCase))
                {
                    commonPath = Path.GetDirectoryName (commonPath) ?? string.Empty;
                }

                if (string.IsNullOrEmpty (commonPath))
                    break;
            }

            return commonPath;
        }

        private string GetRelativePath (string basePath, string fullPath)
        {
            if (string.IsNullOrEmpty (basePath) || string.IsNullOrEmpty (fullPath))
                return Path.GetFileName (fullPath) ?? string.Empty;

            try
            {
                if (fullPath.StartsWith (basePath, StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath = fullPath.Substring (basePath.Length).TrimStart('\\', '/');
                    return string.IsNullOrEmpty (relativePath) ? Path.GetFileName (fullPath) : relativePath;
                }
            }
            catch { }

            return Path.GetFileName (fullPath) ?? string.Empty;
        }

        public override ResourceOptions GetDefaultOptions()
        {
            return new CabOptions
            {
                CompressionLevel = CompressionLevel.Max
            };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            return GetDefaultOptions();
        }
    }
}