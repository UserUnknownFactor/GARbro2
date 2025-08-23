using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;

using GameRes;

namespace GARbro.GUI
{
    /// <summary>
    /// Represents a file being previewed.
    /// </summary>
    public class PreviewFile : IDisposable
    {
        public IEnumerable<string> Path { get; set; }
        public string              Name { get; set; }
        public Entry              Entry { get; set; }
        public string          TempFile { get; set; }

        public bool IsEqual (IEnumerable<string> path, Entry entry)
        {
            return Path != null && entry != null && path != null &&
                path.SequenceEqual (Path) && Entry == entry;
        }

        public bool IsRealFile
        {
            get { return string.IsNullOrEmpty (TempFile); }
        }

        public string GetDisplayName()
        {
            return Name ?? System.IO.Path.GetFileName (TempFile) ?? Localization._T ("Unknown");
        }

        public void Dispose()
        {
            if (string.IsNullOrEmpty (TempFile)) return;
            if (File.Exists (TempFile))
            {
                try
                {
                    File.Delete (TempFile);
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// Comparer for DirectoryPosition that prevents duplicates
    /// </summary>
    public class DirectoryPositionComparer : IEqualityComparer<DirectoryPosition>
    {
        public bool Equals (DirectoryPosition x, DirectoryPosition y)
        {
            if (x == null || y == null)
                return x == y;

            // Same path = same position (ignore selection and scroll)
            return x.Path.SequenceEqual (y.Path);
        }

        public int GetHashCode (DirectoryPosition obj)
        {
            if (obj == null) return 0;

            int hash = 17;
            foreach (var p in obj.Path)
                hash = hash * 31 + (p?.GetHashCode() ?? 0);
            return hash;
        }
    }

    /// <summary>
    /// Stores current position within directory view model.
    /// </summary>
    public class DirectoryPosition
    {
        public IEnumerable<string> Path { get; set; }
        public string Item { get; set; }
        public double ScrollOffset { get; set; }
    
        public DirectoryPosition (DirectoryViewModel vm, EntryViewModel item, double scrollOffset = 0)
        {
            Path = vm.Path.ToArray();
            Item = null != item ? item.Name : null;
            ScrollOffset = scrollOffset;
        }
    
        public DirectoryPosition (string filename)
        {
            Path = new string[] { System.IO.Path.GetDirectoryName (filename) };
            Item = System.IO.Path.GetFileName (filename);
            ScrollOffset = 0;
        }
    }

    public class BooleanToCollapsedVisibilityConverter : IValueConverter
    {
        #region IValueConverter Members

        public object Convert (object value, Type targetType, object parameter, CultureInfo culture)
        {
            //reverse conversion (false=>Visible, true=>collapsed) on any given parameter
            bool input = (null == parameter) ? (bool)value : !((bool)value);
            return (input) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        #endregion
    }

    public class SortModeToBooleanConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object parameter, CultureInfo culture)
        {
            string actual_mode = value as string;
            string check_mode = parameter as string;
            if (string.IsNullOrEmpty (check_mode))
                return string.IsNullOrEmpty (actual_mode);
            return check_mode.Equals (actual_mode);
        }

        public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EntryTypeConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object parameter, CultureInfo culture)
        {
            var type = value as string;
            if (!string.IsNullOrEmpty (type))
            {
                var translation = Localization._T ($"Type_{type}");
                if (!string.IsNullOrEmpty (translation))
                    return translation;
            }
            return value;
        }

        public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public static class FileOperationHelper
    {
        [StructLayout (LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOCONFIRMATION = 0x0010;

        [DllImport ("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHFileOperation (ref SHFILEOPSTRUCT FileOp);

        public static void DeleteToRecycleBin (string path, bool showDialog = true)
        {
            var fileOp = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + '\0' + '\0',
                fFlags = FOF_ALLOWUNDO
            };

            if (!showDialog)
            {
                fileOp.fFlags |= FOF_NOCONFIRMATION;
            }
            SHFileOperation (ref fileOp);
        }
    }
}