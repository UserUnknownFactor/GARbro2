using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using GARbro.GUI.Properties;
using GameRes;

namespace GARbro.GUI
{
    public partial class MainWindow
    {
        private LinkedList<string> m_recent_files;

        public IEnumerable<Tuple<string, string>> RecentFiles
        {
            get
            {
                int i = 1;
                return m_recent_files.Select (f => Tuple.Create (f, string.Format ("_{0} {1}", i++, f)));
            }
        }

        private void PushRecentFile (string file)
        {
            var node = m_recent_files.Find (file);
            if (node != null && node == m_recent_files.First)
                return;
            if (null == node)
            {
                while (MaxRecentFiles <= m_recent_files.Count)
                    m_recent_files.RemoveLast();
                m_recent_files.AddFirst (file);
            }
            else
            {
                m_recent_files.Remove (node);
                m_recent_files.AddFirst (node);
            }
            RecentFilesMenu.ItemsSource = RecentFiles;
        }

        // Text encoding management
        private readonly Dictionary<string, int> _fileEncodingCache = new Dictionary<string, int>();
        private readonly LinkedList<FileEncodingEntry> _fileEncodingHistory = new LinkedList<FileEncodingEntry>();
        private const int MAX_ENCODING_HISTORY = 20;
        private bool _isManualEncodingChange = false;

        private IEnumerable<Encoding> m_encoding_list = GetEncodingList();
        public IEnumerable<Encoding> TextEncodings { get { return m_encoding_list; } }

        private class FileEncodingEntry
        {
            public string FileIdentifier { get; set; }
            public int CodePage { get; set; }
        }

        private void LoadEncodingHistory ()
        {
            try
            {
                if (Settings.Default.fileEncodingHistory != null)
                {
                    foreach (var entry in Settings.Default.fileEncodingHistory)
                    {
                        var parts = entry.Split ('|');
                        if (parts.Length >= 2 && int.TryParse (parts[1], out int codePage))
                        {
                            var historyEntry = new FileEncodingEntry
                            {
                                FileIdentifier = parts[0],
                                CodePage = codePage
                            };
                            _fileEncodingHistory.AddLast (historyEntry);
                            _fileEncodingCache[parts[0]] = codePage;
                        }
                    }
                }
                if (EncodingChoice.SelectedItem == null && EncodingChoice.Items.Count > 0)
                    EncodingChoice.SelectedIndex = 0;
            }
            catch { }
        }

        private void SaveEncodingHistory ()
        {
            try
            {
                var settings = Settings.Default;
                if (settings.fileEncodingHistory == null)
                    settings.fileEncodingHistory = new StringCollection();
                else
                    settings.fileEncodingHistory.Clear();

                foreach (var entry in _fileEncodingHistory)
                {
                    settings.fileEncodingHistory.Add (string.Format ("{0}|{1}", entry.FileIdentifier, entry.CodePage));
                }

                settings.Save();
            }
            catch { }
        }

        internal static IEnumerable<Encoding> GetEncodingList (bool exclude_utf16 = false)
        {
            var list = new HashSet<Encoding>();
            try
            {
                list.Add (Encoding.Default);
                var oem = System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                list.Add (Encoding.GetEncoding (oem));
            }
            catch (Exception ex)
            {
                if (ex is ArgumentException || ex is NotSupportedException)
                    list.Add (Encoding.GetEncoding (20127));
                else
                    throw;
            }
            try
            {
                list.Add (Encoding.Default);
                var oem = GetOEMCP();
                list.Add (Encoding.GetEncoding ((int)oem));
            }
            catch { }
            list.Add (Encoding.GetEncoding (932));
            list.Add (Encoding.GetEncoding (936));
            list.Add (Encoding.UTF8);
            if (!exclude_utf16)
            {
                list.Add (Encoding.Unicode);
                list.Add (Encoding.BigEndianUnicode);
                list.Add (Encoding.UTF32);
            }
            return list;
        }

        [System.Runtime.InteropServices.DllImport ("kernel32.dll")]
        static extern uint GetOEMCP ();

        private void OnEncodingSelect (object sender, SelectionChangedEventArgs e)
        {
            var enc = this.EncodingChoice.SelectedItem as Encoding;
            if (null == enc || _textPreviewHandler == null || !_textPreviewHandler.IsActive)
                return;

            _isManualEncodingChange = true;

            if (m_current_preview != null)
            {
                var fileIdentifier = GetFileIdentifier (m_current_preview);
                RememberFileEncoding (fileIdentifier, enc);
            }

            RefreshPreviewPane();
            _isManualEncodingChange = false;
        }

        private void HandleEncodingSelection (Entry entry, PreviewFile previousPreview)
        {
            if (!_isManualEncodingChange)
            {
                if (!m_current_preview.IsEqual (previousPreview?.Path, previousPreview?.Entry))
                {
                    if (entry.Type == "script" || entry.Type == "text" || entry.Type == "config" ||
                        (string.IsNullOrEmpty (entry.Type) && entry.Size < 0x100000))
                    {
                        var fileIdentifier = GetFileIdentifier (m_current_preview);
                        var rememberedEncoding = GetRememberedEncoding (fileIdentifier);
                        if (rememberedEncoding != null)
                            EncodingChoice.SelectedItem = rememberedEncoding;
                        else
                            EncodingChoice.SelectedItem = null;
                    }
                }
            }
        }

        internal string GetFileIdentifier (PreviewFile preview)
        {
            if (preview?.Path != null && preview.Path.Any())
            {
                return string.Join (VFS.DIR_DELIMITER,
                    preview.Path.Concat (new[] { preview.Name }));
            }
            return preview?.Name ?? "";
        }

        internal void RememberFileEncoding (string fileIdentifier, Encoding encoding)
        {
            if (string.IsNullOrEmpty (fileIdentifier) || encoding == null)
                return;

            _fileEncodingCache[fileIdentifier] = encoding.CodePage;
            var existingEntry = _fileEncodingHistory.FirstOrDefault (e => e.FileIdentifier == fileIdentifier);
            if (existingEntry != null)
                _fileEncodingHistory.Remove (existingEntry);

            _fileEncodingHistory.AddFirst (new FileEncodingEntry
            {
                FileIdentifier = fileIdentifier,
                CodePage = encoding.CodePage
            });

            while (_fileEncodingHistory.Count > MAX_ENCODING_HISTORY)
                _fileEncodingHistory.RemoveLast();

            SaveEncodingHistory();
        }

        private Encoding GetRememberedEncoding (string fileIdentifier)
        {
            if (string.IsNullOrEmpty (fileIdentifier))
                return null;

            if (_fileEncodingCache.TryGetValue (fileIdentifier, out int codePage))
            {
                try
                {
                    return Encoding.GetEncoding (codePage);
                }
                catch
                {
                    _fileEncodingCache.Remove (fileIdentifier);
                }
            }
            return null;
        }
    }
}