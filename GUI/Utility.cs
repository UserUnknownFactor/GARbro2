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
    internal class NativeMethods
    {
        public static bool IsWindowsVistaOrLater
        {
            get
            {
                return Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version >= new Version (6, 0, 6000);
            }
        }

        [DllImport ("shlwapi.dll", CharSet = CharSet.Unicode)]
        internal static extern int StrCmpLogicalW (string psz1, string psz2);

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
