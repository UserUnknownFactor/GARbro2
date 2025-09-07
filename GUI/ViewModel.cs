using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Windows.Data;
using System.Runtime.InteropServices;

using GameRes;

namespace GARbro.GUI
{
    public class DirectoryViewModel : ObservableCollection<EntryViewModel>
    {
        public IReadOnlyList<string> Path { get; private set; }
        public IEnumerable<Entry>  Source { get; private set; }
        public bool             IsArchive { get; private set; }

        public DirectoryViewModel (IEnumerable<string> path, IEnumerable<Entry> filelist, bool is_archive)
        {
            Path = path.ToList();
            Source = filelist;
            IsArchive = is_archive;
            ImportFromSource();
        }

        protected void ImportFromSource ()
        {
            var last_dir = Path.Last();
            if (IsArchive || !string.IsNullOrEmpty (last_dir) && null != Directory.GetParent (last_dir))
            {
                Add (new EntryViewModel (new SubDirEntry (VFS.DIR_PARENT), -2));
            }
            foreach (var entry in Source)
            {
                int prio = entry is SubDirEntry ? -1 : 0;
                Add (new EntryViewModel (entry, prio));
            }
        }

        public EntryViewModel Find (string name)
        {
            return this.FirstOrDefault (e => e.Name.Equals (name, StringComparison.InvariantCultureIgnoreCase));
        }
    }

    public class EntryViewModel : INotifyPropertyChanged
    {
        public EntryViewModel (Entry entry, int priority)
        {
            Source = entry;
            Name = GetRelativePath(entry.Name);
            Priority = priority;
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get { return _isEditing; }
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    if (_isEditing)
                        EditingName = Name;
                    OnPropertyChanged("IsEditing");
                }
            }
        }

        private string _editingName;
        public string EditingName
        {
            get { return _editingName; }
            set
            {
                if (_editingName != value)
                {
                    _editingName = value;
                    OnPropertyChanged("EditingName");
                }
            }
        }

        private string GetRelativePath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath) || fullPath == VFS.DIR_PARENT)
                return fullPath;

            if (!Path.IsPathRooted(fullPath))
                return SafeGetFileName(fullPath);

            try
            {
                string currentDir = VFS.Top.CurrentDirectory;
                if (string.IsNullOrEmpty(currentDir))
                    currentDir = Directory.GetCurrentDirectory();

                currentDir = Path.GetFullPath(currentDir);
                fullPath = Path.GetFullPath(fullPath);

                if (fullPath.StartsWith(currentDir, StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath = fullPath.Substring(currentDir.Length);
                    if (relativePath.StartsWith(Path.DirectorySeparatorChar.ToString()) || 
                        relativePath.StartsWith(Path.AltDirectorySeparatorChar.ToString()))
                    {
                        relativePath = relativePath.Substring(1);
                    }
                    return relativePath;
                }
            }
            catch { }

            return SafeGetFileName(fullPath);
        }

        private static readonly char[] SeparatorCharacters = { '\\', '/' };

        /// <summary>
        /// Same as Path.GetFileName, but robustly ignores invalid characters
        /// </summary>
        string SafeGetFileName(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return string.Empty;

            filename = filename.TrimEnd(SeparatorCharacters);

            if (string.IsNullOrEmpty(filename))
                return string.Empty;

            var name_start = filename.LastIndexOfAny(SeparatorCharacters);
            if (name_start == -1)
                return filename;

            if (name_start == filename.Length - 1)
                return string.Empty;

            return filename.Substring(name_start + 1);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public Entry Source { get; private set; }

        public string Name { get; set; }

        public string Type
        {
            get { return Source.Type; }
            set
            {
                if (Source.Type != value)
                {
                    Source.Type = value;
                    OnPropertyChanged ("Type");
                }
            }
        }
        public uint?  Size { get { return IsDirectory ? null : (uint?)Source.Size; } }
        public int    Priority { get; private set; }
        public bool   IsDirectory { get { return Priority < 0; } }

        private void OnPropertyChanged (string property = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged (this, new PropertyChangedEventArgs (property));
            }
        }
    }

    public sealed class FileSystemComparer : IComparer
    {
        private string              m_property;
        private int                 m_direction;
        private static Comparer     s_default_comparer = new Comparer (CultureInfo.CurrentUICulture);

        public FileSystemComparer (string property, ListSortDirection direction)
        {
            m_property = property;
            m_direction = direction == ListSortDirection.Ascending ? 1 : -1;
        }

        public int Compare (object a, object b)
        {
            var v_a = a as EntryViewModel;
            var v_b = b as EntryViewModel;
            if (null == v_a || null == v_b)
                return s_default_comparer.Compare (a, b) * m_direction;

            if (v_a.Priority < v_b.Priority)
                return -1;
            if (v_a.Priority > v_b.Priority)
                return 1;
            if (string.IsNullOrEmpty (m_property))
                return 0;
            int order;
            if (m_property != "Name")
            {
                if ("Type" == m_property)
                {
                    // empty strings placed in the end
                    if (string.IsNullOrEmpty (v_a.Type))
                        order = string.IsNullOrEmpty (v_b.Type) ? 0 : m_direction;
                    else if (string.IsNullOrEmpty (v_b.Type))
                        order = -m_direction;
                    else
                        order = string.Compare (v_a.Type, v_b.Type, true) * m_direction;
                }
                else
                {
                    var prop_a = a.GetType ().GetProperty (m_property).GetValue (a);
                    var prop_b = b.GetType ().GetProperty (m_property).GetValue (b);
                    order = s_default_comparer.Compare (prop_a, prop_b) * m_direction;
                }
                if (0 == order)
                    order = CompareNames (v_a.Name, v_b.Name);
            }
            else
                order = CompareNames (v_a.Name, v_b.Name) * m_direction;
            return order;
        }

        static int CompareNames (string a, string b)
        {
            return NativeMethods.StrCmpLogicalW (a, b);
            //return NaturalStringComparer.Compare (a, b);
            //return string.Compare (a, b, StringComparison.CurrentCultureIgnoreCase);
        }

        /*private static class NaturalStringComparer
        {
            public static int Compare(string x, string y)
            {
                if (x == null && y == null) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                int lengthX = x.Length;
                int lengthY = y.Length;

                int indexX = 0;
                int indexY = 0;

                while (indexX < lengthX && indexY < lengthY)
                {
                    if (char.IsDigit(x[indexX]) && char.IsDigit(y[indexY]))
                    {
                        int numStartX = indexX;
                        int numStartY = indexY;

                        while (indexX < lengthX && x[indexX] == '0') indexX++;
                        while (indexY < lengthY && y[indexY] == '0') indexY++;

                        int numLengthX = 0;
                        int numLengthY = 0;

                        int tempX = indexX;
                        int tempY = indexY;

                        while (tempX < lengthX && char.IsDigit(x[tempX]))
                        {
                            numLengthX++;
                            tempX++;
                        }

                        while (tempY < lengthY && char.IsDigit(y[tempY]))
                        {
                            numLengthY++;
                            tempY++;
                        }

                        if (numLengthX != numLengthY)
                        {
                            return numLengthX.CompareTo(numLengthY);
                        }

                        while (indexX < tempX && indexY < tempY)
                        {
                            int diff = x[indexX].CompareTo(y[indexY]);
                            if (diff != 0) return diff;
                            indexX++;
                            indexY++;
                        }

                        if (indexX == tempX && indexY == tempY)
                        {
                            int zerosX = indexX - numStartX - numLengthX;
                            int zerosY = indexY - numStartY - numLengthY;
                            if (zerosX != zerosY)
                            {
                                return zerosY.CompareTo(zerosX);
                            }
                        }
                    }
                    else
                    {
                        int diff = char.ToUpperInvariant(x[indexX]).CompareTo(char.ToUpperInvariant(y[indexY]));
                        if (diff != 0) return diff;

                        indexX++;
                        indexY++;
                    }
                }

                return lengthX.CompareTo(lengthY);
            }
        }*/

        internal static class NativeMethods
        {
            [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
            public static extern int StrCmpLogicalW(string psz1, string psz2);
        }
    }

    /// <summary>
    /// Image format model for formats drop-down list widgets.
    /// </summary>
    public class ImageFormatModel
    {
        public ImageFormat Source { get; private set; }
        public string Tag {
            get { return null != Source ? Source.Tag : Localization._T("TextAsIs"); }
        }

        public ImageFormatModel (ImageFormat impl = null)
        {
            Source = impl;
        }
    }
}
