using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GameRes
{
    public interface IFileSystem : IDisposable
    {
        /// <summary>
        /// Returns entry corresponding to the given file or directory within filesystem.
        /// </summary>
        /// <exception cref="FileNotFoundException">File is not found.</exception>
        Entry FindFile (string filename);

        /// <summary>
        /// System.IO.File.Exists() analog.
        /// </summary>
        bool FileExists (string filename);

        /// <summary>
        /// System.IO.Directory.Exists() analog.
        /// </summary>
        bool DirectoryExists (string filename);

        /// <summary>
        /// Open file for reading as stream.
        /// </summary>
        Stream OpenStream (Entry entry);

        /// <summary>
        /// Open file for reading as seekable binary stream.
        /// </summary>
        IBinaryStream OpenBinaryStream (Entry entry);

        /// <summary>
        /// Open file for reading as memory-mapped view.
        /// </summary>
        ArcView OpenView (Entry entry);

        /// <summary>
        /// Enumerates subdirectories and files in current directory.
        /// </summary>
        IEnumerable<Entry> GetFiles ();

        /// <summary>
        /// Returns enumeration of files within current directory that match specified pattern.
        /// </summary>
        IEnumerable<Entry> GetFiles (string pattern);

        /// <summary>
        /// System.IO.Path.Combine() analog.
        /// </summary>
        string CombinePath (params string[] paths);

        /// <summary>
        /// System.IO.Path.GetDirectoryName() analog.
        /// </summary>
        string GetDirectoryName (string path);

        /// <summary>
        /// Recursively enumerates files in the current directory and its subdirectories.
        /// Subdirectory entries are omitted from resulting set.
        /// </summary>
        IEnumerable<Entry> GetFilesRecursive ();

        string CurrentDirectory { get; set; }

        /// <summary>
        /// Gets file system statistics.
        /// </summary>
        FileSystemStats GetStats();

        /// <summary>
        /// Checks if the given path is a directory.
        /// </summary>
        bool IsDirectory (string path);

        /// <summary>
        /// Gets the size of a directory recursively.
        /// </summary>
        long GetDirectorySize (string path);
    }

    public class FileSystemStats
    {
        public long       TotalFiles { get; set; }
        public long TotalDirectories { get; set; }
        public long        TotalSize { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class SubDirEntry : Entry
    {
        public override string Type  { get { return "directory"; } }

        public SubDirEntry (string name)
        {
            Name = name;
            Size = 0;
        }
    }

    public static class VFSSync
    {
        private static readonly ReaderWriterLockSlim _vfsLock = new ReaderWriterLockSlim();

        /// <summary>
        /// Acquire a read lock for VFS operations that don't modify the structure (OpenImage, OpenStream, etc.)
        /// Multiple readers can access VFS concurrently.
        /// </summary>
        public static IDisposable ReadLock()
        {
            return new ReadLockHolder (_vfsLock);
        }

        /// <summary>
        /// Acquire a write lock for VFS operations that modify the structure (ChDir, navigation, disposal)
        /// Only one writer at a time, and no readers during write.
        /// </summary>
        public static IDisposable WriteLock()
        {
            return new WriteLockHolder (_vfsLock);
        }

        private class ReadLockHolder : IDisposable
        {
            private readonly ReaderWriterLockSlim _lock;
            private bool _disposed;

            public ReadLockHolder (ReaderWriterLockSlim lockObj)
            {
                _lock = lockObj;
                _lock.EnterReadLock();
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _lock.ExitReadLock();
                    _disposed = true;
                }
            }
        }

        private class WriteLockHolder : IDisposable
        {
            private readonly ReaderWriterLockSlim _lock;
            private bool _disposed;

            public WriteLockHolder (ReaderWriterLockSlim lockObj)
            {
                _lock = lockObj;
                _lock.EnterWriteLock();
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _lock.ExitWriteLock();
                    _disposed = true;
                }
            }
        }
    }

    public sealed class PhysicalFileSystem : IFileSystem
    {
        private readonly ConcurrentDictionary<string, FileSystemStats> _statsCache = new ConcurrentDictionary<string, FileSystemStats>();

        public string CurrentDirectory
        {
            get { return Directory.GetCurrentDirectory(); }
            set { Directory.SetCurrentDirectory (value); }
        }

        public string CombinePath (params string[] paths)
        {
            if (paths == null || paths.Length == 0)
                return string.Empty;

            if (paths.Length == 1)
                return paths[0];

            return Path.Combine (paths);
        }

        public string GetDirectoryName (string path)
        {
            return Path.GetDirectoryName (path);
        }

        public Entry FindFile (string filename)
        {
            var fullPath = Path.GetFullPath (filename);
            if (!File.Exists (fullPath) && !Directory.Exists (fullPath))
                throw new FileNotFoundException ("Unable to find the specified file.", filename);

            var attr = File.GetAttributes (fullPath);
            if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                return new SubDirEntry (fullPath);
            else
                return EntryFromFileInfo (new FileInfo (fullPath));
        }

        public bool FileExists (string filename)
        {
            return File.Exists (filename);
        }

        public bool DirectoryExists (string path)
        {
            return IsDirectory (path);
        }

        public bool IsDirectory (string path)
        {
            return Directory.Exists (path);
        }

        public IEnumerable<Entry> GetFiles ()
        {
            var info = new DirectoryInfo (CurrentDirectory);
            var entries = new List<Entry>();

            Parallel.ForEach (info.EnumerateDirectories(), subdir =>
            {
                if (0 == (subdir.Attributes & (FileAttributes.System /*| FileAttributes.Hidden*/)))
                {
                    lock (entries)
                    {
                        entries.Add (new SubDirEntry (subdir.FullName));
                    }
                }
            });

            Parallel.ForEach (info.EnumerateFiles(), file =>
            {
                if (0 == (file.Attributes & (FileAttributes.System /*| FileAttributes.Hidden*/)))
                {
                    lock (entries)
                    {
                        entries.Add (EntryFromFileInfo (file));
                    }
                }
            });

            return entries.OrderBy (e => e.Name);
        }

        public IEnumerable<Entry> GetFiles (string pattern)
        {
            string path = GetDirectoryName (pattern);
            pattern = Path.GetFileName (pattern);
            path = CombinePath (CurrentDirectory, path);
            var info = new DirectoryInfo (path);

            foreach (var file in info.EnumerateFiles (pattern))
            {
                if (0 != (file.Attributes & (FileAttributes.System /*| FileAttributes.Hidden*/)))
                    continue;
                yield return EntryFromFileInfo (file);
            }
        }

        public IEnumerable<Entry> GetFilesRecursive ()
        {
            var info = new DirectoryInfo (CurrentDirectory);
            foreach (var file in info.EnumerateFiles ("*", SearchOption.AllDirectories))
            {
                if (0 != (file.Attributes & (FileAttributes.System /*| FileAttributes.Hidden*/)))
                    continue;
                yield return EntryFromFileInfo (file);
            }
        }

        public FileSystemStats GetStats ()
        {
            return _statsCache.GetOrAdd (CurrentDirectory, path =>
            {
                var stats = new FileSystemStats();
                var info = new DirectoryInfo (path);

                if (info.Exists)
                {
                    var files = info.GetFiles ("*", SearchOption.AllDirectories);
                    var dirs  = info.GetDirectories ("*", SearchOption.AllDirectories);

                    stats.TotalFiles       = files.Length;
                    stats.TotalDirectories = dirs.Length;
                    stats.TotalSize        = files.Sum (f => f.Length);
                    stats.LastModified     = 
                         files.Concat (dirs.Cast<FileSystemInfo>())
                        .Max (f => f.LastWriteTime);
                }

                return stats;
            });
        }

        public long GetDirectorySize (string path)
        {
            var info = new DirectoryInfo (path);
            if (!info.Exists)
                return 0;

            return info.GetFiles ("*", SearchOption.AllDirectories)
                .Sum (f => f.Length);
        }

        private Entry EntryFromFileInfo (FileInfo file)
        {
            var entry = new Entry {
                Name = file.FullName,
                Size = (uint)Math.Min (file.Length, uint.MaxValue)
            };
            entry.Type = FormatCatalog.Instance.GetTypeFromName (file.FullName, null, null, file.Length);
            return entry;
        }

        public Stream OpenStream (Entry entry)
        {
            return File.OpenRead (entry.Name);
        }

        public IBinaryStream OpenBinaryStream (Entry entry)
        {
            var input = OpenStream (entry);
            return new BinaryStream (input, entry.Name);
        }

        public ArcView OpenView (Entry entry)
        {
            return new ArcView (entry.Name);
        }

        public void Dispose ()
        {
            _statsCache.Clear();
            GC.SuppressFinalize (this);
        }

        /// <summary>
        /// Create file named <paramref name="filename"/> in current directory and open it
        /// for writing. Overwrites existing file, if any.
        /// </summary>
        static public Stream CreateFile (string filename)
        {
            filename = CreatePath (filename);
            return File.Create (filename);
        }

        /// <summary>
        /// Create all directories that lead to <paramref name="filename"/>, if any.
        /// </summary>
        static public string CreatePath (string filename)
        {
            filename = NormalizePath (filename);
            string dir = Path.GetDirectoryName (filename);
            if (!string.IsNullOrEmpty (dir)) // check for malformed filenames
            {
                string root = Path.GetPathRoot (dir);
                if (!string.IsNullOrEmpty (root))
                    dir = dir.Substring (root.Length); // strip root

                string cwd = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar;
                dir = Path.GetFullPath (dir);
                filename = Path.GetFileName (filename);
                filename = SanitizeFileName (filename);

                // check whether filename would reside within current directory
                if (dir.StartsWith (cwd, StringComparison.OrdinalIgnoreCase))
                {
                    // path looks legit, create it
                    Directory.CreateDirectory (dir);
                    filename = Path.Combine (dir, filename);
                }
            }
            return filename;
        }

        public static string NormalizePath (string path)
        {
            if (string.IsNullOrEmpty (path))
                return path;
            return path.Replace('/', Path.DirectorySeparatorChar);
        }

        public static string SanitizeFileName (string filename)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized    = new StringBuilder (filename.Length);

            foreach (char c in filename)
            {
                if (Array.IndexOf (invalidChars, c) >= 0)
                    sanitized.Append('_');
                else
                    sanitized.Append (c);
            }

            return sanitized.ToString();
        }
    }

    public abstract class ArchiveFileSystem : IFileSystem
    {
        protected readonly ArcFile                      m_arc;
        protected readonly Dictionary<string, Entry>    m_dir;

        public ArcFile Source { get { return m_arc; } }

        public abstract string CurrentDirectory { get; set; }

        public ArchiveFileSystem (ArcFile arc)
        {
            m_arc = arc;
            m_dir = new Dictionary<string, Entry> (arc.Dir.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in arc.Dir)
            {
                if (!m_dir.ContainsKey (entry.Name))
                    m_dir.Add (entry.Name, entry);
            }
        }

        public bool FileExists (string filename)
        {
            return m_dir.ContainsKey (filename)
                || !string.IsNullOrEmpty (CurrentDirectory)
                   && m_dir.ContainsKey (CombinePath (CurrentDirectory, filename));
        }

        public bool DirectoryExists (string path)
        {
            return IsDirectory (path);
        }

        public bool IsDirectory (string path)
        {
            var dirPath = path.TrimEnd ('/', '\\') + VFS.DIR_DELIMITER;
            return m_dir.Keys.Any (k => k.StartsWith (dirPath, StringComparison.OrdinalIgnoreCase));
        }

        public Stream OpenStream (Entry entry)
        {
            return m_arc.OpenEntry (entry);
        }

        public IBinaryStream OpenBinaryStream (Entry entry)
        {
            return m_arc.OpenBinaryEntry (entry);
        }

        public ArcView OpenView (Entry entry)
        {
            return m_arc.OpenView (entry);
        }

        public abstract Entry FindFile (string filename);

        public abstract IEnumerable<Entry> GetFiles ();

        public abstract IEnumerable<Entry> GetFilesRecursive ();

        public abstract string CombinePath (params string[] paths);

        public abstract string GetDirectoryName (string path);

        public abstract IEnumerable<Entry> GetFiles (string pattern);

        public abstract FileSystemStats GetStats();

        public abstract long GetDirectorySize (string path);

        #region IDisposable Members
        bool _arc_disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!_arc_disposed)
            {
                if (disposing)
                {
                    m_arc.Dispose();
                }
                _arc_disposed = true;
            }
        }
        #endregion
    }

    public class FlatArchiveFileSystem : ArchiveFileSystem
    {
        private readonly Lazy<FileSystemStats> _stats;

        public override string CurrentDirectory
        {
            get { return ""; }
            set
            {
                if (string.IsNullOrEmpty (value))
                    return;
                if (VFS.DIR_PARENT == value || VFS.DIR_CURRENT == value)
                    return;
                if ("\\" == value || VFS.DIR_DELIMITER == value)
                    return;
                throw new DirectoryNotFoundException();
            }
        }

        public FlatArchiveFileSystem (ArcFile arc) : base (arc)
        {
            _stats = new Lazy<FileSystemStats>(() => CalculateStats());
        }

        public override Entry FindFile (string filename)
        {
            Entry entry = null;
            if (!m_dir.TryGetValue (filename, out entry))
                throw new FileNotFoundException ("Unable to find the specified file.", filename);
            return entry;
        }

        public override IEnumerable<Entry> GetFiles ()
        {
            return m_arc.Dir;
        }

        public override IEnumerable<Entry> GetFilesRecursive ()
        {
            return m_arc.Dir;
        }

        public override IEnumerable<Entry> GetFiles (string pattern)
        {
            var glob = new FileNameGlob (pattern);
            return m_arc.Dir.Where (f => glob.IsMatch (f.Name));
        }

        public override string CombinePath (params string[] paths)
        {
            // Flat archives don't have subdirectories
            return paths?.LastOrDefault() ?? string.Empty;
        }

        public override string GetDirectoryName (string path)
        {
            return "";
        }

        public override FileSystemStats GetStats()
        {
            return _stats.Value;
        }

        public override long GetDirectorySize (string path)
        {
            return 0;
        }

        private FileSystemStats CalculateStats()
        {
            var stats = new FileSystemStats
            {
                TotalFiles = m_arc.Dir.Count,
                TotalDirectories = 0,
                TotalSize = m_arc.Dir.Sum (e => (long)e.Size),
                LastModified = DateTime.Now
            };
            return stats;
        }
    }

    public class TreeArchiveFileSystem : ArchiveFileSystem
    {
        private string  m_cwd;
        private readonly ConcurrentDictionary<string, FileSystemStats> _statsCache = new ConcurrentDictionary<string, FileSystemStats>();
        private static readonly char[] m_path_delimiters = { '/', '\\' };

        private static string PathDelimiter { get; set; }

        public TreeArchiveFileSystem (ArcFile arc) : base (arc)
        {
            m_cwd = "";
            PathDelimiter = VFS.DIR_DELIMITER;
        }

        public override string CurrentDirectory
        {
            get { return m_cwd; }
            set { ChDir (value); }
        }

        public override string CombinePath (params string[] paths)
        {
            if (paths.Length == 2)
            {
                if (0 == paths[0].Length)
                    return paths[1];
                if (0 == paths[1].Length)
                    return paths[0];
                if (paths[0].EndsWith (PathDelimiter))
                    return paths[0] + paths[1];
            }
            return string.Join (PathDelimiter, paths);
        }

        private string NormalizePath (string path)
        {
            if (string.IsNullOrEmpty (path))
                return path;
            if (PathDelimiter != "\\")
                return path.Replace ("\\", PathDelimiter);
            return path;
        }

        public override Entry FindFile (string filename)
        {
            Entry entry = null;
            filename = NormalizePath (filename);
            if (m_dir.TryGetValue (filename, out entry))
                return entry;
            if (m_dir.TryGetValue (CombinePath (CurrentDirectory, filename), out entry))
                return entry;
            var dir_name = filename + PathDelimiter;
            if (m_dir.Keys.Any (n => n.StartsWith (dir_name)))
                return new SubDirEntry (filename);
            throw new FileNotFoundException ("Unable to find the specified file.", filename);
        }

        static readonly Regex path_re = new Regex (@"\G[/\\]?([^/\\]+)([/\\])");

        public override IEnumerable<Entry> GetFiles ()
        {
            IEnumerable<Entry> dir = GetFilesRecursive();
            var root_dir = m_cwd;
            if (!string.IsNullOrEmpty (root_dir))
                root_dir += PathDelimiter;

            var subdirs = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
            foreach (var entry in dir)
            {
                var match = path_re.Match (entry.Name, root_dir.Length);
                if (match.Success)
                {
                    string name = match.Groups[1].Value;
                    if (subdirs.Add (name))
                    {
                        PathDelimiter = match.Groups[2].Value;
                        yield return new SubDirEntry (root_dir+name);
                    }
                }
                else
                {
                    yield return entry;
                }
            }
        }

        public override IEnumerable<Entry> GetFilesRecursive ()
        {
            if (0 == m_cwd.Length)
                return m_arc.Dir;
            var path = m_cwd + PathDelimiter;
            return from file in m_arc.Dir
                   where file.Name.StartsWith (path, StringComparison.OrdinalIgnoreCase)
                   select file;
        }

        public override IEnumerable<Entry> GetFiles (string pattern)
        {
            string path = GetDirectoryName (pattern);
            if (string.IsNullOrEmpty (path))
                path = CurrentDirectory;
            pattern = Path.GetFileName (pattern);
            var glob = new FileNameGlob (pattern);
            if (string.IsNullOrEmpty (path))
            {
                return m_arc.Dir.Where (f => glob.IsMatch (Path.GetFileName (f.Name)));
            }
            else
            {
                path += PathDelimiter;
                return m_arc.Dir.Where (f => f.Name.StartsWith (path, StringComparison.OrdinalIgnoreCase)
                                             && glob.IsMatch (Path.GetFileName (f.Name)));
            }
        }

        public IEnumerable<Entry> GetFilesRecursive (IEnumerable<Entry> list)
        {
            var result = new List<Entry>();
            foreach (var entry in list)
            {
                if (!(entry is SubDirEntry)) // add ordinary file
                    result.Add (entry);
                else if (VFS.DIR_PARENT == entry.Name) // skip reference to parent directory
                    continue;
                else // add all files contained within directory, recursive
                {
                    var dir_name = entry.Name+PathDelimiter;
                    result.AddRange (from file in m_arc.Dir
                                     where file.Name.StartsWith (dir_name, StringComparison.OrdinalIgnoreCase)
                                     select file);
                }
            }
            return result;
        }

        public override FileSystemStats GetStats()
        {
            return _statsCache.GetOrAdd (CurrentDirectory, path => CalculateStats (path));
        }

        public override long GetDirectorySize (string path)
        {
            path = NormalizePath (path);
            var dirPath = string.IsNullOrEmpty (path) ? "" : path + PathDelimiter;
            return m_arc.Dir
                .Where (e => e.Name.StartsWith (dirPath, StringComparison.OrdinalIgnoreCase))
                .Sum (e => (long)e.Size);
        }

        private FileSystemStats CalculateStats (string path)
        {
            path = NormalizePath (path);
            var entries = string.IsNullOrEmpty (path)
                ? m_arc.Dir
                : m_arc.Dir.Where (e => e.Name.StartsWith (path + PathDelimiter, StringComparison.OrdinalIgnoreCase));

            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                var relativePath = string.IsNullOrEmpty (path)
                    ? entry.Name
                    : entry.Name.Substring (path.Length + 1);

                var parts = relativePath.Split (m_path_delimiters, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    dirs.Add (string.Join (PathDelimiter, parts.Take (i + 1)));
                }
            }

            return new FileSystemStats
            {
                TotalFiles = entries.Count(),
                TotalDirectories = dirs.Count,
                TotalSize = entries.Sum (e => (long)e.Size),
                LastModified = DateTime.Now
            };
        }

        private void ChDir (string path)
        {
            if (string.IsNullOrEmpty (path))
            {
                m_cwd = "";
                return;
            }

            var cur_dir = new List<string>();

            if (-1 != Array.IndexOf (m_path_delimiters, path[0]))
                path = path.TrimStart (m_path_delimiters);
            else if (VFS.DIR_PARENT == path && !string.IsNullOrEmpty (m_cwd))
                cur_dir.AddRange (m_cwd.Split (m_path_delimiters));

            var path_list = path.Split (m_path_delimiters);
            foreach (var dir in path_list)
            {
                if (VFS.DIR_CURRENT == dir)
                    continue;
                else if (VFS.DIR_PARENT == dir)
                {
                    if (0 == cur_dir.Count)
                        continue;
                    cur_dir.RemoveAt (cur_dir.Count - 1);
                }
                else
                    cur_dir.Add (dir);
            }

            // detect delimiters since we can have both mixed (TODO: fix the plugin if this happens)
            var firstEntry = m_arc.Dir.FirstOrDefault(e => e.Name.Contains('/') || e.Name.Contains('\\'));
            if (firstEntry != null)
            {
                if (firstEntry.Name.Contains('/'))
                    PathDelimiter = "/";
                else if (firstEntry.Name.Contains('\\'))
                    PathDelimiter = "\\";
            }

            string new_path = string.Join (PathDelimiter, cur_dir);
            if (0 != new_path.Length)
            {
                var dir_name = new_path + PathDelimiter;
                var entry = m_arc.Dir.FirstOrDefault (e => e.Name.StartsWith (dir_name, StringComparison.OrdinalIgnoreCase));
                if (null == entry)
                    throw new DirectoryNotFoundException();
            }
            m_cwd = new_path;
        }

        public override string GetDirectoryName (string path)
        {
            int sep = path.LastIndexOfAny (m_path_delimiters);
            if (-1 == sep)
                return "";
            return path.Substring (0, sep);
        }

        protected override void Dispose (bool disposing)
        {
            if (disposing)
                _statsCache.Clear();

            base.Dispose (disposing);
        }
    }

    public sealed class FileSystemStack : IDisposable
    {
        Stack<IFileSystem>  m_fs_stack = new Stack<IFileSystem>();
        Stack<string> m_arc_name_stack = new Stack<string>();

        public IEnumerable<IFileSystem> All { get { return m_fs_stack; } }

        public IFileSystem Top { get { return m_fs_stack.Peek(); } }
        public int       Count { get { return m_fs_stack.Count; } }
        public IEnumerable<string> ArcStack { get { return m_arc_name_stack; } }

        public ArcFile      CurrentArchive { get; private set; }
        private IFileSystem LastVisitedArc { get; set; }
        private string     LastVisitedPath { get; set; }

        public FileSystemStack ()
        {
            m_fs_stack.Push (new PhysicalFileSystem());
        }

        public void ChDir (Entry entry)
        {
            if (entry is SubDirEntry)
            {
                if (Count > 1 && VFS.DIR_PARENT == entry.Name && string.IsNullOrEmpty (Top.CurrentDirectory))
                {
                    Pop();
                    if (!string.IsNullOrEmpty (LastVisitedPath))
                    {
                        Top.CurrentDirectory = Top.GetDirectoryName (LastVisitedPath);
                    }
                }
                else
                {
                    Top.CurrentDirectory = entry.Name;
                }
                return;
            }

            if (entry.Name == LastVisitedPath && null != LastVisitedArc)
            {
                Push (LastVisitedPath, LastVisitedArc);
                var fs = LastVisitedArc as ArchiveFileSystem;
                if (null != fs)
                    CurrentArchive = fs.Source;
                return;
            }

            // Only flush if we're not opening a nested archive
            if (Count == 1)
                Flush();

            var arc = ArcFile.TryOpen (entry);
            if (null == arc)
            {
                if (FormatCatalog.Instance.LastError is OperationCanceledException)
                    ExceptionDispatchInfo.Capture (FormatCatalog.Instance.LastError).Throw();
                else
                    throw new UnknownFormatException (FormatCatalog.Instance.LastError);
            }
            try
            {
                Push (entry.Name, arc.CreateFileSystem());
                CurrentArchive = arc;
            }
            catch
            {
                arc.Dispose();
                throw;
            }
        }

        private void Push (string path, IFileSystem fs)
        {
            m_fs_stack.Push (fs);
            m_arc_name_stack.Push (path);
        }

        internal void Pop ()
        {
            if (m_fs_stack.Count > 1)
            {
                LastVisitedArc = m_fs_stack.Pop();
                LastVisitedPath = m_arc_name_stack.Pop();

                // Only flush if we're returning to physical filesystem
                if (m_fs_stack.Count == 1)
                    Flush();

                if (m_fs_stack.Count > 1 && m_fs_stack.Peek() is ArchiveFileSystem)
                    CurrentArchive = (m_fs_stack.Peek() as ArchiveFileSystem).Source;
                else
                    CurrentArchive = null;
            }
        }

        public void Flush ()
        {
            if (LastVisitedArc != null && (0 == Count || LastVisitedArc != Top))
            {
                LastVisitedArc.Dispose();
                LastVisitedArc = null;
                LastVisitedPath = null;
            }
        }

        private bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                // Dispose archives in reverse order
                Flush();
                // Dispose file systems
                foreach (var fs in m_fs_stack.Reverse())
                            fs.Dispose();

                _disposed = true;
            }
            GC.SuppressFinalize (this);
        }
    }

    public static class VFS
    {
        private static FileSystemStack m_vfs = new FileSystemStack();
        private static readonly ConcurrentDictionary<string, WeakReference> _entryCache = new ConcurrentDictionary<string, WeakReference>();

        /// <summary>
        /// Top, or "current" filesystem in VFS hierarchy.
        /// </summary>
        public static IFileSystem Top { get { return m_vfs.Top; } }

        /// <summary>
        /// Whether top filesystem is virtual (i.e. represents an archive).
        /// </summary>
        public static bool  IsVirtual { get { return m_vfs.Count > 1; } }

        /// <summary>
        /// Number of filesystems in hierarchy. ==1 when only physical file system is represented.
        /// Always >= 1
        /// </summary>
        public static  int      Count { get { return m_vfs.Count; } }

        /// <summary>
        /// Archive corresponding to the top filesystem, or null if file system doesn't have underlying
        /// archive file.
        /// </summary>
        public static ArcFile CurrentArchive { get { return m_vfs.CurrentArchive; } }

        private static string[] m_top_path = new string[1];

        public static readonly    string DIR_PARENT = "..";
        public static readonly   string DIR_CURRENT = ".";
        public static readonly string DIR_DELIMITER = "/";

        public static IEnumerable<string> FullPath
        {
            get
            {
                m_top_path[0] = Top.CurrentDirectory;
                if (1 == Count)
                    return m_top_path;
                else
                    return m_vfs.ArcStack.Reverse().Concat (m_top_path);
            }
            set
            {
                if (!value.Any())
                    return;

                var desired = value.ToArray();
                int desired_vfs_count = desired.Length;

                // Find common path prefix with current state
                int i = 0;
                using (var arc_iterator = m_vfs.ArcStack.Reverse().GetEnumerator())
                {
                    while (i < desired_vfs_count - 1 && arc_iterator.MoveNext())
                    {
                        if (arc_iterator.Current != desired[i])
                            break;
                        ++i;
                    }
                }

                // Navigate up to common ancestor
                while (Count > i + 1)
                    m_vfs.Pop();

                // Navigate down to target
                while (Count < desired_vfs_count)
                {
                    try
                    {
                        var entry = m_vfs.Top.FindFile (desired[Count-1]);
                        if (entry is SubDirEntry)
                        {
                            // It's a directory, not an archive
                            if (Count == desired_vfs_count - 1)
                            {
                                // Last component can be a directory
                                m_vfs.Top.CurrentDirectory = entry.Name;
                                return;
                            }
                            else
                            {
                                // Middle component shouldn't be just a directory
                                throw new DirectoryNotFoundException ($"Cannot navigate through directory: {desired[Count-1]}");
                            }
                        }
                        m_vfs.ChDir (entry);
                    }
                    catch (FileNotFoundException)
                    {
                        // File not found, try to handle gracefully
                        // Check if it's the last component and might be a directory
                        if (Count == desired_vfs_count - 1)
                        {
                            try
                            {
                                m_vfs.Top.CurrentDirectory = desired[Count-1];
                                return;
                            }
                            catch
                            {
                                throw new FileNotFoundException (string.Format ("Unable to find: {0}", desired[Count-1]));
                            }
                        }
                        throw;
                    }
                }

                // Set the final directory
                m_vfs.Top.CurrentDirectory = desired.Last();
            }
        }

        /// <summary>
        /// Try to navigate to a path, returning success status
        /// </summary>
        public static bool TrySetFullPath (IEnumerable<string> path)
        {
            try
            {
                FullPath = path;
                return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Get all filesystem levels for debugging
        /// </summary>
        public static IEnumerable<IFileSystem> AllFileSystems
        {
            get { return m_vfs.All; }
        }

        public static string CombinePath (params string[] paths)
        {
            if (paths == null || paths.Length == 0)
                return string.Empty;

            if (paths.Length == 1)
                return paths[0];

            return m_vfs.Top.CombinePath (paths);
        }

        public static string GetRelativePath (string fullPath, string basePath)
        {
            if (fullPath.StartsWith (basePath, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring (basePath.Length).TrimStart('\\', '/');
            return Path.GetFileName (fullPath);
        }

        public static string NormalizePath (string path)
        {
            if (string.IsNullOrEmpty (path))
                return path;
            return path.Replace ("\\", DIR_DELIMITER);
        }

        public static string GetDirectoryName (string path)
        {
            return m_vfs.Top.GetDirectoryName (path);
        }

        /// <summary>
        /// Gets just the filename portion of a path.
        /// </summary>
        public static string GetFileName (string path)
        {
            int sep = path.LastIndexOfAny (PathSeparatorChars);
            if (sep >= 0)
                return path.Substring (sep + 1);
            return path;
        }

        /// <summary>
        /// Gets just the filename portion of a path.
        /// </summary>
        public static string GetExtension (string path)
        {
            if (string.IsNullOrEmpty (path))
                return string.Empty;

            string fileName = GetFileName (path);
            int lastDot = path.LastIndexOf('.');
            if (lastDot < 1 || lastDot == fileName.Length - 1)
                return string.Empty;

            return path.Substring(lastDot);
        }

        /// <summary>
        /// Checks if the given path is rooted (absolute path).
        /// </summary>
        public static bool IsPathRooted (string path)
        {
            if (string.IsNullOrEmpty (path))
                return false;

            if (path.Length >= 3 && char.IsLetter (path[0]) && path[1] == ':' &&
                (path[2] == '\\' || path[2] == '/'))
                return true;

            if (path.Length >= 2 && path[0] == '\\' && path[1] == '\\')
                return true;

            if (path[0] == '/' || path[0] == '\\')
                return true;

            return false;
        }

        public static Entry FindFile (string filename)
        {
            if (VFS.DIR_PARENT == filename)
                return new SubDirEntry (VFS.DIR_PARENT);

            WeakReference cachedRef;
            if (_entryCache.TryGetValue (filename, out cachedRef))
            {
                var cached = cachedRef.Target as Entry;
                if (cached != null)
                    return cached;
            }

            var entry = m_vfs.Top.FindFile (filename);
            _entryCache[filename] = new WeakReference (entry);
            return entry;
        }

        public static bool DirectoryExists (string dirname)
        {
            return m_vfs.Top.DirectoryExists (dirname);
        }

        public static bool FileExists (string filename)
        {
            return m_vfs.Top.FileExists (filename);
        }

        public static Stream OpenStream (Entry entry)
        {
            return m_vfs.Top.OpenStream (entry);
        }

        public static IBinaryStream OpenBinaryStream (Entry entry)
        {
            return m_vfs.Top.OpenBinaryStream (entry);
        }

        public static ArcView OpenView (Entry entry)
        {
            return m_vfs.Top.OpenView (entry);
        }

        public static IImageDecoder OpenImage (Entry entry)
        {
            var fs = m_vfs.Top;
            var arc_fs = fs as ArchiveFileSystem;
            if (arc_fs != null)
                return arc_fs.Source.OpenImage (entry);

            var input = fs.OpenBinaryStream (entry);
            return ImageFormatDecoder.Create (input);
        }

        public static VideoData OpenVideo (Entry entry)
        {
            using (var file = OpenBinaryStream (entry))
            {
                var format = VideoFormat.FindFormat (file);
                if (null == format)
                    throw new InvalidFormatException();
                file.Position = 0;
                return format.Item1.Read (file, format.Item2);
            }
        }

        public static Stream OpenStream (string filename)
        {
            return m_vfs.Top.OpenStream (m_vfs.Top.FindFile (filename));
        }

        public static IBinaryStream OpenBinaryStream (string filename)
        {
            return m_vfs.Top.OpenBinaryStream (m_vfs.Top.FindFile (filename));
        }

        public static ArcView OpenView (string filename)
        {
            return m_vfs.Top.OpenView (m_vfs.Top.FindFile (filename));
        }

        public static void ChDir (Entry entry)
        {
            _entryCache.Clear();
            m_vfs.ChDir (entry);
        }

        public static void ChDir (string path)
        {
            m_vfs.ChDir (FindFile (path));
        }

        public static void Flush ()
        {
            _entryCache.Clear();
            m_vfs.Flush();
        }

        public static IEnumerable<Entry> GetFiles ()
        {
            return m_vfs.Top.GetFiles();
        }

        /// <summary>
        /// Returns enumeration of files within current directory that match specified pattern.
        /// </summary>
        public static IEnumerable<Entry> GetFiles (string pattern)
        {
            return m_vfs.Top.GetFiles (pattern);
        }

        /// <summary>
        /// Gets file system statistics for current directory.
        /// </summary>
        public static FileSystemStats GetStats()
        {
            return m_vfs.Top.GetStats();
        }

        /// <summary>
        /// Checks if the given path is a directory.
        /// </summary>
        public static bool IsDirectory (string path)
        {
            return m_vfs.Top.IsDirectory (path);
        }

        public static readonly ISet<char> InvalidFileNameChars = new HashSet<char> (Path.GetInvalidFileNameChars());

        public static readonly char[] PathSeparatorChars = { '\\', '/', ':' };

        /// <summary>
        /// Returns true if given <paramref name="path"/> points to a specified <paramref name="filename"/>.
        /// </summary>
        public static bool IsPathEqualsToFileName (string path, string filename)
        {
            // first, filter out completely different paths
            if (!path.EndsWith (filename, StringComparison.OrdinalIgnoreCase))
                return false;

            // now, compare length of filename portion of the path
            int filename_index = path.LastIndexOfAny (PathSeparatorChars);
            filename_index++;
            int filename_portion_length = path.Length - filename_index;
            return filename.Length == filename_portion_length;
        }

        /// <summary>
        /// Change filename portion of the <paramref name="path"/> to <paramref name="target"/>.
        /// </summary>
        public static string ChangeFileName (string path, string target)
        {
            var dir_name = GetDirectoryName (path);
            return CombinePath (dir_name, target);
        }

        /// <summary>
        /// Searches for a file through all filesystem levels, starting from the current (top) level
        /// and working down to the physical filesystem.
        /// </summary>
        public static Entry FindFileInHierarchy (string filename)
        {
            // First try the current filesystem
            try
            {
                return FindFile (filename);
            }
            catch (FileNotFoundException)
            {
                // Continue searching in lower levels
            }

            // Search through all filesystem levels
            foreach (var fs in m_vfs.All)
            {
                try
                {
                    var entry = fs.FindFile (filename);
                    if (entry != null)
                        return entry;
                }
                catch (FileNotFoundException) { }
            }

            throw new FileNotFoundException ("Unable to find the specified file in any filesystem level.", filename);
        }

        /// <summary>
        /// Checks if a file exists in any filesystem level.
        /// </summary>
        public static bool FileExistsInHierarchy (string filename)
        {
            try
            {
                FindFileInHierarchy (filename);
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
        }

        /// <summary>
        /// Opens a stream for a file that may exist in any filesystem level.
        /// </summary>
        public static Stream OpenStreamInHierarchy (string filename)
        {
            var entry = FindFileInHierarchy (filename);

            foreach (var fs in m_vfs.All)
            {
                try
                {
                    if (fs.FileExists (entry.Name))
                        return fs.OpenStream (entry);
                }
                catch { }
            }

            throw new FileNotFoundException ("Unable to open file from any filesystem level.", filename);
        }

        /// <summary>
        /// Opens a stream for an entry that may exist in any filesystem level.
        /// </summary>
        public static Stream OpenStreamInHierarchy (Entry entry)
        {
            foreach (var fs in m_vfs.All)
            {
                try
                {
                    return fs.OpenStream (entry);
                }
                catch { }
            }

            throw new FileNotFoundException ("Unable to open entry from any filesystem level.", entry.Name);
        }

        /// <summary>
        /// Searches for a file in the directory of the current archive file.
        /// This is useful for finding companion files with separate resources.
        /// </summary>
        public static Entry FindFileInArchiveDirectory (string filename)
        {
            if (CurrentArchive == null)
                throw new InvalidOperationException ("No archive is currently open.");

            var archiveDir = GetDirectoryName (CurrentArchive.File.Name);
            var fullPath = CombinePath (archiveDir, filename);

            return FindFileInHierarchy (fullPath);
        }

        const long MAX_STREAM_SIZE_IN_MEMORY = 256 * 1024 * 1024; // 256MB threshold

        /// <summary>
        /// Reads from any Stream like an internal one in an archive file,
        /// creating MemoryStream or temporeary file if the source is too big.
        /// This is useful for reading separately stored resources.
        /// </summary>
        public static byte[] ReadFromAnyStream (Stream input, long offset, long size)
        {
            if (input.CanSeek)
            {
                input.Position = offset;
                var data = new byte[size];
                input.Read (data, 0, (int)size);
                return data;
            }
            else
            {
                long streamSize = -1;
                try
                {
                    if (input.Length > 0)
                        streamSize = input.Length;
                }
                catch
                {
                    // Length not available, assume it might be large
                    streamSize = MAX_STREAM_SIZE_IN_MEMORY + 1;
                }

                if (streamSize < MAX_STREAM_SIZE_IN_MEMORY && streamSize > 0)
                {
                    // Small enough for memory
                    using (var memStream = new MemoryStream())
                    {
                        input.CopyTo (memStream);
                        memStream.Position = offset;
                        var data = new byte[size];
                        memStream.Read (data, 0, (int)size);
                        return data;
                    }
                }
                else
                {
                    // Use temp file for large or unknown size streams
                    string tempFile = Path.GetTempFileName() + "_GARbro_Stream";
                    try
                    {
                        using (var tempStream = File.Create (tempFile))
                        {
                            input.CopyTo (tempStream);
                        }

                        using (var fileStream = File.OpenRead (tempFile))
                        {
                            fileStream.Position = offset;
                            var data = new byte[size];
                            fileStream.Read (data, 0, (int)size);
                            return data;
                        }
                    }
                    finally
                    {
                        try { File.Delete (tempFile); } catch { }
                    }
                }
            }
        }
    }

    public class FileNameGlob
    {
        Regex  m_glob;

        public FileNameGlob (string pattern)
        {
            pattern = Regex.Escape (pattern);
            if (pattern.EndsWith (@"\.\*")) // "*" and "*.*" are equivalent
                pattern = pattern.Remove (pattern.Length-4) + @"(?:\..*)?";
            pattern = pattern.Replace (@"\*", ".*").Replace (@"\?", ".");
            m_glob = new Regex ("^"+pattern+"$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        public bool IsMatch (string str)
        {
            return m_glob.IsMatch (str);
        }
    }

    public class UnknownFormatException : FileFormatException
    {
        public UnknownFormatException () : base (Localization._T ("MsgUnknownFormat")) { }
        public UnknownFormatException (Exception inner) : base (Localization._T ("MsgUnknownFormat"), inner) { }
    }
}