using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Diagnostics;

namespace GARbro.GUI
{
    public partial class MainWindow
    {
        private System.Windows.Forms.NotifyIcon _trayIcon;
        private bool _isMinimizedToTray = false;
        private IntPtr _hookID = IntPtr.Zero;
        private NativeMethods.LowLevelKeyboardProc _hookProc;

        private void InitializeBossKey()
        {
            InitializeTrayIcon();
            SetupBossKeyHook();
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();

            _trayIcon.Icon = CreateMonochromeIcon();

            _trayIcon.Text = "Information";
            _trayIcon.Visible = false;

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Show", null, (s, e) => RestoreFromTray());
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, (s, e) => 
            {
                _isShuttingDown = true;
                Application.Current.Shutdown();
            });

            _trayIcon.ContextMenuStrip = contextMenu;

            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                    RestoreFromTray();
            };
        }

        private System.Drawing.Icon CreateMonochromeIcon()
        {
            int size = 16;
            var bitmap = new System.Drawing.Bitmap(size, size);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);

                // Simple square (like a window/app icon)
                // g.DrawRectangle(pen, 3, 3, 9, 9);

                // Circle (like notifications/info)
                // g.DrawEllipse(pen, 3, 3, 9, 9);

                // Three horizontal lines (like menu/settings)
                g.FillRectangle(brush, 3, 3, 10, 2);
                g.FillRectangle(brush, 3, 7, 10, 2);
                g.FillRectangle(brush, 3, 11, 10, 2);

                // Bell icon
                // var points = new System.Drawing.Point[]
                // {
                //     new System.Drawing.Point(8, 3),
                //     new System.Drawing.Point(5, 9),
                //     new System.Drawing.Point(11, 9)
                // };
                // g.DrawPolygon(pen, points);
                // g.DrawLine(pen, 6, 10, 10, 10);

                brush.Dispose();
            }

            IntPtr hIcon = bitmap.GetHicon();
            var icon = System.Drawing.Icon.FromHandle(hIcon);
            return icon;
        }

        private void SetupBossKeyHook()
        {
            _hookProc = HookCallback;
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                _hookID = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_KEYBOARD_LL,
                    _hookProc,
                    NativeMethods.GetModuleHandle(curModule.ModuleName),
                    0);
            }

            if (_hookID == IntPtr.Zero)
            {
                Trace.WriteLine("Failed to install boss key hook");
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                // Right Ctrl or F12 used as boss key
                if ((vkCode == NativeMethods.VK_RCONTROL || vkCode == NativeMethods.VK_F12) 
                    && !_isMinimizedToTray)
                {
                    Dispatcher.BeginInvoke(new Action(MinimizeToTray));
                }
            }

            return NativeMethods.CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void MinimizeToTray()
        {
            if (_isMinimizedToTray || _trayIcon == null)
                return;

            _isMinimizedToTray = true;
            this.Hide();
            _trayIcon.Visible = true;
        }

        private void RestoreFromTray()
        {
            if (!_isMinimizedToTray || _trayIcon == null)
                return;

            _isMinimizedToTray = false;
            _trayIcon.Visible = false;
            this.Show();

            if (this.WindowState == WindowState.Minimized)
                this.WindowState = WindowState.Normal;

            this.Activate();
        }

        private void CleanupBossKey()
        {
            if (_hookID != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }
    }
}

namespace GARbro
{
    internal static partial class NativeMethods
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        public const int VK_RCONTROL = 0xA3;
        public const int VK_F12 = 0x7B;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, 
            IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, 
            IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}