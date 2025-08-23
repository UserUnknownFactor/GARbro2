using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Threading.Tasks;
using GameRes;

namespace GARbro.GUI
{
    public partial class ImageOverlayControl : UserControl
    {
        private ImageOverlayHandler _overlayHandler;
        private ObservableCollection<OverlayImageViewModel> _layers;
        private Point _dragStartPoint;
        private ListBoxItem _draggedItem;
        private bool _isDragging;

        private bool   _isDraggingImage = false;
        private Point  _imageDragStart;
        private Image  _draggedImage;
        private double _originalOffsetX;
        private double _originalOffsetY;

        private bool _isExternalDragInProgress = false;
        private DispatcherTimer _hintVisibilityTimer;

        // Add this event
        public event EventHandler LayersChanged;

        public ImageOverlayControl ()
        {
            InitializeComponent();

            _overlayHandler = new ImageOverlayHandler (OverlayCanvas);
            _layers = new ObservableCollection<OverlayImageViewModel>();
            LayersList.ItemsSource = _layers;
            Visibility = Visibility.Collapsed;

            OverlayCanvas.PreviewMouseLeftButtonDown += Canvas_PreviewMouseLeftButtonDown;
            OverlayCanvas.PreviewMouseMove += Canvas_PreviewMouseMove;
            OverlayCanvas.PreviewMouseLeftButtonUp += Canvas_PreviewMouseLeftButtonUp;
            OverlayCanvas.MouseLeave += Canvas_MouseLeave;

            PreviewKeyDown += OnPreviewKeyDown;
            PreviewKeyUp   += OnPreviewKeyUp;
        }

        // Helper method to raise the event
        private void OnLayersChanged()
        {
            LayersChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ShowDropHint()
        {
            System.Diagnostics.Debug.WriteLine("ShowDropHint called");
            _isExternalDragInProgress = true;
            
            // Use a timer to keep forcing visibility
            _hintVisibilityTimer?.Stop();
            _hintVisibilityTimer = new DispatcherTimer();
            _hintVisibilityTimer.Interval = TimeSpan.FromMilliseconds(50);
            _hintVisibilityTimer.Tick += (s, e) => {
                if (_isExternalDragInProgress)
                    DropHint.Visibility = Visibility.Visible;
                else
                {
                    DropHint.Visibility = Visibility.Collapsed;
                    _hintVisibilityTimer.Stop();
                }
            };
            _hintVisibilityTimer.Start();
            
            Dispatcher.Invoke(() => {
                DropHint.Visibility = Visibility.Visible;
                System.Diagnostics.Debug.WriteLine($"Hint visibility set to: {DropHint.Visibility}");
            });
        }

        public void HideDropHint()
        {
            System.Diagnostics.Debug.WriteLine("HideDropHint called");
            _isExternalDragInProgress = false;
            _hintVisibilityTimer?.Stop();
            Dispatcher.Invoke(() => {
                DropHint.Visibility = Visibility.Collapsed;
            });
        }

        private void OnPreviewKeyDown (object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                TouchScrolling.SetIsEnabled (OverlayCanvas, false);
                UpdateCursorForShift();
            }
        }

        private void OnPreviewKeyUp (object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                if (!_isDraggingImage)
                {
                    TouchScrolling.SetIsEnabled (OverlayCanvas, true);
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private void UpdateCursorForShift ()
        {
            if (_isDraggingImage) return;

            var mousePos = Mouse.GetPosition (OverlayCanvas);
            var hit = VisualTreeHelper.HitTest (OverlayCanvas, mousePos);
            if (hit != null && hit.VisualHit is Image img && img.Tag is OverlayImage)
                Mouse.OverrideCursor = Cursors.Hand;
        }

        private void Canvas_PreviewMouseLeftButtonDown (object sender, MouseButtonEventArgs e)
        {
            // Only start drag if Shift is held
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                return;

            var hit = e.OriginalSource as Image;
            if (hit != null && hit.Tag is OverlayImage overlayImage)
            {
                _isDraggingImage = true;
                _draggedImage    = hit;
                _imageDragStart  = e.GetPosition (OverlayCanvas);
                _originalOffsetX = overlayImage.OffsetX;
                _originalOffsetY = overlayImage.OffsetY;

                Mouse.OverrideCursor = Cursors.SizeAll;
                OverlayCanvas.CaptureMouse();

                e.Handled = true;
            }
        }

        private void Canvas_PreviewMouseMove (object sender, MouseEventArgs e)
        {
            if (_isDraggingImage && _draggedImage != null)
            {
                var currentPos = e.GetPosition (OverlayCanvas);
                var deltaX = currentPos.X - _imageDragStart.X;
                var deltaY = currentPos.Y - _imageDragStart.Y;

                var overlayImage = _draggedImage.Tag as OverlayImage;
                if (overlayImage != null)
                {
                    Canvas.SetLeft (_draggedImage, Canvas.GetLeft (_draggedImage) + deltaX);
                    Canvas.SetTop (_draggedImage, Canvas.GetTop (_draggedImage) + deltaY);
                    _imageDragStart = currentPos;
                }

                e.Handled = true;
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                var hit = e.OriginalSource as Image;
                if (hit != null && hit.Tag is OverlayImage img && img.ZIndex != int.MaxValue)
                    Mouse.OverrideCursor = Cursors.Hand;
                else
                    Mouse.OverrideCursor = null;
            }
        }

        private void Canvas_PreviewMouseLeftButtonUp (object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingImage && _draggedImage != null)
            {
                var overlayImage = _draggedImage.Tag as OverlayImage;
                if (overlayImage != null)
                {
                    var currentLeft = Canvas.GetLeft (_draggedImage);
                    var currentTop = Canvas.GetTop (_draggedImage);
                    var baseX = (OverlayCanvas.Width - overlayImage.Source.PixelWidth) / 2.0;
                    var baseY = (OverlayCanvas.Height - overlayImage.Source.PixelHeight) / 2.0;

                    _overlayHandler.SetImagePosition (overlayImage.ZIndex, 
                        currentLeft - baseX, 
                        currentTop - baseY);
                }

                _isDraggingImage = false;
                _draggedImage = null;
                Mouse.OverrideCursor = null;
                OverlayCanvas.ReleaseMouseCapture();

                // Re-enable touch scrolling if Shift is not held
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                    TouchScrolling.SetIsEnabled (OverlayCanvas, true);

                e.Handled = true;
            }
        }

        private void Canvas_MouseLeave (object sender, MouseEventArgs e)
        {
            if (_isDraggingImage)
            {
                _isDraggingImage = false;
                _draggedImage = null;
                Mouse.OverrideCursor = null;
                OverlayCanvas.ReleaseMouseCapture();

                // Re-enable touch scrolling
                TouchScrolling.SetIsEnabled (OverlayCanvas, true);

                // Restore original position
                _overlayHandler.UpdateComposite();
            }
        }

        private void ImageSizeDiagnostics ()
        {
            var images = _overlayHandler.GetImages();
            foreach (var img in images)
            {
                System.Diagnostics.Debug.WriteLine ($"Image: {img.Name}, Size: {img.Source.PixelWidth}x{img.Source.PixelHeight}, DPI: {img.Source.DpiX}x{img.Source.DpiY} Scale: {img.DpiScaleX}x{img.DpiScaleY}");
            }

            System.Diagnostics.Debug.WriteLine ($"Canvas Size: {OverlayCanvas.Width}x{OverlayCanvas.Height}");
            System.Diagnostics.Debug.WriteLine ($"Viewbox Size: {OverlayViewbox.ActualWidth}x{OverlayViewbox.ActualHeight}");
            System.Diagnostics.Debug.WriteLine ($"ScrollViewer Size: {OverlayScrollViewer.ActualWidth}x{OverlayScrollViewer.ActualHeight}");
            System.Diagnostics.Debug.WriteLine ($"Screen DPI: {Desktop.DpiX}x{Desktop.DpiY}");
        }

        public void AddImage (BitmapSource image, string name)
        {
            if (!HasImage (name))
            {
                _overlayHandler.AddImage (image, name);
                RefreshLayersList();
                OnLayersChanged();
            }
        }

        public bool HasImage (string name)
        {
            return _overlayHandler.GetImages().Any (img => img.Name == name);
        }

        public int GetLayerCount ()
        {
            return _overlayHandler.GetImages().Count;
        }

        public void Clear ()
        {
            _overlayHandler.Clear();
            _layers.Clear();
            OnLayersChanged();
        }

        public BitmapSource GetCompositeBitmap ()
        {
            return _overlayHandler.GetCompositeBitmap();
        }

        public void SetPreviewImage (BitmapSource image, string name)
        {
            _overlayHandler.SetPreviewImage (image, name);
        }

        public void ClearPreview ()
        {
            _overlayHandler.ClearPreview();
        }

        private void RefreshLayersList ()
        {
            var selectedIndex = LayersList.SelectedIndex;
            _layers.Clear();
            var images = _overlayHandler.GetImages();
            foreach (var img in images.OrderByDescending (i => i.ZIndex))
                _layers.Add (new OverlayImageViewModel (img));

            if (selectedIndex >= 0 && selectedIndex < _layers.Count)
                LayersList.SelectedIndex = selectedIndex;
        }

        public void ApplyScaling (bool downscale)
        {
            if (downscale)
            {
                OverlayScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                OverlayScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

                OverlayViewbox.Stretch = Stretch.Uniform;
                OverlayViewbox.StretchDirection = StretchDirection.DownOnly;
                OverlayViewbox.HorizontalAlignment = HorizontalAlignment.Center;
                OverlayViewbox.VerticalAlignment = VerticalAlignment.Center;
            }
            else
            {
                OverlayScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                OverlayScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

                OverlayViewbox.Stretch = Stretch.None;
                OverlayViewbox.StretchDirection = StretchDirection.Both;
                OverlayViewbox.HorizontalAlignment = HorizontalAlignment.Center;
                OverlayViewbox.VerticalAlignment = VerticalAlignment.Center;
            }

            // Force re-render of the composite
            if (_overlayHandler != null)
            {
                _overlayHandler.UpdateComposite();
            }
        }

        private void OverlayScrollViewer_SizeChanged (object sender, SizeChangedEventArgs e)
        {
            UpdateScaling();
        }

        public void UpdateScaling ()
        {
            ApplyScaling (MainWindow.DownScaleImage.Get<bool>());
            //ImageSizeDiagnostics();
        }

        private void LayersList_SelectionChanged (object sender, SelectionChangedEventArgs e)
        {
            var selected = LayersList.SelectedItem as OverlayImageViewModel;
            if (selected != null)
                OpacitySlider.Value = selected.Model.Opacity;
        }

        private void LayerVisibility_Changed (object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            var dc = checkBox?.DataContext as OverlayImageViewModel;
            if (dc != null)
                _overlayHandler.SetImageVisibility (dc.Model.ZIndex, dc.IsVisible);
        }

        private void OpacitySlider_ValueChanged (object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var selected = LayersList.SelectedItem as OverlayImageViewModel;
            if (selected != null)
            {
                _overlayHandler.SetImageOpacity (selected.Model.ZIndex, e.NewValue);
                selected.Model.Opacity = e.NewValue;
            }
        }

        private void MoveUp_Click (object sender, RoutedEventArgs e)
        {
            var selected = LayersList.SelectedItem as OverlayImageViewModel;
            if (selected != null)
            {
                var currentIndex = _layers.IndexOf (selected);
                if (currentIndex > 0)
                {
                    _overlayHandler.MoveImageUp (selected.Model.ZIndex);
                    RefreshLayersList();
                    LayersList.SelectedIndex = currentIndex - 1;
                    //OnLayersChanged();
                }
            }
        }

        private void MoveDown_Click (object sender, RoutedEventArgs e)
        {
            var selected = LayersList.SelectedItem as OverlayImageViewModel;
            if (selected != null)
            {
                var currentIndex = _layers.IndexOf (selected);
                if (currentIndex < _layers.Count - 1)
                {
                    _overlayHandler.MoveImageDown (selected.Model.ZIndex);
                    RefreshLayersList();
                    LayersList.SelectedIndex = currentIndex + 1;
                    //OnLayersChanged();
                }
            }
        }

        private void Remove_Click (object sender, RoutedEventArgs e)
        {
            var selected = LayersList.SelectedItem as OverlayImageViewModel;
            if (selected != null)
            {
                _overlayHandler.RemoveImage (selected.Model.ZIndex);
                RefreshLayersList();
                OnLayersChanged();
            }
        }

        // Drag and drop between layers
        private void LayersList_PreviewMouseLeftButtonDown (object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition (null);
            _draggedItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        }

        private void LayersList_PreviewMouseMove (object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point position = e.GetPosition (null);
                if (Math.Abs (position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs (position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (_draggedItem != null)
                    {
                        _isDragging = true;
                        ClearPreview(); // Clear preview when starting drag
                        var data = new DataObject ("LayerItem", _draggedItem.DataContext);
                        DragDrop.DoDragDrop (_draggedItem, data, DragDropEffects.Move);
                        _isDragging = false;
                    }
                }
            }
        }

        private void LayersList_DragEnter(object sender, DragEventArgs e)
        {
            if (!_isExternalDragInProgress)
                return;

            if (e.Data.GetDataPresent(typeof(EntryViewModel)) ||
                e.Data.GetDataPresent("EntryViewModelArray"))
            {
                e.Effects = DragDropEffects.Copy;
                // Keep hint visible
                DropHint.Visibility = Visibility.Visible;
            }
            e.Handled = true;
        }

        private void LayersList_Drop (object sender, DragEventArgs e)
        {
            HideDropHint(); // Hide after drop

            if (e.Data.GetDataPresent ("LayerItem"))
            {
                var droppedData = e.Data.GetData ("LayerItem") as OverlayImageViewModel;
                if (droppedData == null) return;

                ClearPreview();

                var target = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);

                if (target != null)
                {
                    // Dropping on an existing item
                    var targetData = target.DataContext as OverlayImageViewModel;
                    if (targetData != null && droppedData != targetData)
                    {
                        _overlayHandler.ReorderLayers (droppedData.Model.ZIndex, targetData.Model.ZIndex);
                        RefreshLayersList();
                        LayersList.SelectedItem = droppedData;
                        OnLayersChanged();
                    }
                }
                else
                {
                    // Dropped outside items - determine if top or bottom
                    Point dropPosition = e.GetPosition (LayersList);

                    if (_layers.Count > 0)
                    {
                        if (dropPosition.Y < 50) // Dropped at top
                        {
                            var topLayer = _layers.First();
                            if (topLayer != droppedData)
                            {
                                _overlayHandler.MoveToTop (droppedData.Model.ZIndex);
                                RefreshLayersList();
                                LayersList.SelectedItem = droppedData;
                                OnLayersChanged();
                            }
                        }
                        else // Dropped at bottom
                        {
                            var bottomLayer = _layers.Last();
                            if (bottomLayer != droppedData)
                            {
                                _overlayHandler.MoveToBottom (droppedData.Model.ZIndex);
                                RefreshLayersList();
                                LayersList.SelectedItem = droppedData;
                                OnLayersChanged(); // Raise event
                            }
                        }
                    }
                }
            }

            // Handle drops from ListView (EntryViewModel)
            else if (e.Data.GetDataPresent (typeof (EntryViewModel)))
            {
                HandleEntryDrop (e.Data.GetData (typeof (EntryViewModel)) as EntryViewModel);
            }

            // Handle multiple entries
            else if (e.Data.GetDataPresent ("EntryViewModelArray"))
            {
                var entries = e.Data.GetData ("EntryViewModelArray") as EntryViewModel[];
                if (entries != null)
                {
                    foreach (var entry in entries.Where (item => item.Type == "image"))
                        HandleEntryDrop (entry);
                }
            }

            if (_dropIndicator != null)
            {
                _dropIndicator.Visibility = Visibility.Collapsed;
                _lastIndicatorY = -1;
            }

            OnLayersChanged(); // Raise event after all drops
        }

        private void LayersList_DragOver (object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent ("LayerItem"))
            {
                e.Effects = DragDropEffects.Move;

                // Show drop indicator
                Point dropPos = e.GetPosition (LayersList);
                UpdateDropIndicator (dropPos);
            }
            else if (e.Data.GetDataPresent (typeof (EntryViewModel)) ||
                     e.Data.GetDataPresent ("EntryViewModelArray"))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private Line _dropIndicator;
        private double _lastIndicatorY = -1;

        private void UpdateDropIndicator (Point dropPosition)
        {
            if (_dropIndicator == null)
            {
                _dropIndicator = new Line
                {
                    Stroke = new SolidColorBrush (Colors.Blue),
                    StrokeThickness = 2,
                    IsHitTestVisible = false
                };

                var grid = FindAncestor<Grid>(LayersList);
                if (grid != null && !grid.Children.Contains (_dropIndicator))
                {
                    grid.Children.Add (_dropIndicator);
                    Grid.SetRow (_dropIndicator, Grid.GetRow (LayersList));
                    Panel.SetZIndex (_dropIndicator, 1000);
                }
            }

            double newIndicatorY = -1;

            // Find which item we're hovering over
            var hitTest = VisualTreeHelper.HitTest (LayersList, dropPosition);
            if (hitTest != null)
            {
                var listBoxItem = FindAncestor<ListBoxItem>(hitTest.VisualHit);
                if (listBoxItem != null)
                {
                    var itemBounds = listBoxItem.TransformToAncestor (LayersList)
                                               .TransformBounds (new Rect (0, 0, listBoxItem.ActualWidth, listBoxItem.ActualHeight));

                    // Determine if we're in top or bottom half
                    bool isTopHalf = dropPosition.Y < itemBounds.Top + itemBounds.Height / 2;

                    if (isTopHalf)
                        newIndicatorY = itemBounds.Top;
                    else
                        newIndicatorY = itemBounds.Bottom;
                }
                else
                {
                    // Not over an item - show at top or bottom
                    if (dropPosition.Y < 50)
                        newIndicatorY = 0;
                    else
                        newIndicatorY = LayersList.ActualHeight;
                }
            }

            // Only update if position changed
            if (Math.Abs (newIndicatorY - _lastIndicatorY) > 0.1)
            {
                _lastIndicatorY = newIndicatorY;
                _dropIndicator.X1 = 5;
                _dropIndicator.X2 = LayersList.ActualWidth - 5;
                _dropIndicator.Y1 = _dropIndicator.Y2 = newIndicatorY;
                _dropIndicator.Visibility = Visibility.Visible;
            }
        }

        private void LayerPanel_Drop (object sender, DragEventArgs e)
        {
            HideDropHint();

            if (e.Data.GetDataPresent (typeof (EntryViewModel)))
            {
                HandleEntryDrop (e.Data.GetData (typeof (EntryViewModel)) as EntryViewModel);
            }
            else if (e.Data.GetDataPresent ("EntryViewModelArray"))
            {
                var entries = e.Data.GetData ("EntryViewModelArray") as EntryViewModel[];
                if (entries != null)
                {
                    foreach (var entry in entries.Where (a => a.Type == "image"))
                        HandleEntryDrop (entry);
                }
            }

            //OnLayersChanged();
        }

        private void LayersList_DragLeave (object sender, DragEventArgs e)
        {
            if (_dropIndicator != null)
            {
                _dropIndicator.Visibility = Visibility.Collapsed;
                _lastIndicatorY = -1;
            }
            // Don't hide hint on drag leave
        }

        private void LayerPanel_DragOver (object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent (typeof (EntryViewModel)) ||
                e.Data.GetDataPresent ("EntryViewModelArray"))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void LayerPanel_DragLeave (object sender, DragEventArgs e)
        {
            // Don't hide hint here either
        }

        private async void HandleEntryDrop (EntryViewModel entry)
        {
            if (entry == null || entry.Type != "image")
                return;

            if (HasImage (entry.Name))
                return;

            try
            {
                // Create a simple async loader for overlay
                await Task.Run(() =>
                {
                    using (var data = VFS.OpenImage (entry.Source))
                    {
                        var bitmap = data.Image.Bitmap;
                        if (!bitmap.IsFrozen)
                            bitmap.Freeze();
                        
                        Dispatcher.Invoke(() => {
                            AddImage (bitmap, entry.Name);
                            // OnLayersChanged will be called inside AddImage
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine ($"Failed to load {entry.Name}: {ex.Message}");
            }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T)
                    return (T)current;

                current = VisualTreeHelper.GetParent (current);
            }
            return null;
        }
    }

    public class OverlayImageViewModel : INotifyPropertyChanged
    {
        public OverlayImage Model { get; }

        public OverlayImageViewModel (OverlayImage model)
        {
            Model = model;
        }

        public string Name => Model.Name;

        public bool IsVisible
        {
            get => Model.IsVisible;
            set
            {
                if (Model.IsVisible != value)
                {
                    Model.IsVisible = value;
                    OnPropertyChanged ("IsVisible");
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged (string propertyName)
        {
            PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (propertyName));
        }
    }
}