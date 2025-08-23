using System;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Rnd.Windows
{
    /// <summary>
    /// Window without an icon.
    /// </summary>
    public class ModalWindow : Window
    {
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            HideIcon (this);
        }

        internal class NativeMethods
        {
            [DllImport ("user32.dll")]
            internal static extern int GetWindowLong (IntPtr hwnd, int index);

            [DllImport ("user32.dll")]
            internal static extern int SetWindowLong (IntPtr hwnd, int index, int newStyle);

            [DllImport ("user32.dll")]
            internal static extern bool SetWindowPos (IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int width, int height, uint flags);

            [DllImport ("user32.dll")]
            internal static extern IntPtr SendMessage (IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        }

        const int GWL_EXSTYLE = -20;
        const int WS_EX_DLGMODALFRAME = 0x0001;

        const int SWP_NOSIZE = 0x0001;
        const int SWP_NOMOVE = 0x0002;
        const int SWP_NOZORDER = 0x0004;
        const int SWP_FRAMECHANGED = 0x0020;
        const uint WM_SETICON = 0x0080;

        /// <summary>
        /// Win32 mumbo-jumbo to hide window icon and its menu.
        /// </summary>

        public static void HideIcon (Window window)
        {
            // Get this window's handle
            IntPtr hwnd = new WindowInteropHelper (window).Handle;

            // Change the extended window style to not show a window icon
            int extendedStyle = NativeMethods.GetWindowLong (hwnd, GWL_EXSTYLE);
            NativeMethods.SetWindowLong (hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_DLGMODALFRAME);
            NativeMethods.SendMessage (hwnd, WM_SETICON, IntPtr.Zero, IntPtr.Zero);
            NativeMethods.SendMessage (hwnd, WM_SETICON, new IntPtr (1), IntPtr.Zero);

            // Update the window's non-client area to reflect the changes
            NativeMethods.SetWindowPos (hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }
    }
}
