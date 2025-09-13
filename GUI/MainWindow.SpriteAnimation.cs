using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GARbro.GUI.Properties;
using GameRes;

namespace GARbro.GUI
{
    public partial class MainWindow
    {
        internal SpriteSheetAnimator _spriteAnimator;
        internal                 bool _isSpriteAnimating = false;
        private               Canvas _gridCanvas = null;
        private     FrameworkElement _gridOverlay = null;

        private void InitializeSpriteAnimation ()
        {
        }

        internal void ShowSpriteSheetControls ()
        {
            SpriteSheetPanel.Visibility = Visibility.Visible;

            FrameDelayBox.Text    = Settings.Default.SpriteSheetDelay.ToString();
            SpriteColumnsBox.Text = Settings.Default.FrameGridColumns.ToString();
            SpriteRowsBox.Text    = Settings.Default.FrameGridRows.ToString();

            // re-trigger the selection to apply current mode for relevant controls
            var currentIndex = SpriteLayoutCombo.SelectedIndex;
            SpriteLayoutCombo.SelectedIndex = -1;
            SpriteLayoutCombo.SelectedIndex = currentIndex;
        }


        internal void OnSpriteModeSwitched (SpriteMode previousMode, SpriteMode newMode)
        {
            switch (previousMode)
            {
            case SpriteMode.Grid:
                StopSpriteAnimation();
                RemoveGridOverlay();
                break;
            case SpriteMode.Overlay:
                HideOverlayEditor();
                break;
            }

            switch (newMode)
            {
            case SpriteMode.Static:
                SetupStaticMode();
                break;
            case SpriteMode.Grid:
                SetupGridMode();
                break;
            case SpriteMode.Overlay:
                SetupOverlayMode();
                break;
            }
        }

        private void SetupStaticMode()
        {
            SpriteConfigPanel.Visibility = Visibility.Collapsed;
            MediaControlPanel.Visibility = Visibility.Collapsed;

            ResetToStaticImage();
        }

        private void SetupGridMode()
        {
            SpriteConfigPanel.Visibility = Visibility.Visible;

            // Show grid controls
            GridSizeTextBlock.Visibility  = Visibility.Visible;
            SpriteColumnsBox.Visibility   = Visibility.Visible;
            GridXTextBlock.Visibility     = Visibility.Visible;
            SpriteRowsBox.Visibility      = Visibility.Visible;
            DelayControlsPanel.Visibility = Visibility.Visible;
            AutoDetectButton.Visibility   = Visibility.Visible;

            // Hide overlay controls
            OverlayPanel.Visibility = Visibility.Collapsed;

            EnsureImageLoadedForGrid();

            if (_mediaControl != null)
                _mediaControl.UpdateSpriteButtons (false);
        }

        private void SetupOverlayMode()
        {
            SpriteConfigPanel.Visibility = Visibility.Visible;

            // Hide grid controls
            GridSizeTextBlock.Visibility  = Visibility.Collapsed;
            SpriteColumnsBox.Visibility   = Visibility.Collapsed;
            GridXTextBlock.Visibility     = Visibility.Collapsed;
            SpriteRowsBox.Visibility      = Visibility.Collapsed;
            DelayControlsPanel.Visibility = Visibility.Collapsed;
            AutoDetectButton.Visibility   = Visibility.Collapsed;

            // Show overlay controls
            OverlayPanel.Visibility       = Visibility.Visible;
            MediaControlPanel.Visibility  = Visibility.Collapsed;

            ShowOverlayEditor();
        }

        internal void HideSpriteSheetControls ()
        {
            SpriteSheetPanel.Visibility = Visibility.Collapsed;
            StopSpriteAnimation();
            RemoveGridOverlay();
            HideOverlayEditor();
        }

        private void SpriteLayoutCombo_SelectionChanged (object sender, SelectionChangedEventArgs e)
        {
            if (SpriteLayoutCombo == null || SpriteLayoutCombo.SelectedItem == null)
                return;

            SpriteMode mode;
            var selected = (ComboBoxItem)SpriteLayoutCombo.SelectedItem;
            switch (selected.Tag.ToString())
            {
            case "Static":
                mode = SpriteMode.Static;
                break;
            case "Grid":
                mode = SpriteMode.Grid;
                break;
            case "Overlay":
                mode = SpriteMode.Overlay;
                break;
            default:
                mode = SpriteMode.Static;
                break;
            }

            _previewStateMachine.TransitionToSpriteMode (mode);
        }

        private void EnsureImageLoadedForGrid()
        {
            ImageCanvas.Visibility = Visibility.Visible;
            m_animated_image_viewer.Visibility = Visibility.Collapsed;

            if (ImageCanvas.Source == null)
            {
                var currentEntry = CurrentDirectory.SelectedItem as EntryViewModel;
                if (currentEntry != null && currentEntry.Type == "image")
                {
                    PreviewEntry (currentEntry.Source);
                    return;
                }
            }

            var currentImage = ImageCanvas.Source as BitmapSource;
            if (currentImage != null)
            {
                if (_spriteAnimator == null || _spriteAnimator.Source != currentImage)
                {
                    _spriteAnimator = new SpriteSheetAnimator (currentImage);
                }
            }
        }

        private void ExtractSpriteFrames ()
        {
            if (_spriteAnimator == null || ImageCanvas.Source == null)
                return;

            var selected = (ComboBoxItem)SpriteLayoutCombo.SelectedItem;
            var layout = selected.Tag.ToString();

            if (layout == "Grid")
            {
                int columns, rows;
                if (int.TryParse (SpriteColumnsBox.Text, out columns) &&
                    int.TryParse (SpriteRowsBox.Text, out rows) &&
                    columns > 0 && rows > 0)
                {
                    _spriteAnimator.ExtractGridFrames (columns, rows);
                    Settings.Default.FrameGridColumns = columns;
                    Settings.Default.FrameGridRows = rows;
                }
            }

            if (_spriteAnimator.HasFrames)
            {
                SetPreviewStatus (Localization.Format ("SpriteSheetFrames", _spriteAnimator.Frames.Count));
                _previewStateMachine.TransitionToMedia (MediaType.Sprite, null);
            }
        }

        internal void StartSpriteAnimation ()
        {
            RemoveGridOverlay();

            if (_spriteAnimator == null || !_spriteAnimator.HasFrames)
            {
                ExtractSpriteFrames();
                if (!_spriteAnimator.HasFrames)
                    return;
            }

            int delay;
            if (!int.TryParse (FrameDelayBox.Text, out delay))
                delay = 100;

            delay = Math.Max (10, Math.Min (5000, delay));
            FrameDelayBox.Text = delay.ToString();
            Settings.Default.SpriteSheetDelay = delay;

            var delays = new List<int>();
            for (int i = 0; i < _spriteAnimator.Frames.Count; i++)
                delays.Add (delay);

            ShowAnimatedImagePreview();

            m_animated_image_viewer.LoadAnimatedImage (_spriteAnimator.Frames, delays);
            ApplyScalingToAnimatedViewer();

            _isSpriteAnimating = true;
            _mediaControl.UpdateSpriteButtons (true);
            SpriteConfigPanel.IsEnabled = false;
        }

        internal void StopSpriteAnimation (bool offGrid = false)
        {
            if (_isSpriteAnimating)
            {
                _isSpriteAnimating = false;
                m_animated_image_viewer.StopAnimation();
                m_animated_image_viewer.Visibility = Visibility.Collapsed;
                ImageCanvas.Visibility = Visibility.Visible;

                _mediaControl.UpdateSpriteButtons (false);

                if (offGrid)
                    MediaControlPanel.Visibility = Visibility.Collapsed;

                if (m_current_preview != null && m_current_preview.Entry != null)
                    UpdatePreviewPane (m_current_preview.Entry);
                SpriteConfigPanel.IsEnabled = true;
            }
        }

        internal void ToggleSpriteAnimation ()
        {
            if (!_isSpriteAnimating)
            {
                StartSpriteAnimation();
            }
            else
            {
                if (m_animated_image_viewer != null)
                {
                    if (m_animated_image_viewer.IsPaused)
                    {
                        m_animated_image_viewer.StartAnimation();
                        _mediaControl.UpdateSpriteButtons (true);
                        SpriteConfigPanel.IsEnabled = false;
                    }
                    else
                    {
                        m_animated_image_viewer.StopAnimation();
                        _mediaControl.UpdateSpriteButtons (false);
                        SpriteConfigPanel.IsEnabled = true;
                    }
                }
            }
        }

        private void ResetToStaticImage ()
        {
            StopSpriteAnimation();
            RemoveGridOverlay();
            m_animated_image_viewer.Reset();
            m_animated_image_viewer.Visibility = Visibility.Collapsed;
            ImageCanvas.Visibility = Visibility.Visible;

            if (m_current_preview != null && m_current_preview.Entry != null)
            {
                RefreshPreviewPane();
            }
            else
            {
                var currentEntry = CurrentDirectory.SelectedItem as EntryViewModel;
                if (currentEntry != null && currentEntry.Type == "image")
                {
                    PreviewEntry (currentEntry.Source);
                }
            }
        }

        private void AutoDetectButton_Click (object sender, RoutedEventArgs e)
        {
            if (_spriteAnimator == null)
                return;

            int columns, rows;
            var detectedLayout = _spriteAnimator.AutoDetectLayout (out columns, out rows);

            if (detectedLayout == SpriteSheetAnimator.SpriteLayout.Grid)
            {
                SpriteColumnsBox.Text = columns.ToString();
                SpriteRowsBox.Text = rows.ToString();

                DrawGridOverlay (columns, rows);
            }

            if (detectedLayout != SpriteSheetAnimator.SpriteLayout.Static)
            {
                ExtractSpriteFrames();

                double sourceWidth = ImageCanvas.Source.Width / columns;
                double sourceHeight = ImageCanvas.Source.Height / rows;
                SetPreviewStatus (Localization.Format ("AutoDetectedGrid", columns, rows, (uint)sourceWidth, (uint)sourceHeight));
            }
            else
                SetPreviewStatus (Localization._T ("CouldNotDetectSprite"));
        }

        private void DrawGridOverlay (int columns, int rows)
        {
            RemoveGridOverlay();

            if (ImageCanvas.Source == null || columns <= 0 || rows <= 0)
                return;

            _gridCanvas = new Canvas();
            _gridCanvas.IsHitTestVisible = false;

            double sourceWidth = ImageCanvas.Source.Width;
            double sourceHeight = ImageCanvas.Source.Height;

            double cellWidth = sourceWidth / columns;
            double cellHeight = sourceHeight / rows;

            var gridColor = new SolidColorBrush (Color.FromArgb (180, 100, 180, 255));

            // Calculate line thickness to appear as 2 pixels on screen
            double desiredScreenThickness = 2.0;
            double gridThickness = desiredScreenThickness;

            if (ImageCanvas.LayoutTransform is ScaleTransform dpiTransform)
                gridThickness /= dpiTransform.ScaleX;

            if (ImageCanvas.Stretch == Stretch.Uniform && ImageView.ActualWidth > 0 && ImageView.ActualHeight > 0)
            {
                double scaleX = ImageView.ActualWidth / sourceWidth;
                double scaleY = ImageView.ActualHeight / sourceHeight;
                double scale = Math.Min (scaleX, scaleY);

                if (scale < 1.0)
                    gridThickness /= scale;
            }

            gridThickness = Math.Max (1.0, gridThickness);

            // Draw vertical lines
            for (int i = 1; i < columns; i++)
            {
                var line = new Line
                {
                    X1 = i * cellWidth,
                    Y1 = 0,
                    X2 = i * cellWidth,
                    Y2 = sourceHeight,
                    Stroke = gridColor,
                    StrokeThickness = gridThickness,
                    SnapsToDevicePixels = true
                };
                _gridCanvas.Children.Add (line);
            }

            // Draw horizontal lines
            for (int i = 1; i < rows; i++)
            {
                var line = new Line
                {
                    X1 = 0,
                    Y1 = i * cellHeight,
                    X2 = sourceWidth,
                    Y2 = i * cellHeight,
                    Stroke = gridColor,
                    StrokeThickness = gridThickness,
                    SnapsToDevicePixels = true
                };
                _gridCanvas.Children.Add (line);
            }

            _gridCanvas.Width = sourceWidth;
            _gridCanvas.Height = sourceHeight;
            _gridCanvas.LayoutTransform = ImageCanvas.LayoutTransform;

            if (ImageCanvas.Stretch == Stretch.None)
            {
                var container = new Grid();

                container.SetValue (TouchScrolling.IsEnabledProperty, ImageCanvas.GetValue (TouchScrolling.IsEnabledProperty));
                container.SetValue (TouchScrolling.DraggingCursorProperty, ImageCanvas.GetValue (TouchScrolling.DraggingCursorProperty));
                container.Cursor = ImageCanvas.Cursor;

                ImageCanvas.SetValue (TouchScrolling.IsEnabledProperty, false);

                ImageView.Content = null;

                container.Children.Add (ImageCanvas);
                container.Children.Add (_gridCanvas);

                ImageView.Content = container;

                _gridOverlay = container;
            }
            else
            {
                var viewbox = new Viewbox
                {
                    Stretch = ImageCanvas.Stretch,
                    StretchDirection = ImageCanvas.StretchDirection,
                    HorizontalAlignment = ImageCanvas.HorizontalAlignment,
                    VerticalAlignment = ImageCanvas.VerticalAlignment,
                    Child = _gridCanvas,
                    IsHitTestVisible = false
                };

                PreviewPane.Children.Add (viewbox);
                Panel.SetZIndex (viewbox, 10);

                _gridOverlay = viewbox;
            }
        }

        internal void RemoveGridOverlay()
        {
            if (_gridOverlay != null)
            {
                if (_gridOverlay is Grid container && container.Parent == ImageView)
                {
                    // Restore TouchScrolling to ImageCanvas
                    ImageCanvas.SetValue (TouchScrolling.IsEnabledProperty, true);
                    ImageCanvas.SetValue (TouchScrolling.DraggingCursorProperty, Cursors.Hand);

                    // Restore ImageCanvas as direct content of ScrollViewer
                    container.Children.Remove (ImageCanvas);
                    ImageView.Content = ImageCanvas;
                }
                else if (_gridOverlay.Parent is Panel parent)
                    parent.Children.Remove (_gridOverlay);

                _gridOverlay = null;
                _gridCanvas = null;
            }
        }

        private void SpriteConfig_Changed (object sender, RoutedEventArgs e)
        {
            ExtractSpriteFrames();

            if (sender == SpriteColumnsBox || sender == SpriteRowsBox)
            {
                int columns, rows;
                if (int.TryParse (SpriteColumnsBox.Text, out columns) &&
                    int.TryParse (SpriteRowsBox.Text, out rows) &&
                    columns > 0 && rows > 0)
                {
                    DrawGridOverlay (columns, rows);
                }
            }
        }

        private void NumberInput_PreviewTextInput (object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit (c))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private void ShowOverlayEditor ()
        {
            if (_overlayControl == null) return;

            MediaControlPanel.Visibility = Visibility.Collapsed;

            _overlayControl.ClearPreview();

            var currentEntry = CurrentDirectory.SelectedItem as EntryViewModel;
            if (currentEntry != null && currentEntry.Type == "image" && CurrentDirectory.SelectedItems.Count == 1)
            {
                try
                {
                    using (var data = VFS.OpenImage (currentEntry.Source))
                    {
                        var bitmap = data.Image.Bitmap;
                        if (!_overlayControl.HasImage (currentEntry.Name))
                        {
                            _overlayControl.Clear();
                            _overlayControl.AddImage (bitmap, currentEntry.Name);
                        }
                        UpdateOverlayStatus();
                    }
                }
                catch (Exception ex)
                {
                    SetFileStatus (Localization.Format ("LoadingFailure", currentEntry.Name, ex.Message));
                }
            }

            HideImagePreview();
            HideAnimatedImagePreview();
            _overlayControl.Visibility = Visibility.Visible;

            CurrentDirectory.IsDragDropEnabled = true;
            _overlayControl.UpdateLayout();
            _overlayControl.UpdateScaling();
        }

        private void HideOverlayEditor ()
        {
            if (_overlayControl != null)
                _overlayControl.Visibility = Visibility.Collapsed;
            if (OverlayPanel != null)
                OverlayPanel.Visibility = Visibility.Collapsed;
            CurrentDirectory.IsDragDropEnabled = false;
        }

        private void AddSelectedButton_Click (object sender, RoutedEventArgs e)
        {
            if (_overlayControl == null) return;

            var selected = CurrentDirectory.SelectedItems.Cast<EntryViewModel>()
                .Where (entry => entry.Type == "image")
                .ToList();

            if (!selected.Any())
            {
                SetFileStatus (Localization._T ("NoImagesSelected"));
                return;
            }

            foreach (var entry in selected)
            {
                try
                {
                    using (var data = VFS.OpenImage (entry.Source))
                    {
                        if (!_overlayControl.HasImage (entry.Name))
                            _overlayControl.AddImage (data.Image.Bitmap, entry.Name);
                    }
                }
                catch (Exception ex)
                {
                    SetFileStatus (Localization.Format ("LoadingFailure", entry.Name, ex.Message));
                }
            }

            UpdateOverlayStatus();
        }

        private void ClearOverlayButton_Click (object sender, RoutedEventArgs e)
        {
            _overlayControl?.Clear();
            UpdateOverlayStatus();
        }

        private string BuildOverlayFileName (string baseName, int layerCount)
        {
            if (string.IsNullOrEmpty (baseName))
                return layerCount > 2 ? $"overlay_{layerCount}layers" : "overlay";

            string nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension (baseName);
            nameWithoutExt = System.Text.RegularExpressions.Regex.Replace(
                nameWithoutExt, 
                @"(_overlay|_\d+layers?|_composite)",  "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return (layerCount > 2 ? $"{nameWithoutExt}_overlay_{layerCount}layers" : $"{nameWithoutExt}_overlay");
        }

        private void ExportOverlayButton_Click (object sender, RoutedEventArgs e)
        {
            if (_overlayControl == null) return;

            var composite = _overlayControl.GetCompositeBitmap();
            if (composite == null)
            {
                SetFileStatus (Localization._T ("NoExportLayers"));
                return;
            }

            var layerCount = _overlayControl.GetLayerCount();
            var lowestLayerName = _overlayControl.GetLowestLayerName();
            string smartFileName = BuildOverlayFileName (lowestLayerName, layerCount);

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = Localization._T ("ImageSaveFilter"),
                DefaultExt = ".png",
                FileName = smartFileName
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    using (var stream = dlg.OpenFile())
                    {
                        BitmapEncoder encoder;
                        switch (System.IO.Path.GetExtension (dlg.FileName).ToLower())
                        {
                            case ".bmp":
                                encoder = new BmpBitmapEncoder();
                                break;
                            case ".jpg":
                            case ".jpeg":
                                encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                                break;
                            default:
                                encoder = new PngBitmapEncoder();
                                break;
                        }

                        encoder.Frames.Add (BitmapFrame.Create (composite));
                        encoder.Save (stream);
                    }

                    SetFileStatus (Localization.Format ("MsgExportedTo", System.IO.Path.GetFileName (dlg.FileName)));
                }
                catch (Exception ex)
                {
                    SetFileStatus (Localization.Format ("MsgExportFail", ex.Message));
                }
            }
        }

        private void UpdateOverlayStatus()
        {
            if (OverlayStatusText != null && _overlayControl != null)
            {
                var count = _overlayControl.GetLayerCount();
                OverlayStatusText.Text = count > 0 
                    ? count.Pluralize ("nth_layer")
                    : Localization._T ("SelectOverlayImages");

                SetPreviewStatus (count.Pluralize ("OverlayLayers"));
            }
        }
    }
}