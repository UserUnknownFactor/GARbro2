using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.ComponentModel;
using GARbro.GUI.Preview;
using GameRes;
using System.Threading;
using System.Text;

namespace GARbro.GUI
{
    public class SequentialPreviewLoader
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim (1, 1);
        private CancellationTokenSource _activeCts;
        private CancellationTokenSource _latestCts;
        private readonly object _lock = new object();

        public async Task<T> LoadAsync<T> (
            Func<CancellationToken, Task<T>> loadFunc,
            CancellationToken cancellationToken = default)
        {
            CancellationTokenSource myCts;

            lock (_lock)
            {
                if (_latestCts != null && _latestCts != _activeCts)
                    _latestCts.Cancel();

                myCts = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
                _latestCts = myCts;
                _activeCts?.Cancel();
            }

            await _semaphore.WaitAsync (myCts.Token);
            try
            {
                lock (_lock)
                {
                    if (_latestCts != myCts)
                        throw new OperationCanceledException();

                    _activeCts = myCts;
                }

                return await loadFunc (myCts.Token);
            }
            finally
            {
                lock (_lock)
                {
                    if (_activeCts == myCts) _activeCts = null;
                    if (_latestCts == myCts) _latestCts = null;
                }

                _semaphore.Release();
            }
        }

        public Task LoadAsync (Func<CancellationToken, Task> loadFunc, CancellationToken cancellationToken = default)
        {
            return LoadAsync<object>(async ct => { await loadFunc (ct); return null; }, cancellationToken);
        }

        public void CancelCurrent ()
        {
            lock (_lock)
            {
                _activeCts?.Cancel();
                _latestCts?.Cancel();
            }
        }
    }

    public partial class MainWindow
    {
        private PreviewFile m_current_preview = new PreviewFile();
        internal readonly SequentialPreviewLoader _previewLoader = new SequentialPreviewLoader();

        internal ImagePreviewHandler _imagePreviewHandler;
        internal AudioPreviewHandler _audioPreviewHandler;
        internal VideoPreviewHandler _videoPreviewHandler;
        internal TextPreviewHandler  _textPreviewHandler;
        internal ImageOverlayControl _overlayControl;
        internal ModelPreviewHandler _modelPreviewHandler;

        internal PreviewStateMachine _previewStateMachine;

        internal ImagePreviewControl m_animated_image_viewer;
        internal VideoPreviewControl m_video_preview_ctl;

        private void InitializePreviewSystem ()
        {
            m_animated_image_viewer = new ImagePreviewControl();
            m_animated_image_viewer.Stretch = ImageCanvas.Stretch;
            m_animated_image_viewer.StretchDirection = ImageCanvas.StretchDirection;

            m_video_preview_ctl = new VideoPreviewControl (this);
            m_video_preview_ctl.StatusChanged   += (status) => SetPreviewStatus (status);
            m_video_preview_ctl.PositionChanged += (pos, dur) => UpdateVideoPosition (pos, dur);
            m_video_preview_ctl.MediaEnded += () => _previewStateMachine.OnMediaEnded();
            m_video_preview_ctl.PlaybackStateChanged += (isPlaying) => _previewStateMachine.OnPlaybackStateChanged (isPlaying);

            PreviewPane.Children.Add (m_animated_image_viewer);
            PreviewPane.Children.Add (m_video_preview_ctl);

            TextView.IsWordWrapEnabled = true;

            _modelPreviewHandler = new ModelPreviewHandler (this);

            _imagePreviewHandler = new ImagePreviewHandler (this, ImageCanvas, m_animated_image_viewer);
            _audioPreviewHandler = new AudioPreviewHandler (this);
            _videoPreviewHandler = new VideoPreviewHandler (this, m_video_preview_ctl);
            _textPreviewHandler  = new TextPreviewHandler (this);
            _overlayControl      = new ImageOverlayControl();

            _overlayControl.LayersChanged    += (sender, e) => UpdateOverlayStatus();
            _overlayControl.AllLayersCleared += (sender, e) => RestorePreviewAfterClear();

            PreviewPane.Children.Add (_overlayControl);
            Panel.SetZIndex (_overlayControl, 90);

            _previewStateMachine = new PreviewStateMachine (this);
        }

        private void CancelAllPreviewOperations ()
        {
            _previewLoader.CancelCurrent();

            _imagePreviewHandler?.Reset();
            _textPreviewHandler?.Reset();
            //CancelAllManualPreviewOperations();
        }

        private void CancelAllManualPreviewOperations ()
        {
            _audioPreviewHandler?.Reset();
            _videoPreviewHandler?.Reset();
            _modelPreviewHandler?.Reset();
        }

        private void DisposePreviewHandlers ()
        {
            _imagePreviewHandler?.Dispose();
            _audioPreviewHandler?.Dispose();
            _videoPreviewHandler?.Dispose();
            _textPreviewHandler?.Dispose();
            _modelPreviewHandler?.Dispose();
        }

        internal void PreviewEntry (Entry entry)
        {
            if (m_current_preview.IsEqual (ViewModel.Path, entry) && entry.Type != "video")
                return;
            UpdatePreviewPane (entry);
        }

        private void RestorePreviewAfterClear()
        {
            if (_overlayControl.Visibility == Visibility.Visible)
            {
                var selected = CurrentDirectory.SelectedItems;
                if (selected.Count == 1)
                {
                    var entry = selected[0] as EntryViewModel;
                    if (entry != null && entry.Type == "image")
                    {
                        try
                        {
                            using (var data = VFS.OpenImage (entry.Source))
                            {
                                _overlayControl.SetPreviewImage (data.Image.Bitmap, entry.Name);
                            }
                        }
                        catch
                        {
                            _overlayControl.ClearPreview();
                        }
                    }
                }
            }
        }

        private void RefreshPreviewPane()
        {
            var current = CurrentDirectory.SelectedItem as EntryViewModel;
            if (null != current)
                UpdatePreviewPane (current.Source);
            else
                ResetPreviewPane();
        }

        private void ResetPreviewPane()
        {
            HideAllPreviewControls();
            SetFileStatus ("");
            SetPreviewStatus ("");
            _previewStateMachine.ResetToDefault();
        }

        private bool IsPreviewPossible (Entry entry)
        {
            if (_modelPreviewHandler != null && _modelPreviewHandler.IsModelFile (entry))
                return true;

            return "image" == entry.Type || "script" == entry.Type || "text" == entry.Type ||
                "config" == entry.Type || // not audio/video since they're big and slow to autoload
                (string.IsNullOrEmpty (entry.Type) && entry.Size < 0x100000);
        }

        private string GetEffectiveEntryType (Entry entry)
        {
            if (_modelPreviewHandler != null && _modelPreviewHandler.IsModelFile (entry))
                return "model";

            return entry.Type;
        }

        private void UpdatePreviewPane(Entry entry)
        {
            var vm = ViewModel;
            var previousPreview = m_current_preview;
            m_current_preview = new PreviewFile { Path = vm.Path, Name = entry.Name, Entry = entry, TempFile = null };

            RemoveGridOverlay();
            SetPreviewStatus ("");

            System.Text.Encoding preferredEnc = null;

            // Check if this is a text-like file
            bool isTextFile = entry.Type == "script" || entry.Type == "text" || entry.Type == "config" ||
                              (string.IsNullOrEmpty(entry.Type) && entry.Size < 0x100000);

            if (isTextFile)
            {
                if (_isManualEncodingChange)
                {
                    // User manually changed encoding - this is handled in OnEncodingSelect
                    preferredEnc = EncodingChoice.SelectedItem as Encoding;
                }
                else
                {
                    // Use the priority logic from GetPreferredEncoding
                    preferredEnc = GetPreferredEncoding(m_current_preview);
                }
            }

            m_current_preview.PreferredEncoding = preferredEnc;

            if (!IsPreviewPossible (entry))
            {
                ResetPreviewPane();
                return;
            }

            var entryType = GetEffectiveEntryType (entry);
            if (_previewStateMachine.CurrentMediaType == MediaType.Sprite && entryType == "image")
                LoadPreviewContent (m_current_preview);
            else
                _previewStateMachine.TransitionToMedia (entryType, () => LoadPreviewContent (m_current_preview));
        }

        private async void LoadPreviewContent (PreviewFile pf)
        {
            try
            {
                var eType = pf.Entry.Type;
                if (_modelPreviewHandler != null && _modelPreviewHandler.IsModelFile (pf.Entry))
                {
                    HideAllPreviewControls();
                    SetFileStatus (Localization.Format ("LoadingFile", Localization._T ($"Type_{eType}")));

                    await _previewLoader.LoadAsync (async ct => {
                        await _modelPreviewHandler.LoadContentAsync (m_current_preview, ct);
                        SetFileStatus ("");
                    });
                }
                else if ("video" == eType)
                {
                    ShowVideoPreview();
                    SetFileStatus (Localization.Format ("LoadingFile", Localization._T ($"Type_{eType}")));

                    await _previewLoader.LoadAsync (async ct => {
                        await _videoPreviewHandler.LoadContentAsync (m_current_preview, ct);
                    });
                }
                else if ("audio" == eType)
                {
                    SetFileStatus (Localization.Format ("LoadingFile", Localization._T ($"Type_{eType}")));

                    await _previewLoader.LoadAsync (async ct => {
                        await _audioPreviewHandler.LoadContentAsync (m_current_preview, ct);
                        SetFileStatus ("");
                    });
                }
                else if ("image" == eType)
                {
                    ShowImagePreview();

                    await _previewLoader.LoadAsync (async ct => {
                        await _imagePreviewHandler.LoadContentAsync (m_current_preview, ct);
                    });
                }
                else // text, script, or unknown
                {
                    //_imagePreviewHandler.Reset();
                    ShowTextPreview();

                    await _previewLoader.LoadAsync (async ct => {
                        await _textPreviewHandler.LoadContentAsync (m_current_preview, ct);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Normal - user navigated away
                SetFileStatus ("");
            }
            catch (Exception ex)
            {
                SetFileStatus (ex.Message);
                ResetPreviewPane();
            }
        }

        // Fix the other LoadPreviewImageAsync method
        private async Task LoadPreviewImageAsync (PreviewFile preview)
        {
            try
            {
                await _previewLoader.LoadAsync (async ct =>
                {
                    await _imagePreviewHandler.LoadContentAsync (preview, ct);
                });
            }
            catch (OperationCanceledException)
            {
                // Normal - user navigated away
            }
            catch (Exception ex)
            {
                SetFileStatus (Localization.Format ("ImagePreviewError", ex.Message));
                _imagePreviewHandler.Reset();
                //SetFileStatus (Localization._T (ex.Message));
            }
        }

        internal void ApplyDownScaleSetting ()
        {
            bool image_need_scale = DownScaleImage.Get<bool>();

            if (image_need_scale)
            {
                if (ImageCanvas.Source != null)
                {
                    var image = ImageCanvas.Source;
                    double displayWidth = image.Width;
                    double displayHeight = image.Height;

                    if (ImageCanvas.LayoutTransform is ScaleTransform scale)
                    {
                        displayWidth *= scale.ScaleX;
                        displayHeight *= scale.ScaleY;
                    }

                    image_need_scale = displayWidth > ImageView.ActualWidth || displayHeight > ImageView.ActualHeight;
                }
                else if (m_animated_image_viewer.Source != null)
                {
                    var image = m_animated_image_viewer.Source;
                    double displayWidth = image.Width;
                    double displayHeight = image.Height;

                    if (m_animated_image_viewer.LayoutTransform is ScaleTransform scale)
                    {
                        displayWidth *= scale.ScaleX;
                        displayHeight *= scale.ScaleY;
                    }

                    image_need_scale = displayWidth > ImageView.ActualWidth || displayHeight > ImageView.ActualHeight;
                }
                if (_overlayControl != null && _overlayControl.Visibility == Visibility.Visible)
                {
                    _overlayControl.UpdateScaling();
                }
            }

            SetImageScaleMode (image_need_scale);
            RedrawGridIfExists();
        }

        internal void ApplyScalingToAnimatedViewer ()
        {
            bool downscale_enabled = DownScaleImage.Get<bool>();

            if (downscale_enabled && m_animated_image_viewer.Source != null)
            {
                var image = m_animated_image_viewer.Source;

                bool image_is_larger = image.Width > ImageView.ActualWidth ||
                                      image.Height > ImageView.ActualHeight;

                if (image_is_larger)
                {
                    // Downscale large images
                    m_animated_image_viewer.Stretch = Stretch.Uniform;
                    RenderOptions.SetBitmapScalingMode (m_animated_image_viewer, BitmapScalingMode.HighQuality);
                }
                else
                {
                    // Keep small images at original size
                    m_animated_image_viewer.Stretch = Stretch.None;
                    RenderOptions.SetBitmapScalingMode (m_animated_image_viewer, BitmapScalingMode.NearestNeighbor);
                }
            }
            else
            {
                m_animated_image_viewer.Stretch = Stretch.None;
                RenderOptions.SetBitmapScalingMode (m_animated_image_viewer, BitmapScalingMode.NearestNeighbor);
            }
        }

        private void SetImageScaleMode (bool scale)
        {
            if (scale)
            {
                // Only apply Uniform stretch to images larger than view
                ImageCanvas.Stretch = Stretch.Uniform;
                RenderOptions.SetBitmapScalingMode (ImageCanvas, BitmapScalingMode.HighQuality);
                ImageView.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                ImageView.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

                m_animated_image_viewer.Stretch = Stretch.Uniform;
                RenderOptions.SetBitmapScalingMode (m_animated_image_viewer, BitmapScalingMode.HighQuality);
            }
            else
            {
                // No scaling - show at original size
                ImageCanvas.Stretch = Stretch.None;
                RenderOptions.SetBitmapScalingMode (ImageCanvas, BitmapScalingMode.NearestNeighbor);
                ImageView.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                ImageView.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;

                m_animated_image_viewer.Stretch = Stretch.None;
                RenderOptions.SetBitmapScalingMode (m_animated_image_viewer, BitmapScalingMode.NearestNeighbor);
            }
        }

        private void RedrawGridIfExists ()
        {
            if (_gridOverlay != null && SpriteLayoutCombo.SelectedIndex == 2)
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

        private void PreviewSizeChanged (object sender, SizeChangedEventArgs e)
        {
            ImageSource image = null;
            if (ImageCanvas.Visibility == Visibility.Visible)
                image = ImageCanvas.Source;
            else if (m_animated_image_viewer.Visibility == Visibility.Visible)
                image = m_animated_image_viewer.Source;

            if (null == image)
                return;

            ApplyDownScaleSetting();
        }

        private void OnEntrySelectionChanged (object sender, SelectionChangedEventArgs e)
        {
            if (m_animated_image_viewer != null)
                m_animated_image_viewer.StopAnimation();

            if (m_video_preview_ctl != null)
                m_video_preview_ctl.Stop();

            RemoveGridOverlay();

            if (_overlayControl != null && _overlayControl.Visibility == Visibility.Visible)
            {
                var selected = CurrentDirectory.SelectedItems;

                if (selected.Count == 1)
                {
                    var entry = selected[0] as EntryViewModel;
                    if (entry != null && entry.Type == "image")
                    {
                        // Only show preview if image is not already in layers
                        if (!_overlayControl.HasImage (entry.Name))
                        {
                            try
                            {
                                using (var data = VFS.OpenImage (entry.Source))
                                {
                                    _overlayControl.SetPreviewImage (data.Image.Bitmap, entry.Name);
                                }
                            }
                            catch
                            {
                                _overlayControl.ClearPreview();
                            }
                        }
                        else
                            _overlayControl.ClearPreview();
                    }
                    else
                        _overlayControl.ClearPreview();
                }
                else
                    _overlayControl.ClearPreview();
            }
        }

        // UI Element management methods used by PreviewStateMachine
        internal void ShowImagePreview ()
        {
            m_animated_image_viewer.Visibility = Visibility.Collapsed;
            if (m_video_preview_ctl != null)
                m_video_preview_ctl.Visibility = Visibility.Collapsed;
            ImageCanvas.Visibility = Visibility.Visible;
            TextView.Visibility = Visibility.Collapsed;
        }

        internal void ShowAnimatedImagePreview ()
        {
            ImageCanvas.Visibility = Visibility.Collapsed;
            if (m_video_preview_ctl != null)
                m_video_preview_ctl.Visibility = Visibility.Collapsed;
            TextView.Visibility = Visibility.Collapsed;
            m_animated_image_viewer.Visibility = Visibility.Visible;
        }

        internal void ShowVideoPreview ()
        {
            ImageCanvas.Visibility = Visibility.Collapsed;
            m_animated_image_viewer.Visibility = Visibility.Collapsed;
            TextView.Visibility = Visibility.Collapsed;
            m_video_preview_ctl.Visibility = Visibility.Visible;
        }

        internal void ShowTextPreview ()
        {
            ImageCanvas.Visibility = Visibility.Collapsed;
            m_animated_image_viewer.Visibility = Visibility.Collapsed;
            if (m_video_preview_ctl != null)
                m_video_preview_ctl.Visibility = Visibility.Collapsed;
            TextView.Visibility = Visibility.Visible;
        }

        public void HideAllPreviewControls ()
        {
            HideAnimatedImagePreview();
            HideImagePreview();
            HideVideoPreview();
            HideTextPreview();

            if (_overlayControl != null)
                _overlayControl.Visibility = Visibility.Collapsed;
        }

        internal void HideImagePreview ()
        {
            ImageCanvas.Visibility = Visibility.Collapsed;
        }

        internal void HideAnimatedImagePreview ()
        {
            m_animated_image_viewer.Visibility = Visibility.Collapsed;
        }

        internal void HideVideoPreview ()
        {
            m_video_preview_ctl.Visibility = Visibility.Collapsed;
        }

        internal void HideTextPreview ()
        {
            TextView.Visibility = Visibility.Collapsed;
        }
    }
}