using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Win32;
using System.Collections.Specialized;

using GARbro.GUI.History;
using GARbro.GUI.Properties;
using GameRes;


namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private App m_app;
        public  App App { get { return m_app; } }

        public static readonly GuiResourceSetting DownScaleImage = new GuiResourceSetting ("winDownScaleImage");
        public static readonly GuiResourceSetting AutoCropTransparent = new GuiResourceSetting ("winAutoCropTransparent");

        const StringComparison StringIgnoreCase = StringComparison.CurrentCultureIgnoreCase;

        private bool _isShuttingDown = false;

        const int MaxRecentFiles = 9;
        private double _savedVolume = 0.8;
        const int DEFAULT_WIDTH = 1200;
        const int DEFAULT_HEIGHT = 600;

        public MainWindow()
        {
            m_app = Application.Current as App;

            InitializeComponent();

            if (this.Top < 0) this.Top = 0;
            if (this.Left < 0) this.Left = 0;

            InitializePreviewSystem();
            InitializeNavigation();
            InitializeMediaSystem();
            InitializeModelControls();

            FitWindowMenuItem.IsChecked = DownScaleImage.Get<bool>();
            AutoCropMenuItem.IsChecked = AutoCropTransparent.Get<bool>(); 

            if (null == Settings.Default.appRecentFiles)
                Settings.Default.appRecentFiles = new StringCollection();
            m_recent_files = new LinkedList<string>(Settings.Default.appRecentFiles.Cast<string>().Take (MaxRecentFiles));
            RecentFilesMenu.ItemsSource = RecentFiles;

            FormatCatalog.Instance.ParametersRequest += (s, e) => Dispatcher.Invoke (() => OnParametersRequest (s, e));

            CurrentDirectory.SizeChanged += (s, e) => {
                if (e.WidthChanged)
                {
                    pathLine.MinWidth = e.NewSize.Width - 79;
                    this.MinWidth = e.NewSize.Width + 79;
                }
            };

            DownScaleImage.PropertyChanged += (s, e) => {
                ApplyDownScaleSetting();
                FitWindowMenuItem.IsChecked = DownScaleImage.Get<bool>();
            };
            AutoCropTransparent.PropertyChanged += (s, e) => {
                AutoCropMenuItem.IsChecked = AutoCropTransparent.Get<bool>();
                RefreshPreviewPane();
            };
            pathLine.EnterKeyDown += acb_OnKeyDown;

            this.Closing += OnClosing;

            _savedVolume = Settings.Default.MediaVolume;
            if (_savedVolume < 0 || _savedVolume > 1)
                _savedVolume = 0.8;
        }

        void WindowLoaded (object sender, RoutedEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                ResetWindowPosition();
            lv_SetSortMode (Settings.Default.lvSortColumn, Settings.Default.lvSortDirection);

            Dispatcher.InvokeAsync(() => {
                LoadEncodingHistory();
                if (EncodingChoice.SelectedItem == null && EncodingChoice.Items.Count > 0)
                {
                    var utf8 = EncodingChoice.Items.Cast<Encoding>().FirstOrDefault(cpe => cpe.CodePage == 65001);
                    EncodingChoice.SelectedItem = utf8 ?? EncodingChoice.Items[0];
                }
            }, DispatcherPriority.Loaded);

            Dispatcher.InvokeAsync (WindowRendered, DispatcherPriority.ContextIdle);
            ImageData.SetDefaultDpi (Desktop.DpiX, Desktop.DpiY);
            if (SpriteLayoutCombo.Items.Count > 0)
                SpriteLayoutCombo.SelectedIndex = 0;
            MediaVolumeSlider.Value = _savedVolume;
        }

        void WindowRendered()
        {
            DirectoryViewModel vm = null;
            try
            {
                vm = GetNewViewModel (m_app.InitPath);
            }
            catch (Exception ex)
            {
                PopupError (ex.Message, Localization._T ("MsgErrorOpening"));
            }
            if (null == vm)
            {
                vm = CreateViewModel (Directory.GetCurrentDirectory(), true);
            }
            ViewModel = vm;
            lv_SelectItem (0);

            m_history.NavigateTo (GetCurrentPosition());

            if (!vm.IsArchive)
                SetFileStatus (Localization._T ("MsgReady"));
        }

        void WindowKeyDown (object sender, KeyEventArgs e)
        {
            if (MainMenuBar.Visibility != Visibility.Visible && Key.System == e.Key)
            {
                MainMenuBar.Visibility = Visibility.Visible;
                MainMenuBar.IsKeyboardFocusWithinChanged += HideMenuBar;
            }
        }

        void HideMenuBar (object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!MainMenuBar.IsKeyboardFocusWithin)
            {
                MainMenuBar.IsKeyboardFocusWithinChanged -= HideMenuBar;
                MainMenuBar.Visibility = Visibility.Collapsed;
            }
        }

        private void OnClosing (object sender, CancelEventArgs e)
        {
            _isShuttingDown = true;
            try
            {
                Settings.Default.MediaVolume = _savedVolume;
                CleanupMediaPlayback();
                SaveSettings();
                DisposeAllStreams();
                DisposePreviewHandlers();
            }
            catch (Exception ex)
            {
                Trace.WriteLine (ex.Message, "[OnClosing]");
                Trace.WriteLine (ex.StackTrace, "Stack trace");
            }
        }

        private void SaveSettings()
        {
            if (null != m_lvSortByColumn)
            {
                Settings.Default.lvSortColumn = SortMode;
                Settings.Default.lvSortDirection = m_lvSortDirection;
            }
            else
                Settings.Default.lvSortColumn = "";

            Settings.Default.appRecentFiles.Clear();
            foreach (var file in m_recent_files)
                Settings.Default.appRecentFiles.Add (file);

            string cwd = CurrentPath;
            if (!string.IsNullOrEmpty (cwd))
            {
                if (ViewModel.IsArchive)
                    cwd = Path.GetDirectoryName (cwd);
            }
            else
                cwd = Directory.GetCurrentDirectory();
            Settings.Default.appLastDirectory = cwd;

            if (WindowState == WindowState.Normal)
            {
                var screenLeft = SystemParameters.VirtualScreenLeft;
                var screenTop = SystemParameters.VirtualScreenTop;
                var screenWidth = SystemParameters.VirtualScreenWidth;
                var screenHeight = SystemParameters.VirtualScreenHeight;

                bool isOnScreen = Left < screenLeft + screenWidth &&
                    Top < screenTop + screenHeight &&
                    Left + Width > screenLeft &&
                    Top + Height > screenTop;

                if (isOnScreen)
                {
                    Settings.Default.winLeft = Left;
                    Settings.Default.winTop = Top;
                    Settings.Default.winWidth = Width;
                    Settings.Default.winHeight = Height;
                }
                else
                {
                    Settings.Default.winLeft = double.NaN;
                    Settings.Default.winTop = double.NaN;
                    Settings.Default.winWidth = (double)DEFAULT_WIDTH;
                    Settings.Default.winHeight = (double)DEFAULT_HEIGHT;
                }
            }

            SaveEncodingHistory();
        }

        /// <summary>
        /// Set preview status line text.<br/> 
        /// Could be called from any thread.
        /// </summary>
        public void SetFileStatus (string text)
        {
            Dispatcher.Invoke (() => { appFileStatus.Text = text.Trim(); });
        }

        /// <summary>
        /// Set directory listing status line text.<br/> 
        /// Could be called from any thread.
        /// </summary>
        public void SetPreviewStatus (string text)
        {
            Dispatcher.Invoke (() => { appPreviewStatus.Text = text.Trim(); });
        }

        /// <summary>
        /// Popup error message box.<br/> 
        /// Could be called from any thread.
        /// </summary>
        public void PopupError (string message, string title)
        {
            Dispatcher.Invoke (() => MessageBox.Show (this, message, title, MessageBoxButton.OK, MessageBoxImage.Error));
        }

        internal FileErrorDialogResult ShowErrorDialog (string error_title, string error_text, IntPtr parent_hwnd)
        {
            var dialog = new FileErrorDialog (error_title, error_text);
            SetModalWindowParent (dialog, parent_hwnd);
            return dialog.ShowDialog();
        }

        internal FileExistsDialogResult ShowFileExistsDialog (string title, string text, IntPtr parent_hwnd)
        {
            var dialog = new FileExistsDialog (title, text);
            SetModalWindowParent (dialog, parent_hwnd);
            return dialog.ShowDialog();
        }

        private void SetModalWindowParent (Window dialog, IntPtr parent_hwnd)
        {
            if (parent_hwnd != IntPtr.Zero)
            {
                var native_dialog = new WindowInteropHelper (dialog);
                native_dialog.Owner = parent_hwnd;
                NativeMethods.EnableWindow (parent_hwnd, false);
                EventHandler on_closed = null;
                on_closed = (s, e) => {
                    NativeMethods.EnableWindow (parent_hwnd, true);
                    dialog.Closed -= on_closed;
                };
                dialog.Closed += on_closed;
            }
            else
            {
                dialog.Owner = this;
            }
        }

        private bool m_busy_state = false;

        public void SetBusyState()
        {
            if (!Dispatcher.CheckAccess())
            {
                // We're on a background thread, marshal to UI thread
                Dispatcher.Invoke(() => SetBusyState());
                return;
            }

            m_busy_state = true;
            Mouse.OverrideCursor = Cursors.Wait;

            Dispatcher.InvokeAsync(() => {
                m_busy_state = false;
                Mouse.OverrideCursor = null;
            }, DispatcherPriority.ApplicationIdle);
        }

        private void ResetWindowPosition()
        {
            Settings.Default.winLeft = double.NaN;
            Settings.Default.winTop = double.NaN;
            Settings.Default.winWidth = DEFAULT_WIDTH;
            Settings.Default.winHeight = DEFAULT_HEIGHT;
            Settings.Default.winState = WindowState.Normal;

            WindowState = WindowState.Normal;
            Width = DEFAULT_WIDTH;
            Height = DEFAULT_HEIGHT;

            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            Left = (screenWidth - Width) / 2;
            Top = (screenHeight - Height) / 2;
        }

        private void OnParametersRequest (object sender, ParametersRequestEventArgs e)
        {
            var format = sender as IResource;
            if (null != format)
            {
                var control = format.GetAccessWidget() as UIElement;
                if (null != control)
                {
                    bool busy_state = m_busy_state;
                    var param_dialog = new ArcParametersDialog (control, e.Notice);
                    param_dialog.Owner = this;
                    e.InputResult = param_dialog.ShowDialog() ?? false;
                    if (e.InputResult)
                        e.Options = format.GetOptions (control);
                    if (busy_state)
                        SetBusyState();
                }
            }
        }

        static void ToggleVisibility (UIElement item)
        {
            item.Visibility = (Visibility.Visible == item.Visibility) ?
                Visibility.Collapsed :
                Visibility.Visible;
        }

        #region Command Handlers

        private void OpenFileExec (object control, ExecutedRoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                Title = Localization._T ("TextChooseArchive"),
            };
            if (!dlg.ShowDialog (this).Value)
                return;
            OpenFile (dlg.FileName);
        }

        private void OpenRecentExec (object control, ExecutedRoutedEventArgs e)
        {
            string filename = e.Parameter as string;
            if (string.IsNullOrEmpty (filename))
                return;
            OpenFile (filename);
        }

        private void OpenItemExec (object control, ExecutedRoutedEventArgs e)
        {
            SetBusyState();
            EntryViewModel entry = null;
            var lvi = e.OriginalSource as ListViewItem;
            if (lvi != null)
                entry = lvi.Content as EntryViewModel;
            if (null == entry)
                entry = CurrentDirectory.SelectedItem as EntryViewModel;
            if (null == entry)
                return;

            OpenEntry (entry);
        }

        private void DescendExec (object control, ExecutedRoutedEventArgs e)
        {
            var entry = CurrentDirectory.SelectedItem as EntryViewModel;
            if (entry != null)
                OpenDirectoryEntry (ViewModel, entry);
        }

        private void AscendExec (object control, ExecutedRoutedEventArgs e)
        {
            var vm = ViewModel;
            var parent_dir = vm.FirstOrDefault (entry => entry.Name == VFS.DIR_PARENT);
            if (parent_dir != null)
                OpenDirectoryEntry (vm, parent_dir);
        }

        private void GoBackExec (object sender, ExecutedRoutedEventArgs e)
        {
            NavigateBack();
        }

        private void GoForwardExec (object sender, ExecutedRoutedEventArgs e)
        {
            NavigateForward();
        }

        private void RefreshExec (object sender, ExecutedRoutedEventArgs e)
        {
            RefreshView();
        }

        private void DeleteItemExec (object sender, ExecutedRoutedEventArgs e)
        {
            DeleteSelectedItems();
        }

        private void ExploreItemExec (object sender, ExecutedRoutedEventArgs e)
        {
            var entry = CurrentDirectory.SelectedItem as EntryViewModel;
            if (entry != null && !ViewModel.IsArchive)
            {
                try
                {
                    string name = Path.Combine (CurrentPath, entry.Name);
                    Process.Start ("explorer.exe", "/select," + name);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine (ex.Message, "explorer.exe");
                }
            }
        }

        private void AddSelectionExec (object sender, ExecutedRoutedEventArgs e)
        {
            SelectByMask();
        }

        private void SelectAllExec (object sender, ExecutedRoutedEventArgs e)
        {
            CurrentDirectory.SelectAll();
        }

        private void SetFileTypeExec (object sender, ExecutedRoutedEventArgs e)
        {
            var selected = CurrentDirectory.SelectedItems.Cast<EntryViewModel>().Where (x => !x.IsDirectory);
            if (!selected.Any())
                return;
            string type = e.Parameter as string;
            foreach (var entry in selected)
            {
                entry.Type = type;
            }
        }

        private void CopyNamesExec (object sender, ExecutedRoutedEventArgs e)
        {
            var names = CurrentDirectory.SelectedItems.Cast<EntryViewModel>().Select (f => f.Name);
            if (names.Any())
            {
                try
                {
                    Clipboard.SetText (string.Join ("\r\n", names));
                }
                catch (Exception ex)
                {
                    Trace.WriteLine (ex.Message, "Clipboard Error");
                }
            }
        }

        private void NextItemExec (object sender, ExecutedRoutedEventArgs e)
        {
            if (_isFilterActive)
                return;

            var index = CurrentDirectory.SelectedIndex + 1;
            if (index < CurrentDirectory.Items.Count)
            {
                CurrentDirectory.SelectedIndex = index;
                CurrentDirectory.ScrollIntoView (CurrentDirectory.SelectedItem);
            }
        }

        private void SortByExec (object sender, ExecutedRoutedEventArgs e)
        {
            string sort_by = e.Parameter as string;
            lv_Sort (sort_by, ListSortDirection.Ascending);
            lv_SetSortMode (sort_by, ListSortDirection.Ascending);
        }

        private void ScaleImageExec (object sender, ExecutedRoutedEventArgs e)
        {
            DownScaleImage.Value = !DownScaleImage.Get<bool>();
        }

        private void FitWindowExec (object sender, ExecutedRoutedEventArgs e)
        {
            DownScaleImage.Value = !DownScaleImage.Get<bool>();
        }

        private void AutoCropImageExec (object sender, ExecutedRoutedEventArgs e)
        {
            AutoCropTransparent.Value = !AutoCropTransparent.Get<bool>();
        }

        private void HideStatusBarExec (object sender, ExecutedRoutedEventArgs e)
        {
            ToggleVisibility (AppStatusBar);
        }

        private void HideMenuBarExec (object sender, ExecutedRoutedEventArgs e)
        {
            ToggleVisibility (MainMenuBar);
        }

        private void HideToolBarExec (object sender, ExecutedRoutedEventArgs e)
        {
            ToggleVisibility (MainToolBar);
        }

        private void PreferencesExec (object sender, ExecutedRoutedEventArgs e)
        {
            var settings = new SettingsWindow();
            settings.Owner = this;
            settings.ShowDialog();
        }

        private void TroubleShootingExec (object sender, ExecutedRoutedEventArgs e)
        {
            var dialog = new TroubleShootingDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void AboutExec (object sender, ExecutedRoutedEventArgs e)
        {
            var about = new AboutBox();
            about.Owner = this;
            about.ShowDialog();
        }

        private void ResetWindowPositionExec (object sender, ExecutedRoutedEventArgs e)
        {
            ResetWindowPosition();
        }

        private void ExitExec (object sender, ExecutedRoutedEventArgs e)
        {
            CleanupMediaPlayback();
            Application.Current.Shutdown();
        }

        #endregion

        #region Can Execute Handlers

        private void CanExecuteAlways (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void CanExecuteControlCommand (object sender, CanExecuteRoutedEventArgs e)
        {
            Control target = e.Source as Control;
            e.CanExecute = target != null;
        }

        private void CanExecuteOnSelected (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = CurrentDirectory.SelectedIndex != -1;
        }

        private void CanExecuteGoBack (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = m_history.CanGoBack();
        }

        private void CanExecuteGoForward (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = m_history.CanGoForward();
        }

        private void CanExecuteInArchive (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ViewModel.IsArchive && CurrentDirectory.SelectedIndex != -1;
        }

        private void CanExecuteCreateArchive (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !ViewModel.IsArchive && CurrentDirectory.SelectedItems.Count > 0;
        }

        private void CanExecuteInDirectory(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !ViewModel.IsArchive && CurrentDirectory.SelectedItems.Count > 0;
        }

        private void CanExecuteExtract (object sender, CanExecuteRoutedEventArgs e)
        {
            if (ViewModel.IsArchive)
            {
                e.CanExecute = true;
                return;
            }
            else if (CurrentDirectory.SelectedIndex != -1)
            {
                var entry = CurrentDirectory.SelectedItem as EntryViewModel;
                if (entry != null && !entry.IsDirectory)
                {
                    e.CanExecute = true;
                    return;
                }
            }
            e.CanExecute = false;
        }

        private void CanExecuteOnPhysicalFile (object sender, CanExecuteRoutedEventArgs e)
        {
            if (!ViewModel.IsArchive && CurrentDirectory.SelectedIndex != -1)
            {
                var entry = CurrentDirectory.SelectedItem as EntryViewModel;
                if (entry != null && !entry.IsDirectory)
                {
                    e.CanExecute = true;
                    return;
                }
            }
            e.CanExecute = false;
        }

        private void CanExecuteConvertMedia (object sender, CanExecuteRoutedEventArgs e)
        {
            if (CurrentDirectory.SelectedItems.Count >= 1)
            {
                e.CanExecute = !ViewModel.IsArchive;
            }
        }

        private void CanExecuteOnImage (object sender, CanExecuteRoutedEventArgs e)
        {
            var entry = CurrentDirectory.SelectedItem as EntryViewModel;
            e.CanExecute = !ViewModel.IsArchive && entry != null && entry.Type == "image";
        }

        private void CanExecuteScaleImage (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ImageCanvas.Source != null;
        }

        private void CanExecuteFitWindow (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ImageCanvas.Source != null;
        }

        private void CanExecuteAutoCropImage (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ImageCanvas.Source != null && SpriteLayoutCombo?.SelectedIndex == 0;
        }

        private void CanExecutePlaybackControl (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = _previewStateMachine.CurrentMediaType != MediaType.None;
        }

        #endregion

        private void OnDropEvent (object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent (DataFormats.FileDrop))
                    return;
                var files = (string[])e.Data.GetData (DataFormats.FileDrop);
                if (!files.Any())
                    return;
                var filename = files.First();
                try
                {
                    OpenFileOrDir (filename);
                }
                catch (Exception ex)
                {
                    VFS.FullPath = new string[] { Path.GetDirectoryName (filename) };
                    var vm = new DirectoryViewModel (VFS.FullPath, VFS.GetFiles(), VFS.IsVirtual);
                    PushViewModel (vm);
                    filename = Path.GetFileName (filename);
                    lv_SelectItem (filename);
                    SetFileStatus (string.Format ("{0}: {1}", filename, ex.Message));
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine (ex.Message, "Drop event failed");
            }
        }
    }

    #region Commands bindings

    public static class Commands
    {
        public static readonly RoutedCommand OpenItem = new RoutedCommand();
        public static readonly RoutedCommand OpenFile = new RoutedCommand();
        public static readonly RoutedCommand OpenRecent = new RoutedCommand();
        public static readonly RoutedCommand ExtractItem = new RoutedCommand();
        public static readonly RoutedCommand CreateArchive = new RoutedCommand();
        public static readonly RoutedCommand SortBy = new RoutedCommand();
        public static readonly RoutedCommand Exit = new RoutedCommand();
        public static readonly RoutedCommand About = new RoutedCommand();
        public static readonly RoutedCommand CheckUpdates = new RoutedCommand();
        public static readonly RoutedCommand GoBack = new RoutedCommand();
        public static readonly RoutedCommand GoForward = new RoutedCommand();
        public static readonly RoutedCommand DeleteItem = new RoutedCommand();
        public static readonly RoutedCommand RenameItem = new RoutedCommand();
        public static readonly RoutedCommand ExploreItem = new RoutedCommand();
        public static readonly RoutedCommand ConvertMedia = new RoutedCommand();
        public static readonly RoutedCommand Refresh = new RoutedCommand();
        public static readonly RoutedCommand Browse = new RoutedCommand();
        public static readonly RoutedCommand FitWindow = new RoutedCommand();
        public static readonly RoutedCommand HideStatusBar = new RoutedCommand();
        public static readonly RoutedCommand HideMenuBar = new RoutedCommand();
        public static readonly RoutedCommand HideToolBar = new RoutedCommand();
        public static readonly RoutedCommand AddSelection = new RoutedCommand();
        public static readonly RoutedCommand SelectAll = new RoutedCommand();
        public static readonly RoutedCommand SetFileType = new RoutedCommand();
        public static readonly RoutedCommand NextItem = new RoutedCommand();
        public static readonly RoutedCommand CopyNames = new RoutedCommand();
        public static readonly RoutedCommand StopPlayback = new RoutedCommand();
        public static readonly RoutedCommand PausePlayback = new RoutedCommand();
        public static readonly RoutedCommand CyclePlayback = new RoutedCommand();
        public static readonly RoutedCommand AutoPlayback = new RoutedCommand();
        public static readonly RoutedCommand Preferences = new RoutedCommand();
        public static readonly RoutedCommand TroubleShooting = new RoutedCommand();
        public static readonly RoutedCommand Descend = new RoutedCommand();
        public static readonly RoutedCommand Ascend = new RoutedCommand();
        public static readonly RoutedCommand ScaleImage = new RoutedCommand();
        public static readonly RoutedCommand ResetWindowPosition = new RoutedCommand();
        public static readonly RoutedCommand AutoCropImage = new RoutedCommand();
    }

    #endregion
}