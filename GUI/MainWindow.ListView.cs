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

        private EntryViewModel _itemBeingRenamed;

        // Command execution
        private void RenameItemExec(object sender, ExecutedRoutedEventArgs e)
        {
            if (CurrentDirectory.SelectedItems.Count != 1)
                return;

            var item = CurrentDirectory.SelectedItem as EntryViewModel;
            if (item == null || item.IsDirectory || VFS.IsVirtual)
                return;

            StartRename(item);
        }

        private void StartRename(EntryViewModel item)
        {
            // Cancel any existing rename
            if (_itemBeingRenamed != null && _itemBeingRenamed != item)
                CancelRename();

            _itemBeingRenamed = item;
            item.IsEditing = true;

            // Ensure the item is visible
            CurrentDirectory.ScrollIntoView(item);
        }

        private void CancelRename()
        {
            if (_itemBeingRenamed != null)
            {
                _itemBeingRenamed.IsEditing = false;
                _itemBeingRenamed = null;
                ListViewFocus();
            }
        }

        private void CommitRename()
        {
            if (_itemBeingRenamed == null)
                return;

            var item = _itemBeingRenamed;
            var oldName = item.Name;
            var newName = item.EditingName?.Trim();

            // Validate new name
            if (string.IsNullOrEmpty(newName) || newName == oldName)
            {
                CancelRename();
                return;
            }

            // Check for invalid characters
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                SetFileStatus(Localization._T("MsgInvalidFileName"));
                CancelRename();
                return;
            }

            try
            {
                var oldPath = Path.Combine(CurrentPath, oldName);
                var newPath = Path.Combine(CurrentPath, newName);

                // Check if target exists
                if (File.Exists(newPath))
                {
                    SetFileStatus(string.Format(Localization._T("MsgFileAlreadyExists"), newName));
                    CancelRename();
                    return;
                }

                // Perform rename
                File.Move(oldPath, newPath);

                // Update the view model
                item.Name = newName;
                item.IsEditing = false;
                _itemBeingRenamed = null;

                SetFileStatus(string.Format(Localization._T("MsgRenamed"), oldName, newName));

                // Refresh view to update the item
                RefreshView();

                // Re-select the renamed item
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    lv_SelectItem(newName);
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                SetFileStatus(string.Format(Localization._T("MsgRenameFailed"), ex.Message));
                CancelRename();
            }
        }

        // Event handlers for the rename TextBox
        private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitRename();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelRename();
                e.Handled = true;
            }
        }

        private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Use dispatcher to allow focus change to complete
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var textBox = sender as TextBox;
                if (_itemBeingRenamed != null && textBox != null && !textBox.IsFocused)
                    CommitRename();
            }), DispatcherPriority.Input);
        }

        private void RenameTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox != null)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    textBox.Focus();
                    Keyboard.Focus(textBox);

                    // Select filename without extension
                    var name = textBox.Text;
                    var extIndex = name.LastIndexOf('.');
                    if (extIndex > 0)
                        textBox.Select(0, extIndex);
                    else
                        textBox.SelectAll();
                }), DispatcherPriority.Input);
            }
        }

        private void lv_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // If we're renaming, check if click is outside the rename textbox
            if (_itemBeingRenamed != null)
            {
                var hitTest = VisualTreeHelper.HitTest(CurrentDirectory, e.GetPosition(CurrentDirectory));
                if (hitTest != null)
                {
                    var textBox = FindVisualParent<TextBox>(hitTest.VisualHit);
                    if (textBox == null || textBox.Name != "item_EditName")
                        CommitRename();
                }
            }

            _lvDragStartPoint = e.GetPosition(null);
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
            if (CurrentDirectory.SelectedItems.Count == 1 && 
                item.Type == "image" && 
                !_overlayControl.HasImage(item.Name))
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
            if (_itemBeingRenamed != null)
            {
                e.Handled = false;
                return;
            }
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
                case Key.Escape:
                    if (_isFilterActive)
                        ClearFilter();
                    _previewStateMachine?.StopCurrentPlayback();
                    break;
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

        private void FilterInput_TextChanged (object sender, TextChangedEventArgs e)
        {
            if (!_isFilterActive)
                return;
            _filterText = FilterInput.Text;
            ApplyFilter();
        }

        private void FilterInput_LostFocus (object sender, RoutedEventArgs e)
        {
            if (ClearFilterButton.IsKeyboardFocusWithin)
                return;

            if (string.IsNullOrEmpty(FilterInput.Text))
            {
                Dispatcher.BeginInvoke(new Action(() => {
                    ClearFilter();
                }), DispatcherPriority.Background);
            }
        }

        private void FilterInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                ClearFilter();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                ListViewFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Up || e.Key == Key.Down)
            {
                if (CurrentDirectory.Items.Count > 0)
                {
                    int currentIndex = CurrentDirectory.SelectedIndex;
                    int newIndex = currentIndex;

                    if (e.Key == Key.Up)
                        newIndex = currentIndex > 0 ? currentIndex - 1 : 0;
                    else if (e.Key == Key.Down)
                        newIndex = currentIndex < CurrentDirectory.Items.Count - 1 ? currentIndex + 1 : currentIndex;

                    if (newIndex == -1)
                        newIndex = 0;

                    CurrentDirectory.SelectedIndex = newIndex;
                    CurrentDirectory.ScrollIntoView(CurrentDirectory.SelectedItem);
                }
                e.Handled = true;
            }
        }

        private void ClearFilter()
        {
            _isFilterActive = false;

            FilterInput.Text = string.Empty;
            _filterText      = string.Empty;
            
            FilterInputBorder.Visibility = Visibility.Collapsed;
            ListViewFocus();
            ApplyFilter();
        }

        private void ClearFilterButton_Click (object sender, RoutedEventArgs e)
        {
            ClearFilter();
        }

        private void ApplyFilter ()
        {
            var view = CollectionViewSource.GetDefaultView (CurrentDirectory.ItemsSource) as ListCollectionView;
            if (view != null)
            {
                if (string.IsNullOrEmpty (_filterText))
                    view.Filter = null;
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
            }
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