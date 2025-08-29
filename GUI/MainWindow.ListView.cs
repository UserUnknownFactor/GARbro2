using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;

using GameRes;
using System.Diagnostics;

namespace GARbro.GUI
{
    public partial class MainWindow
    {
        public bool EnableListViewDragDrop
        {
            get { return (_overlayControl != null && _overlayControl.IsVisible); }
        }

        private void lv_SelectItem (EntryViewModel item)
        {
            if (item != null)
            {
                CurrentDirectory.SelectedItem = item;
                CurrentDirectory.ScrollIntoView (item);
                var lvi = (ListViewItem)CurrentDirectory.ItemContainerGenerator.ContainerFromItem (item);
                if (lvi != null)
                    lvi.Focus();
            }
        }

        private void lv_SelectItem (int index)
        {
            CurrentDirectory.SelectedIndex = index;
            CurrentDirectory.ScrollIntoView (CurrentDirectory.SelectedItem);
            var lvi = (ListViewItem)CurrentDirectory.ItemContainerGenerator.ContainerFromIndex (index);
            if (lvi != null)
                lvi.Focus();
        }

        private void lv_SelectItem (string name, bool scrollIntoView = true)
        {
            if (!string.IsNullOrEmpty (name))
            {
                var item = ViewModel.Find (name);
                if (item != null)
                {
                    CurrentDirectory.SelectedItem = item;
                    if (scrollIntoView)
                        CurrentDirectory.ScrollIntoView (item);
                    var lvi = (ListViewItem)CurrentDirectory.ItemContainerGenerator.ContainerFromItem (item);
                    if (lvi != null)
                        lvi.Focus();
                }
            }
        }

        public void ListViewFocus ()
        {
            if (CurrentDirectory.SelectedIndex != -1)
            {
                var item = CurrentDirectory.SelectedItem;
                var lvi = CurrentDirectory.ItemContainerGenerator.ContainerFromItem (item) as ListViewItem;
                if (lvi != null)
                {
                    lvi.Focus();
                    return;
                }
            }
            CurrentDirectory.Focus();
        }

        private void lvi_Selected (object sender, RoutedEventArgs args)
        {
            var lvi = sender as ListViewItem;
            if (lvi == null)
                return;
            var entry = lvi.Content as EntryViewModel;
            if (entry == null)
                return;
            PreviewEntry (entry.Source);
        }

        private void lv_SelectionChanged (object sender, SelectionChangedEventArgs args)
        {
            var lv = sender as ListView;
            if (null == lv)
                return;

            var item = lv.SelectedItem as EntryViewModel;
            if (item != null && m_last_selected != item)
            {
                m_last_selected = item;
                _textPreviewHandler?.Reset();

                if (_overlayControl == null || !_overlayControl.IsVisible)
                {
                    PreviewEntry (item.Source);
                    return;
                }
                else
                {
                    PreviewOverlay (item);
                }
            }
        }

        private void PreviewOverlay (EntryViewModel item)
        {
            if (CurrentDirectory.SelectedItems.Count == 1 && item.Type == "image")
            {
                if (!_overlayControl.HasImage (item.Name))
                {
                    try
                    {
                        // refresh image
                        using (var data = VFS.OpenImage (item.Source))
                        {
                            _overlayControl.SetPreviewImage (data.Image.Bitmap, item.Name);
                        }
                        return;
                    }
                    catch { }
                }
            }
            _overlayControl.ClearPreview();
        }

        private void lvi_DoubleClick (object sender, MouseButtonEventArgs args)
        {
            var lvi = sender as ListViewItem;
            if (Commands.OpenItem.CanExecute (null, lvi))
            {
                Commands.OpenItem.Execute (null, lvi);
                args.Handled = true;
            }
        }

        private ListViewItem lv_GetCurrentContainer ()
        {
            int current = CurrentDirectory.SelectedIndex;
            if (-1 == current)
                return null;

            return CurrentDirectory.ItemContainerGenerator.ContainerFromIndex (current) as ListViewItem;
        }

        private void lv_SetSortMode (string sortBy, ListSortDirection direction)
        {
            m_lvSortByColumn = null;
            GridView view = CurrentDirectory.View as GridView;
            foreach (var column in view.Columns)
            {
                var header = column.Header as GridViewColumnHeader;
                if (null != header && !string.IsNullOrEmpty (sortBy) && sortBy.Equals (header.Tag))
                {
                    if (ListSortDirection.Ascending == direction)
                        column.HeaderTemplate = Resources["SortArrowUp"] as DataTemplate;
                    else
                        column.HeaderTemplate = Resources["SortArrowDown"] as DataTemplate;
                    m_lvSortByColumn = header;
                    m_lvSortDirection = direction;
                }
                else
                    column.HeaderTemplate = Resources["SortArrowNone"] as DataTemplate;
            }
            SortMode = sortBy;
        }

        private void lv_PreviewMouseLeftButtonDown (object sender, MouseButtonEventArgs e)
        {
            _lvDragStartPoint = e.GetPosition (null);
        }

        private void lv_PreviewMouseMove (object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var originalSource = e.OriginalSource as DependencyObject;
                if (originalSource != null) {
                    var scrollBar = FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(originalSource);
                    if (scrollBar != null)
                        return;
                    var header = FindVisualParent<GridViewColumnHeader> (originalSource);
                    if (header != null)
                        return;
                    var thumb = originalSource as System.Windows.Controls.Primitives.Thumb;
                    if (thumb != null)
                        return;
                }

                if (!IsDragDropEnabled()) 
                    return;

                // check for drag selection blocking
                bool hasModifier = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != ModifierKeys.None;
                if (!hasModifier)
                {
                    // Check if we're starting from an already selected item (for drag-drop)
                    Point position = e.GetPosition (CurrentDirectory);
                    var hit = VisualTreeHelper.HitTest (CurrentDirectory, position);
                    if (hit != null)
                    {
                        var item = FindVisualParent<ListViewItem>(hit.VisualHit);
                        if (item == null || !item.IsSelected)
                        {
                            // Not dragging from a selected item = block selection
                            e.Handled = true;
                            return;
                        }
                    }
                }

                // Rest of drag-drop code
                Point dragPosition = e.GetPosition (null);

                if (Math.Abs (dragPosition.X - _lvDragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs (dragPosition.Y - _lvDragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var selectedItems = CurrentDirectory.SelectedItems.Cast<EntryViewModel>().ToArray();
                    if (selectedItems.Any())
                    {
                        // Check if we have any images and overlay is visible
                        bool hasImages = selectedItems.Any (item => item.Type == "image");
                        if (hasImages && _overlayControl != null && _overlayControl.IsVisible)
                            _overlayControl.ShowDropHint();

                        var data = new DataObject();
                        if (selectedItems.Length == 1)
                            data.SetData (typeof(EntryViewModel), selectedItems[0]);
                        else
                            data.SetData ("EntryViewModelArray", selectedItems);

                        try
                        {
                            DragDrop.DoDragDrop (CurrentDirectory, data, DragDropEffects.Copy);
                        }
                        finally
                        {
                            if (_overlayControl != null)
                                _overlayControl.HideDropHint();
                        }
                    }
                }
            }
        }

        private bool IsDragDropEnabled()
        {
            return (_overlayControl != null && _overlayControl.IsVisible);
        }

        // Helper method to find parent of specific type
        private static T FindVisualParent<T> (DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent (child);

            if (parentObject == null)
                return null;

            T parent = parentObject as T;
            if (parent != null)
                return parent;

            return FindVisualParent<T> (parentObject);
        }

        // Add a field at the class level
        private Point _lvDragStartPoint;

        private void lv_Sort (string sortBy, ListSortDirection direction)
        {
            var dataView = CollectionViewSource.GetDefaultView (CurrentDirectory.ItemsSource) as ListCollectionView;
            dataView.CustomSort = new FileSystemComparer (sortBy, direction);
        }

        private void lv_ColumnHeaderClicked (object sender, RoutedEventArgs e)
        {
            var headerClicked = e.OriginalSource as GridViewColumnHeader;

            if (null == headerClicked)
                return;
            if (headerClicked.Role == GridViewColumnHeaderRole.Padding)
                return;

            ListSortDirection direction;
            if (headerClicked != m_lvSortByColumn)
                direction = ListSortDirection.Ascending;
            else if (m_lvSortDirection == ListSortDirection.Ascending)
                direction = ListSortDirection.Descending;
            else
                direction = ListSortDirection.Ascending;

            string sortBy = headerClicked.Tag.ToString();
            lv_Sort (sortBy, direction);
            SortMode = sortBy;

            if (m_lvSortByColumn != null && m_lvSortByColumn != headerClicked)
                m_lvSortByColumn.Column.HeaderTemplate = Resources["SortArrowNone"] as DataTemplate;

            if (ListSortDirection.Ascending == direction)
                headerClicked.Column.HeaderTemplate = Resources["SortArrowUp"] as DataTemplate;
            else
                headerClicked.Column.HeaderTemplate = Resources["SortArrowDown"] as DataTemplate;

            m_lvSortByColumn = headerClicked;
            m_lvSortDirection = direction;
        }

        private void lv_TextInput (object sender, TextCompositionEventArgs e)
        {
            if (!FilterInputBorder.IsVisible && !string.IsNullOrEmpty (e.Text) && e.Text != "\x1B")
            {
                CurrentDirectory.SelectedItems.Clear();
                ShowFilterInput();
                FilterInput.Text = e.Text;
                FilterInput.CaretIndex = FilterInput.Text.Length;
                e.Handled = true;
                return;
            }
            e.Handled = true;
        }

        // To prevent "sticking" of arrow keys on long press scroll with them
        private void lv_PreviewKeyUp (object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down || 
                e.Key == Key.PageUp || e.Key == Key.PageDown ||
                e.Key == Key.Home || e.Key == Key.End)
            {
                // Clear all pending input events
                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    new Action(() => 
                    {
                        // Process all pending input to clear the queue
                        DispatcherFrame frame = new DispatcherFrame();
                        Dispatcher.CurrentDispatcher.BeginInvoke(
                            DispatcherPriority.Background,
                            new DispatcherOperationCallback (ExitFrame), frame);
                        Dispatcher.PushFrame (frame);
                    }));
            }
        }

        private static object ExitFrame (object f)
        {
            ((DispatcherFrame)f).Continue = false;
            return null;
        }

        /*private void lv_PreviewKeyUp (object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down || 
                e.Key == Key.PageUp || e.Key == Key.PageDown ||
                e.Key == Key.Home || e.Key == Key.End)
            {
                // Flush all pending keyboard input events
                Dispatcher.Invoke(() => { }, DispatcherPriority.Input);
                var scrollViewer = GetScrollViewer (CurrentDirectory);
                if (scrollViewer != null)
                    scrollViewer.ScrollToVerticalOffset (scrollViewer.VerticalOffset);
                e.Handled = true;
            }
        }*/

        private void lv_KeyDown (object sender, KeyEventArgs e)
        {
            if (e.IsDown)
            {
                switch (e.Key)
                {
                case Key.Space:
                    CycleToNextItem (forward: true);
                    e.Handled = true;
                    break;

                case Key.Back:
                    CycleToNextItem (forward: false);
                    e.Handled = true;
                    break;
                }
            }
        }

        private void ShowFilterInput ()
        {
            UpdateFilterMargin();
            FilterInputBorder.Visibility = Visibility.Visible;
            FilterInput.Focus();
            _isFilterActive = true;
        }

        private void HideFilterInput ()
        {
            if (string.IsNullOrEmpty (FilterInput.Text))
            {
                FilterInputBorder.Visibility = Visibility.Collapsed;
                _filterText = string.Empty;
                _isFilterActive = false;
                ApplyFilter();
                ListViewFocus();
                SetFileStatus ("");
            }
        }

        private void FilterInput_TextChanged (object sender, TextChangedEventArgs e)
        {
            _filterText = FilterInput.Text;
            CurrentDirectory.SelectedItems.Clear();
            ApplyFilter();

            if (string.IsNullOrEmpty (_filterText))
                SetFileStatus ("");
        }

        private void FilterInput_LostFocus (object sender, RoutedEventArgs e)
        {
            if (ClearFilterButton.IsKeyboardFocusWithin)
                return;

            CurrentDirectory.IsHitTestVisible = false;

            HideFilterInput();

            Dispatcher.BeginInvoke (new Action (() =>
            {
                CurrentDirectory.IsHitTestVisible = true;
            }), DispatcherPriority.Input);
        }

        private void FilterInput_KeyDown (object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                FilterInput.Text = string.Empty;
                HideFilterInput();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                ListViewFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Up || e.Key == Key.Down)
            {
                ListViewFocus();
                if (CurrentDirectory.Items.Count > 0)
                {
                    if (CurrentDirectory.SelectedIndex == -1)
                        CurrentDirectory.SelectedIndex = 0;

                    var container = CurrentDirectory.ItemContainerGenerator.ContainerFromIndex (CurrentDirectory.SelectedIndex) as ListViewItem;
                    container?.Focus();
                }
                e.Handled = true;
            }
        }

        private void ClearFilterButton_Click (object sender, RoutedEventArgs e)
        {
            FilterInput.Text = string.Empty;
            _filterText = string.Empty;
            _isFilterActive = false;
            FilterInputBorder.Visibility = Visibility.Collapsed;
            ApplyFilter();
            ListViewFocus();
            SetFileStatus ("");
        }

        private void ApplyFilter ()
        {
            var view = CollectionViewSource.GetDefaultView (CurrentDirectory.ItemsSource) as ListCollectionView;
            if (view != null)
            {
                if (string.IsNullOrEmpty (_filterText))
                {
                    view.Filter = null;
                }
                else
                {
                    view.Filter = item =>
                    {
                        var entry = item as EntryViewModel;
                        if (entry == null) return true;

                        if (entry.Name == VFS.DIR_PARENT)
                            return true;

                        return entry.Name.IndexOf (_filterText, StringComparison.OrdinalIgnoreCase) >= 0;
                    };
                }

                Dispatcher.BeginInvoke (new Action (() => UpdateFilterMargin()),
                    System.Windows.Threading.DispatcherPriority.Loaded);

                if (!string.IsNullOrEmpty (_filterText))
                {
                    int visibleCount = CurrentDirectory.Items.Count;
                    int totalCount = ViewModel.Count;

                    bool hasParentDir = ViewModel.Any (e => e.Name == VFS.DIR_PARENT);
                    if (hasParentDir)
                    {
                        totalCount--;
                        bool parentDirVisible = CurrentDirectory.Items.Cast<EntryViewModel>().Any (e => e.Name == VFS.DIR_PARENT);
                        if (parentDirVisible)
                            visibleCount--;
                    }

                    if (visibleCount > 0 && totalCount > 0)
                        SetFileStatus (Localization.Format ("FilterItems", visibleCount, totalCount));
                    else
                        SetFileStatus (Localization._T ("FilterNoMatchingEntries"));
                }
                else
                    SetFileStatus ("");

                if (CurrentDirectory.Items.Count > 0 && CurrentDirectory.SelectedIndex == -1)
                    CurrentDirectory.SelectedIndex = 0;
            }
        }

        private void ClearFilter ()
        {
            if (_isFilterActive || !string.IsNullOrEmpty (_filterText))
            {
                _isFilterActive = false;

                if (CurrentDirectory.Items.Count > 0 && CurrentDirectory.SelectedIndex == -1)
                    CurrentDirectory.SelectedIndex = 0;
                _filterText = string.Empty;
                FilterInput.Text = string.Empty;
                FilterInputBorder.Visibility = Visibility.Collapsed;

                var view = CollectionViewSource.GetDefaultView (CurrentDirectory.ItemsSource) as ListCollectionView;
                if (view != null)
                    view.Filter = null;
            }
            SetFileStatus ("");
        }

        private void DeleteSelectedItems()
        {
            var items = CurrentDirectory.SelectedItems.Cast<EntryViewModel>().Where (f => !f.IsDirectory);
            if (!items.Any())
                return;

            this.IsEnabled = false;
            try
            {
                VFS.Flush();
                ResetPreviewPane();
                if (!items.Skip (1).Any())
                {
                    string item_name = Path.Combine (CurrentPath, items.First().Name);
                    Trace.WriteLine (item_name, "DeleteItemExec");
                    FileOperationHelper.DeleteToRecycleBin (item_name, true);
                    DeleteItem (lv_GetCurrentContainer());
                    SetFileStatus (Localization.Format ("MsgDeletedItem", item_name));
                }
                else
                {
                    int count = 0;
                    StopWatchDirectoryChanges();
                    try
                    {
                        var file_list = items.Select (entry => Path.Combine (CurrentPath, entry.Name));
                        if (!GARbro.Shell.File.Delete (file_list, new WindowInteropHelper (this).Handle))
                            throw new ApplicationException (Localization._T ("DeleteFailed"));
                        count = file_list.Count();
                    }
                    finally
                    {
                        ResumeWatchDirectoryChanges();
                    }
                    RefreshView();
                    SetFileStatus (Localization.Format ("MsgDeletedItems", count));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SetFileStatus (ex.Message);
            }
            finally
            {
                this.IsEnabled = true;
            }
        }

        private void DeleteItem (ListViewItem item)
        {
            int i = CurrentDirectory.SelectedIndex;
            int next = -1;
            if (i + 1 < CurrentDirectory.Items.Count)
                next = i + 1;
            else if (i > 0)
                next = i - 1;

            if (next != -1)
                CurrentDirectory.SelectedIndex = next;

            var entry = item.Content as EntryViewModel;
            if (entry != null)
            {
                ViewModel.Remove (entry);
            }
        }

        private void RenameElement (ListViewItem item)
        {
            if (item == null)
                return;
            // TODO: Implement inline rename
        }

        private void SelectByMask()
        {
            try
            {
                var ext_list = new SortedSet<string>();
                foreach (var entry in ViewModel)
                {
                    var ext = Path.GetExtension (entry.Name).ToLowerInvariant();
                    if (!string.IsNullOrEmpty (ext))
                        ext_list.Add (ext);
                }

                var selection = new EnterMaskDialog (ext_list.Select (ext => "*" + ext));
                selection.Owner = this;
                var result = selection.ShowDialog();
                if (!result.Value)
                    return;

                if ("*.*" == selection.Mask.Text)
                {
                    CurrentDirectory.SelectAll();
                    return;
                }

                SetBusyState();
                var glob = new FileNameGlob (selection.Mask.Text);
                var matching = ViewModel.Where (entry => glob.IsMatch (entry.Name));
                if (!matching.Any())
                {
                    SetFileStatus (Localization.Format ("MsgNoMatching", selection.Mask.Text));
                    return;
                }

                var selected = CurrentDirectory.SelectedItems.Cast<EntryViewModel>();
                matching = matching.Except (selected).ToList();
                int count = matching.Count();
                CurrentDirectory.SetSelectedItems (selected.Concat (matching));
                if (count != 0)
                    SetFileStatus (count.Pluralize ("MsgSelectedFiles"));
            }
            catch (Exception ex)
            {
                SetFileStatus (ex.Message);
            }
        }
    }
}