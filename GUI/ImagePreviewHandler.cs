using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;
using System.Windows.Threading;
using GameRes;

namespace GARbro.GUI.Preview
{
    public class ImagePreviewHandler : PreviewHandlerBase
    {
        private readonly          MainWindow _mainWindow;
        private readonly               Image _imageCanvas;          // This is the static image viewer
        private readonly ImagePreviewControl _animatedImageViewer;  // This is for animated images
        private readonly    AsyncImageLoader _imageLoader;

        public override bool IsActive => _imageCanvas.Visibility == Visibility.Visible || 
                                         _animatedImageViewer.Visibility == Visibility.Visible;

        public ImagePreviewHandler (MainWindow mainWindow, Image imageCanvas, ImagePreviewControl animatedImageViewer)
        {
            _mainWindow = mainWindow;
            _imageCanvas = imageCanvas;
            _animatedImageViewer = animatedImageViewer;
            _imageLoader = new AsyncImageLoader();
        }

        public override async Task LoadContentAsync (PreviewFile preview, CancellationToken cancellationToken)
        {
            try
            {
                bool showLoading = ShouldShowLoadingIndicator (preview.Entry);
                if (showLoading)
                {
                    _mainWindow.SetFileStatus (Localization.Format ("LoadingFile",
                        Localization._T ($"Type_{preview.Entry.Type}")));
                }

                var result = await _imageLoader.LoadImageAsync (preview.Entry, cancellationToken);

                if (result.IsCancelled)
                    return;

                if (result.HasError)
                {
                    _mainWindow.SetFileStatus (result.Error);
                    Reset();
                    return;
                }

                await _mainWindow.Dispatcher.InvokeAsync(() =>
                {
                    if (result.IsAnimated)
                        SetAnimatedImage (preview, result);
                    else
                        SetStaticImage (preview, result);

                    _mainWindow.SetFileStatus ("");
                });
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch
            {
                Reset();
                throw;
            }
        }

        private bool ShouldShowLoadingIndicator (Entry entry)
        {
            // Show loading for GIF/WebP files larger than 3MB
            return (entry.Name.EndsWith (".gif", StringComparison.OrdinalIgnoreCase) || 
                    entry.Name.EndsWith (".webp", StringComparison.OrdinalIgnoreCase)) && 
                   entry.Size > 3_000_000;
        }

        private void SetStaticImage (PreviewFile preview, ImageLoadResult result)
        {
            _mainWindow.ShowImagePreview();
            _imageCanvas.Source = result.StaticImage;

            // Apply DPI scaling transform
            double dpiScaleX = result.StaticImage.DpiX / Desktop.DpiX;
            double dpiScaleY = result.StaticImage.DpiY / Desktop.DpiY;

            if (Math.Abs (dpiScaleX - 1.0) > 0.001 || Math.Abs (dpiScaleY - 1.0) > 0.001)
                _imageCanvas.LayoutTransform = new ScaleTransform (dpiScaleX, dpiScaleY);
            else
                _imageCanvas.LayoutTransform = Transform.Identity;

            _mainWindow.ApplyDownScaleSetting();
            _mainWindow.RemoveGridOverlay();
            _mainWindow.StopSpriteAnimation();

            _mainWindow._spriteAnimator = new SpriteSheetAnimator (result.StaticImage);

            if (_mainWindow.SpriteSheetPanel.Visibility != Visibility.Visible)
                _mainWindow.ShowSpriteSheetControls();

            // Use Tag property if available, otherwise use class name
            string formatTag = result.SourceFormat?.Tag ?? result.SourceFormat?.GetType().Name ?? "?";
            _mainWindow.SetPreviewStatus (formatTag + (result.Info?.GetComment() ?? ""));
        }

        private void SetAnimatedImage (PreviewFile preview, ImageLoadResult result)
        {
            _imageCanvas.Source = null;
            _mainWindow.ShowAnimatedImagePreview();

            if (result.AnimatedFrames.Count > 0)
            {
                var firstFrame = result.AnimatedFrames[0];
                double dpiScaleX = firstFrame.DpiX / Desktop.DpiX;
                double dpiScaleY = firstFrame.DpiY / Desktop.DpiY;

                if (Math.Abs (dpiScaleX - 1.0) > 0.001 || Math.Abs (dpiScaleY - 1.0) > 0.001)
                    _animatedImageViewer.LayoutTransform = new ScaleTransform (dpiScaleX, dpiScaleY);
                else
                    _animatedImageViewer.LayoutTransform = Transform.Identity;
            }

            _animatedImageViewer.LoadAnimatedImage (result.AnimatedFrames, result.FrameDelays);
            _mainWindow.ApplyScalingToAnimatedViewer();

            // Use Tag property if available, otherwise use class name
            string formatTag = result.SourceFormat?.Tag ?? result.SourceFormat?.GetType().Name ?? "?";
            _mainWindow.SetPreviewStatus (formatTag + (result.Info?.GetComment() ?? ""));
        }

        public override void Reset ()
        {
            _imageLoader.CancelCurrentLoad();

            _imageCanvas.Source = null;
            _animatedImageViewer.Reset();
            _animatedImageViewer.Visibility = Visibility.Collapsed;
            _imageCanvas.Visibility = Visibility.Visible;

            _mainWindow.HideSpriteSheetControls();
            _mainWindow._spriteAnimator = null;
        }

        protected override void Dispose (bool disposing)
        {
            if (!_disposed && disposing)
                _imageLoader.CancelCurrentLoad();
            base.Dispose (disposing);
        }
    }
}