using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace GameRes
{
    public class ArcFile : IDisposable
    {
        private ArcView            m_arc;
        private ArchiveFormat      m_interface;
        private ICollection<Entry> m_dir;

        protected ArcFile _parentArchive;
        protected Stream  _parentStream;
        protected Entry   _sourceEntry; // The entry in parent archive that this archive was opened from

        /// <summary>Tag that identifies this archive format.</summary>
        public string Tag { get { return m_interface.Tag; } }

        /// <summary>Short archive format description.</summary>
        public string Description { get { return m_interface.Description; } }

        /// <summary>Short archive format comment.</summary>
        public string Comment { get { return m_interface.Comment; } }

        /// <summary>Tags of formats related to this archive format (could be null).</summary>
        public IEnumerable<string> ContainedFormats { get { return m_interface.ContainedFormats; } }

        /// <summary>Memory-mapped view of the archive.</summary>
        public ArcView File { get { return m_arc; } }

        /// <summary>Archive contents.</summary>
        public ICollection<Entry> Dir { get { return m_dir; } }

        /// <summary>Gets the parent archive if this is a nested archive.</summary>
        public ArcFile ParentArchive { get { return _parentArchive; } }

        /// <summary>Gets the source entry in parent archive.</summary>
        public Entry SourceEntry { get { return _sourceEntry; } }

        /// <summary>Checks if this is a nested archive.</summary>
        public bool IsNested { get { return _parentArchive != null; } }

        public ArcFile (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
        {
            m_arc = arc;
            m_interface = impl;
            m_dir = dir;
        }

        /// <summary>
        /// Sets the parent archive for nested archive scenarios.
        /// </summary>
        /// <param name="parent">Parent archive containing this archive</param>
        /// <param name="sourceEntry">The entry in parent that was opened as this archive</param>
        /// <param name="parentStream">Optional stream from parent (for formats that need persistent data)</param>
        public virtual void SetParentArchive(ArcFile parent, Entry sourceEntry, Stream parentStream = null)
        {
            _parentArchive = parent;
            _sourceEntry = sourceEntry;

            // If parent stream is provided and not already a memory stream, convert it
            // to ensure data persistence even if parent is navigated away from
            if (parentStream != null && !(parentStream is MemoryStream))
            {
                var memStream = new MemoryStream();
                parentStream.CopyTo(memStream);
                parentStream.Dispose();
                memStream.Position = 0;
                _parentStream = memStream;
            }
            else
            {
                _parentStream = parentStream;
            }
        }

        /// <summary>
        /// Reopens the archive if it was created from a parent archive stream.
        /// </summary>
        protected virtual bool ReopenFromParent()
        {
            if (_parentArchive == null || _sourceEntry == null)
                return false;

            try
            {
                // Try to re-extract from parent
                using (var stream = _parentArchive.OpenEntry(_sourceEntry))
                {
                    // Convert to memory stream for persistence
                    var memStream = new MemoryStream();
                    stream.CopyTo(memStream);
                    memStream.Position = 0;

                    // Update parent stream
                    if (_parentStream != null)
                        _parentStream.Dispose();
                    _parentStream = memStream;

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Try to open <paramref name="filename"/> as archive.
        /// </summary>
        /// <inheritdoc cref="ArchiveFormat.TryOpen (ArcView)" />
        public static ArcFile TryOpen (string filename)
        {
            return TryOpen (VFS.FindFile (filename));
        }

        /// <summary>
        /// Try to open <paramref name="entry"/> as archive.
        /// </summary>
        /// <inheritdoc cref="TryOpen (string)" /> 
        public static ArcFile TryOpen (Entry entry)
        {
            if (entry.Size < 4)
                return null;
            var file = VFS.OpenView (entry);
            try
            {
                uint signature = file.View.ReadUInt32 (0);
                var formatImpls = FormatCatalog.Instance.FindFormats<ArchiveFormat>(entry.Name, signature);
                foreach (var impl in formatImpls)
                {
                    try
                    {
                        var arc = impl.TryOpen (file);
                        if (null != arc)
                        {
                            file = null; // file ownership passed to ArcFile
                            return arc;
                        }
                    }
                    catch (OperationCanceledException X)
                    {
                        FormatCatalog.Instance.LastError = X;
                        return null;
                    }
                    catch (Exception X)
                    {
                        // ignore failed open attmepts
                        Trace.WriteLine (string.Format ("[{0}] {1}: {2}", impl.Tag, entry.Name, X.Message));
                        FormatCatalog.Instance.LastError = X;
                    }
                }
            }
            finally
            {
                if (null != file)
                    file.Dispose();
            }
            return null;
        }

        /// <summary>
        /// Extract all entries from the archive into current directory.
        /// <paramref name="callback"/> could be used to observe/control extraction process.
        /// </summary>
        public void ExtractFiles (EntryCallback callback)
        {
            int i = 0;
            foreach (var entry in Dir.OrderBy (e => e.Offset))
            {
                var action = callback (i, entry, null);
                if (ArchiveOperation.Abort == action)
                    break;
                if (ArchiveOperation.Skip != action)
                    Extract (entry);
                ++i;
            }
        }

        /// <summary>
        /// Extract specified <paramref name="entry"/> into current directory.
        /// </summary>
        public void Extract (Entry entry)
        {
            if (-1 != entry.Offset)
                m_interface.Extract (this, entry);
        }

        /// <summary>
        /// Open specified <paramref name="entry"/> as Stream.
        /// </summary>
        public Stream OpenEntry (Entry entry)
        {
            try
            {
                return m_interface.OpenEntry (this, entry);
            }
            catch (ObjectDisposedException)
            {
                // If the archive was disposed but we have a parent, try to reopen
                if (IsNested && ReopenFromParent())
                {
                    return m_interface.OpenEntry (this, entry);
                }
                throw;
            }
        }

        /// <summary>
        /// Open specified <paramref name="entry"/> as memory-mapped view.
        /// </summary>
        public ArcView OpenView (Entry entry)
        {
            using (var stream = OpenEntry (entry))
            {
                long size;
                var packed_entry = entry as PackedEntry;
                if (stream.CanSeek)
                    size = stream.Length;
                else if (null != packed_entry && packed_entry.IsPacked)
                {
                    size = packed_entry.UnpackedSize;
                    if (0 == size)
                    {
                        using (var copy = new MemoryStream())
                        {
                            stream.CopyTo (copy);
                            copy.Position = 0;
                            return new ArcView (copy, entry.Name, (uint)copy.Length);
                        }
                    }
                }
                else
                    size = entry.Size;
                if (0 == size)
                    throw new FileSizeException (Localization._T ("MsgFileIsEmpty"));
                return new ArcView (stream, entry.Name, size);
            }
        }

        /// <summary>
        /// Open specified <paramref name="entry"/> as a seekable Stream.
        /// </summary>
        public Stream OpenSeekableEntry (Entry entry)
        {
            var input = OpenEntry (entry);
            if (input == null)
                throw new ArgumentException("OpenEntry mustn't return null in an ArcFile");
            if (input.CanSeek)
                return input;
            using (input)
            {
                int capacity = (int)entry.Size;
                var packed_entry = entry as PackedEntry;
                if (packed_entry != null && packed_entry.UnpackedSize != 0)
                    capacity = (int)packed_entry.UnpackedSize;
                var copy = new MemoryStream (capacity);
                input.CopyTo (copy);
                copy.Position = 0;
                return copy;
            }
        }

        /// <summary>
        /// Open specified <paramref name="entry"/> as <see cref="IBinaryStream"/>.
        /// </summary>
        public IBinaryStream OpenBinaryEntry (Entry entry)
        {
            var input = OpenSeekableEntry (entry);
            return BinaryStream.FromStream (input, entry.Name);
        }

        /// <summary>
        /// Open specified <paramref name="entry"/> as <see cref="IImageDecoder"/>.
        /// </summary>
        public IImageDecoder OpenImage (Entry entry)
        {
            return m_interface.OpenImage (this, entry);
        }

        /// <summary>
        /// Represent current <b>ArcFile</b> as <see cref="ArchiveFileSystem"/>.
        /// </summary>
        public ArchiveFileSystem CreateFileSystem ()
        {
            if (m_interface.IsHierarchic)
                return new TreeArchiveFileSystem (this);
            else
                return new FlatArchiveFileSystem (this);
        }

        #region IDisposable Members
        bool disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                    m_arc.Dispose();
                disposed = true;
            }
        }
        #endregion
    }

    public interface IExtensionProvider
    {
        string GetExtension();
    }
}
