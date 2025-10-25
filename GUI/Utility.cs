using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace GARbro
{
    #region  Native Methods
    internal static partial class NativeMethods
    {
        public static readonly bool IsWindowsVistaOrLater = Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version >= new Version (6, 0, 6000);

        [DllImport ("gdi32.dll")]
        internal static extern int GetDeviceCaps (IntPtr hDc, int nIndex);

        [DllImport ("user32.dll")]
        internal static extern IntPtr GetDC (IntPtr hWnd);

        [DllImport ("user32.dll")]
        internal static extern int ReleaseDC (IntPtr hWnd, IntPtr hDc);

        [DllImport ("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern IntPtr GetActiveWindow();

        [DllImport ("user32.dll")][return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow (IntPtr hWnd, int nCmdShow);

        [DllImport ("user32.dll")][return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnableWindow (IntPtr hWnd, bool bEnable);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int StrCmpLogicalW (string psz1, string psz2);

        private static class NaturalStringComparer
        {
            public static int Compare (string x, string y)
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
                    if (char.IsDigit (x[indexX]) && char.IsDigit (y[indexY]))
                    {
                        int numStartX = indexX;
                        int numStartY = indexY;

                        while (indexX < lengthX && x[indexX] == '0') indexX++;
                        while (indexY < lengthY && y[indexY] == '0') indexY++;

                        int numLengthX = 0;
                        int numLengthY = 0;

                        int tempX = indexX;
                        int tempY = indexY;

                        while (tempX < lengthX && char.IsDigit (x[tempX]))
                        {
                            numLengthX++;
                            tempX++;
                        }

                        while (tempY < lengthY && char.IsDigit (y[tempY]))
                        {
                            numLengthY++;
                            tempY++;
                        }

                        if (numLengthX != numLengthY)
                            return numLengthX.CompareTo (numLengthY);

                        while (indexX < tempX && indexY < tempY)
                        {
                            int diff = x[indexX].CompareTo (y[indexY]);
                            if (diff != 0) return diff;
                            indexX++;
                            indexY++;
                        }

                        if (indexX == tempX && indexY == tempY)
                        {
                            int zerosX = indexX - numStartX - numLengthX;
                            int zerosY = indexY - numStartY - numLengthY;
                            if (zerosX != zerosY)
                                return zerosY.CompareTo (zerosX);
                        }
                    }
                    else
                    {
                        int diff = char.ToUpperInvariant (x[indexX]).CompareTo (char.ToUpperInvariant (y[indexY]));
                        if (diff != 0) return diff;

                        indexX++;
                        indexY++;
                    }
                }

                return lengthX.CompareTo (lengthY);
            }
        }

        static bool _compareErrShown = false;

        public static int SafeCompareNatural (string str1, string str2)
        {
            if (!IsWindowsVistaOrLater || _compareErrShown)
                return NaturalStringComparer.Compare (str1, str2);

            if (str1 == null || str2 == null)
                return string.Compare (str1, str2, StringComparison.OrdinalIgnoreCase);

            try
            {
                return StrCmpLogicalW (str1, str2);
            }
            catch
            {
                if (!_compareErrShown) {
                    System.Diagnostics.Trace.WriteLine ("StrCmpLogicalW failed, reverting to NaturalStringComparer");
                    _compareErrShown = true;
                }
                return NaturalStringComparer.Compare (str1, str2);
            }
        }
    }

    public static class Desktop
    {
        public static int DpiX { get { return dpi_x; } }
        public static int DpiY { get { return dpi_y; } }

        public const int LOGPIXELSX = 88;
        public const int LOGPIXELSY = 90;

        private static int dpi_x = GetCaps (LOGPIXELSX);
        private static int dpi_y = GetCaps (LOGPIXELSY);

        public static int GetCaps (int cap)
        {
            IntPtr hdc = NativeMethods.GetDC (IntPtr.Zero);
            if (hdc == IntPtr.Zero)
                return 96;
            int dpi = NativeMethods.GetDeviceCaps (hdc, cap);
            NativeMethods.ReleaseDC (IntPtr.Zero, hdc);
            return dpi;
        }
    }

    public sealed class NumericStringComparer : IComparer<string>
    {
        public int Compare (string a, string b)
        {
            return NativeMethods.StrCmpLogicalW (a, b);
        }
    }

    public class WaitCursor : IDisposable
    {
        private Cursor m_previousCursor;

        public WaitCursor()
        {
            m_previousCursor = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
        }

        #region IDisposable Members
        bool disposed = false;
        public void Dispose()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!disposed)
            {
                Mouse.OverrideCursor = m_previousCursor;
                disposed = true;
            }
        }
        #endregion
    }
    #endregion
}
