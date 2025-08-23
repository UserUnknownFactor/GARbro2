using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

using GameRes;
using GARbro.GUI.History;

namespace GARbro.GUI
{
    public partial class MainWindow
    {
        private FileSystemWatcher m_watcher = new FileSystemWatcher();
        private NavigationHistory<DirectoryPosition> m_history = new NavigationHistory<DirectoryPosition>(50, new DirectoryPositionComparer());
        private EntryViewModel m_last_selected = null;

        // Filtering
        private string _filterText = string.Empty;
        private bool _isFilterActive = false;

        // Sorting
        private GridViewColumnHeader m_lvSortByColumn = null;
        private ListSortDirection m_lvSortDirection = ListSortDirection.Ascending;

        public string SortMode
        {
            get { return GetValue (SortModeProperty) as string; }
            private set { SetValue (SortModeProperty, value); }
        }

        public static readonly DependencyProperty SortModeProperty =
            DependencyProperty.RegisterAttached ("SortMode", typeof (string), typeof (MainWindow), new UIPropertyMetadata());

        internal string CurrentPath { get { return ViewModel.Path.First(); } }

        public DirectoryViewModel ViewModel
        {
            get
            {
                var source = CurrentDirectory.ItemsSource as CollectionView;
                if (null == source)
                    return null;
                return source.SourceCollection as DirectoryViewModel;
            }
            private set
            {
                StopWatchDirectoryChanges();
                ClearFilter();

                var cvs = this.Resources["ListViewSource"] as CollectionViewSource;
                cvs.Source = value;

                UpdatePathDisplay (value);

                if (value.IsArchive && value.Path.Count <= 2)
                    PushRecentFile (value.Path.First());

                lv_Sort (SortMode, m_lvSortDirection);
                if (!value.IsArchive && !string.IsNullOrEmpty (value.Path.First()))
                {
                    WatchDirectoryChanges (value.Path.First());
                }
                CurrentDirectory.UpdateLayout();
            }
        }

        private void InitializeNavigation ()
        {
            InitDirectoryChangesWatcher();
        }

        private void InitDirectoryChangesWatcher ()
        {
            m_watcher.NotifyFilter = NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.DirectoryName;
            m_watcher.Changed += InvokeRefreshView;
            m_watcher.Created += InvokeRefreshView;
            m_watcher.Deleted += InvokeRefreshView;
            m_watcher.Renamed += InvokeRefreshView;
        }

        private void UpdatePathDisplay (DirectoryViewModel value)
        {
            bool showPhysical = true;
            if (value.IsArchive && value.Path.Count > 1)
            {
                var archivePath = value.Path.Skip (1).Where (p => !string.IsNullOrEmpty (p));
                pathLine.Text = string.Join (VFS.DIR_DELIMITER, archivePath);
                if (!string.IsNullOrEmpty (pathLine.Text))
                    showPhysical = false;
            }
            if (showPhysical)
            {
                var path_component = value.Path.Last();
                if (string.IsNullOrEmpty (path_component) && value.Path.Count > 1)
                    path_component = value.Path[value.Path.Count - 2];
                pathLine.Text = path_component;
            }
        }

        private void OpenFile (string filename)
        {
            try
            {
                OpenFileOrDir (filename);
            }
            catch (OperationCanceledException ex)
            {
                SetFileStatus (ex.Message);
            }
            catch (Exception ex)
            {
                PopupError (string.Format ("{0}\n{1}", filename, ex.Message), Localization._T ("MsgErrorOpening"));
            }
        }

        private void OpenFileOrDir (string filename)
        {
            if (filename == CurrentPath || string.IsNullOrEmpty (filename))
                return;
            if (File.Exists (filename))
                VFS.FullPath = new string[] { filename, "" };
            else
                VFS.FullPath = new string[] { filename };
            var vm = new DirectoryViewModel (VFS.FullPath, VFS.GetFiles(), VFS.IsVirtual);
            PushViewModel (vm);
            ShowCurrentArchiveStatus();
            lv_SelectItem (0);
        }

        private void OpenEntry (EntryViewModel entry)
        {
            switch (entry.Type)
            {
            case "audio":
                SetFileStatus(Localization.Format("LoadingFile", Localization._T($"Type_{entry.Type}")));
                _previewStateMachine.StartAudioPlayback (entry.Source);
                return;
            case "video":
                SetFileStatus (Localization.Format ("LoadingFile", Localization._T ($"Type_{entry.Type}")));
                _previewStateMachine.StartVideoPlayback (entry.Source);
                return;
            case "text":
            case "script":
            case "image":
                return; // don't try to open those as archive
            default:
                OpenDirectoryEntry (ViewModel, entry);
                break;
            }
        }

        private void OpenDirectoryEntry (DirectoryViewModel vm, EntryViewModel entry)
        {
            string old_dir = vm?.Path.Last() ?? "";
            string new_dir = entry.Source.Name;
            bool isGoingUp = (VFS.DIR_PARENT == new_dir);

            if (isGoingUp && VFS.CurrentArchive != null)
            {
                if (string.IsNullOrEmpty(vm?.Path.Last()) && vm.Path.Count > 1)
                {
                    old_dir = vm.Path[vm.Path.Count - 2];
                }
            }

            var currentPos = GetCurrentPosition();

            Trace.WriteLine (new_dir, "OpenDirectoryEntry");
            int old_fs_count = VFS.Count;

            // Special handling for ".." in archives
            if (isGoingUp && VFS.IsVirtual)
            {
                try
                {
                    // Just change directory without trying to recreate the full path
                    VFS.ChDir (entry.Source);
                    vm = new DirectoryViewModel (VFS.FullPath, VFS.GetFiles(), VFS.IsVirtual);
                }
                catch (Exception ex)
                {
                    SetFileStatus (string.Format ("{0}: {1}", Path.GetFileName (new_dir), ex.Message));
                    return;
                }
            }
            else
            {
                // Normal navigation - this can use TryCreateViewModel
                vm = TryCreateViewModel (new_dir);
                if (null == vm)
                {
                    if (VFS.Count == old_fs_count)
                        return;

                    vm = new DirectoryViewModel (VFS.FullPath, VFS.GetFiles(), VFS.IsVirtual);
                    if (vm.Count == 0 && VFS.Count > 1)
                    {
                        VFS.Flush();
                        vm = new DirectoryViewModel (VFS.FullPath, VFS.GetFiles(), VFS.IsVirtual);
                    }
                }
            }

            // Check if this directory exists in history
            var searchPos = new DirectoryPosition (vm, null, 0);
            int existingIndex = m_history.FindState (searchPos);

            if (isGoingUp && existingIndex >= 0 && existingIndex < m_history.CurrentIndex)
            {
                m_history.NavigateTo (currentPos);
                var historyState = m_history.NavigateToIndex (existingIndex);

                ViewModel = vm;

                if (VFS.Count > old_fs_count && null != VFS.CurrentArchive)
                    ShowCurrentArchiveStatus();
                else
                    SetFileStatus ("");

                var itemName = VFS.GetFileName (old_dir);
                RestoreNavigationPosition (historyState, itemName);
                return;
            }

            // Normal navigation
            m_history.NavigateTo (currentPos);

            ViewModel = vm;

            if (VFS.Count > old_fs_count && null != VFS.CurrentArchive)
                ShowCurrentArchiveStatus();
            else
                SetFileStatus ("");

            HandleNavigationPosition (isGoingUp, old_dir);

            var newPos = GetCurrentPosition();
            m_history.NavigateTo (newPos);
        }

        private void HandleNavigationPosition (bool isGoingUp, string old_dir)
        {
            if (isGoingUp)
            {
                var itemName = VFS.GetFileName (old_dir);

                if (!string.IsNullOrEmpty (itemName))
                {
                    // Check if we have history for this directory
                    var historyPos = m_history.Current;
                    if (historyPos != null && historyPos.Path.SequenceEqual (ViewModel.Path))
                    {
                        // Restore from history (like Back button would)
                        RestoreNavigationPosition (historyPos, itemName);
                    }
                    else
                    {
                        // No history - just select and ensure visible
                        SelectAndFocusItem (itemName, true);
                    }
                }
            }
            else
            {
                lv_SelectItem (0);
            }
        }

        private void RestoreNavigationPosition (DirectoryPosition historyPos, string preferredItem = null)
        {
            Dispatcher.BeginInvoke (new Action (() =>
            {
                // Restore scroll position first
                var scrollViewer = GetScrollViewer (CurrentDirectory);
                if (scrollViewer != null && historyPos.ScrollOffset > 0)
                    scrollViewer.ScrollToVerticalOffset (historyPos.ScrollOffset);

                // Select the preferred item or the one from history
                var itemToSelect = preferredItem ?? historyPos.Item;
                if (!string.IsNullOrEmpty (itemToSelect))
                    lv_SelectItem (itemToSelect, false); // Don't auto-scroll since we restored position
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void SelectAndFocusItem (string itemName, bool scrollIntoView)
        {
            Dispatcher.BeginInvoke (new Action (() =>
            {
                var item = ViewModel.Find (itemName);
                if (item != null)
                {
                    CurrentDirectory.SelectedItem = item;
                    if (scrollIntoView)
                        CurrentDirectory.ScrollIntoView (item);
                    var lvi = (ListViewItem)CurrentDirectory.ItemContainerGenerator.ContainerFromItem (item);
                    lvi?.Focus();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ShowCurrentArchiveStatus ()
        {
            if (null == VFS.CurrentArchive)
                return;

            SetFileStatus (VFS.CurrentArchive.Description);
            string comment = "";
            if (!string.IsNullOrEmpty (VFS.CurrentArchive.Comment))
                comment = VFS.CurrentArchive.Comment + ": ";
            comment += VFS.CurrentArchive.Dir.Count().Pluralize ("MsgFiles");
            SetPreviewStatus (comment);
        }

        private void PushViewModel (DirectoryViewModel vm)
        {
            m_history.NavigateTo (GetCurrentPosition());

            var scrollViewer = GetScrollViewer (CurrentDirectory);
            var preserveScroll = scrollViewer?.VerticalOffset ?? 0;

            ViewModel = vm;

            Dispatcher.BeginInvoke (new Action (() =>
            {
                if (preserveScroll > 0 && scrollViewer != null)
                {
                    var newScrollViewer = GetScrollViewer (CurrentDirectory);
                    if (newScrollViewer != null && newScrollViewer.ScrollableHeight >= preserveScroll)
                        newScrollViewer.ScrollToVerticalOffset (preserveScroll);
                }
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private DirectoryViewModel GetNewViewModel (string path)
        {
            if (!string.IsNullOrEmpty (path))
            {
                if (!VFS.IsVirtual)
                    path = Path.GetFullPath (path);
                var entry = VFS.FindFile (path);
                if (!(entry is SubDirEntry))
                    SetBusyState();
                VFS.ChDir (entry);
            }
            return new DirectoryViewModel (VFS.FullPath, VFS.GetFiles(), VFS.IsVirtual);
        }

        private DirectoryViewModel TryCreateViewModel (string path)
        {
            try
            {
                return GetNewViewModel (path);
            }
            catch (Exception ex)
            {
                SetFileStatus (string.Format ("{0}: {1}", Path.GetFileName (path), ex.Message));
                return null;
            }
        }

        private DirectoryViewModel CreateViewModel (string path, bool suppress_warning = false)
        {
            try
            {
                return GetNewViewModel (path);
            }
            catch (Exception ex)
            {
                if (!suppress_warning)
                    PopupError (ex.Message, Localization._T ("MsgErrorOpening"));
                return new DirectoryViewModel (new string[] { "" }, new Entry[0], false);
            }
        }

        public DirectoryPosition GetCurrentPosition()
        {
            var evm = CurrentDirectory.SelectedItem as EntryViewModel;
            var scrollViewer = GetScrollViewer (CurrentDirectory);
            double scrollOffset = scrollViewer?.VerticalOffset ?? 0;
            return new DirectoryPosition (ViewModel, evm, scrollOffset);
        }

        public bool SetCurrentPosition (DirectoryPosition pos)
        {
            try
            {
                // Save current state for nested archive handling
                var currentVfsCount = VFS.Count;
                var currentFullPath = VFS.FullPath?.ToArray();

                // Navigate to the new position
                VFS.FullPath = pos.Path;
                var vm = new DirectoryViewModel (VFS.FullPath, VFS.GetFiles(), VFS.IsVirtual);
                ViewModel = vm;

                Dispatcher.BeginInvoke (new Action (() =>
                {
                    string itemToSelect = pos.Item;

                    // Handle special case when we exited a nested archive
                    if (string.IsNullOrEmpty (itemToSelect) && 
                        currentVfsCount > VFS.Count && 
                        currentFullPath != null && 
                        currentFullPath.Length > VFS.Count - 1)
                    {
                        var archiveWeCameFrom = currentFullPath[VFS.Count - 1];
                        if (!string.IsNullOrEmpty (archiveWeCameFrom))
                            itemToSelect = VFS.GetFileName (archiveWeCameFrom);
                    }

                    // Select the item
                    if (!string.IsNullOrEmpty (itemToSelect)) 
                        lv_SelectItem (itemToSelect, false);
                    else if (ViewModel.Count > 0)
                        lv_SelectItem (0);

                    // Restore exact scroll position from history
                    var scrollViewer = GetScrollViewer (CurrentDirectory);
                    if (scrollViewer != null)
                    {
                        if (pos.ScrollOffset > 0)
                        {
                            CurrentDirectory.UpdateLayout();
                            scrollViewer.ScrollToVerticalOffset (pos.ScrollOffset);
                        }
                        else
                        {
                            var item = ViewModel.Find (itemToSelect);
                            if (item != null)
                            {
                                var lvi = (ListViewItem)CurrentDirectory.ItemContainerGenerator.ContainerFromItem (item);
                                if (lvi != null && !IsItemVisible (lvi, scrollViewer))
                                    CurrentDirectory.ScrollIntoView (item);
                            }
                        }
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);

                return true;
            }
            catch (Exception ex)
            {
                SetFileStatus (ex.Message);
                return false;
            }
        }

        private bool IsItemVisible (ListViewItem item, ScrollViewer scrollViewer)
        {
            if (item == null || scrollViewer == null)
                return false;

            var transform = item.TransformToAncestor (scrollViewer);
            var positionInScrollViewer = transform.Transform (new Point (0, 0));

            return positionInScrollViewer.Y >= 0 && 
                   positionInScrollViewer.Y + item.ActualHeight <= scrollViewer.ViewportHeight;
        }

        private void NavigateBack()
        {
            var previous = m_history.GoBack();
            if (previous != null)
            {
                SetCurrentPosition (previous);
            }
        }

        private void NavigateForward()
        {
            var next = m_history.GoForward();
            if (next != null)
            {
                SetCurrentPosition (next);
            }
        }

        public void ChangePosition (DirectoryPosition new_pos)
        {
            var current = GetCurrentPosition();
            if (!current.Path.SequenceEqual (new_pos.Path))
                m_history.NavigateTo (GetCurrentPosition());
            SetCurrentPosition (new_pos);
        }

        private void WatchDirectoryChanges (string path)
        {
            m_watcher.Path = path;
            m_watcher.EnableRaisingEvents = true;
        }

        public void StopWatchDirectoryChanges ()
        {
            m_watcher.EnableRaisingEvents = false;
        }

        public void ResumeWatchDirectoryChanges ()
        {
            m_watcher.EnableRaisingEvents = !ViewModel.IsArchive;
        }

        private void InvokeRefreshView (object source, FileSystemEventArgs e)
        {
            var watcher = source as FileSystemWatcher;
            var vm = ViewModel;
            if (!vm.IsArchive && vm.Path.First() == watcher.Path)
            {
                watcher.EnableRaisingEvents = false;
                Dispatcher.Invoke (RefreshView);
            }
        }

        public void RefreshView ()
        {
            VFS.Flush();
            var pos = GetCurrentPosition();
            var currentFilter = _filterText;
            SetCurrentPosition (pos);

            if (!string.IsNullOrEmpty (currentFilter))
            {
                _filterText = currentFilter;
                FilterInput.Text = currentFilter;
                FilterInputBorder.Visibility = Visibility.Visible;
                _isFilterActive = true;
                ApplyFilter();
            }
        }

        private ScrollViewer GetScrollViewer (DependencyObject o)
        {
            if (o is ScrollViewer)
                return o as ScrollViewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount (o); i++)
            {
                var child = VisualTreeHelper.GetChild (o, i);
                var result = GetScrollViewer (child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void UpdateFilterMargin ()
        {
            var scrollViewer = GetScrollViewer (CurrentDirectory);
            if (scrollViewer != null)
            {
                var margin = scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible
                    ? new Thickness (0, 0, SystemParameters.VerticalScrollBarWidth, 0)
                    : new Thickness (0);
                CurrentDirectory.Tag = margin;
            }
        }

        static readonly Regex FullpathRe = new Regex(@"^(?:[a-z]:|[\\/])", RegexOptions.IgnoreCase);

        private void acb_OnKeyDown (object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Return)
                return;
            string path = (sender as AutoCompleteBox).Text;
            path = path.Trim (' ', '"');
            if (string.IsNullOrEmpty (path))
                return;
            if (FullpathRe.IsMatch (path))
            {
                OpenFile (path);
                return;
            }
            try
            {
                PushViewModel (GetNewViewModel (path));
                ListViewFocus();
            }
            catch (Exception ex)
            {
                PopupError (ex.Message, Localization._T ("MsgErrorOpening"));
            }
        }
    }
}