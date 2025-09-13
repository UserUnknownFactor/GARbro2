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
                    var entry = _mainWindow.ViewModel.Find(preview.Entry.Name);
                    if (entry != null)
                        entry.Type = "";
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
            return (entry.Name.EndsWith (".gif", StringComparison.OrdinalIgnoreCase) || 
                    entry.Name.EndsWith (".webp", StringComparison.OrdinalIgnoreCase)) && 
                   entry.Size > 3_000_000;
        }

        private void SetStaticImage (PreviewFile preview, ImageLoadResult result)
        {
            _mainWindow.ShowImagePreview();

            var processedImage = ProcessImageForDisplay (result.StaticImage);
            _imageCanvas.Source = processedImage;

            // Apply DPI scaling transform
            double dpiScaleX = processedImage.DpiX / Desktop.DpiX;
            double dpiScaleY = processedImage.DpiY / Desktop.DpiY;

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

            string formatTag = result.SourceFormat?.Tag ?? result.SourceFormat?.GetType().Name ?? "?";
            _mainWindow.SetPreviewStatus (formatTag + (result.Info?.GetComment() ?? ""));
        }

        private BitmapSource ProcessImageForDisplay (BitmapSource image)
        {
            if (!MainWindow.DownScaleImage.Get<bool>() ||
                image.Format.BitsPerPixel != 32 || 
                image.PixelWidth * image.PixelHeight > 4096 * 4096)
                return image;

            var bounds = GetContentBounds (image);
            if (bounds.HasValue)
                return ApplyContentBounds (image, bounds.Value);

            return image;
        }

        private Int32Rect? GetContentBounds (BitmapSource image)
        {
            if (image.Format != PixelFormats.Bgra32 && image.Format != PixelFormats.Pbgra32)
                return null;

            int width = image.PixelWidth;
            int height = image.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];

            image.CopyPixels (pixels, stride, 0);

            int minX = width, minY = height, maxX = 0, maxY = 0;
            bool hasContent = false;

            // Scan for non-transparent pixels
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int pixelOffset = rowOffset + x * 4;
                    byte alpha = pixels[pixelOffset + 3];

                    if (alpha > 0) // Non-transparent pixel
                    {
                        hasContent = true;
                        minX = Math.Min (minX, x);
                        minY = Math.Min (minY, y);
                        maxX = Math.Max (maxX, x);
                        maxY = Math.Max (maxY, y);
                    }
                }
            }

            if (!hasContent)
                return null;

            int contentWidth = maxX - minX + 1;
            int contentHeight = maxY - minY + 1;

            // Only crop if there's significant transparent area (>25% reduction)
            if (contentWidth * contentHeight < width * height * 0.75)
                return new Int32Rect (minX, minY, contentWidth, contentHeight);

            return null;
        }

        private BitmapSource ApplyContentBounds (BitmapSource source, Int32Rect bounds)
        {
            try
            {
                var cropped = new CroppedBitmap (source, bounds);

                // Create a new bitmap to ensure it's not dependent on the original
                var newBitmap = new WriteableBitmap (cropped);
                newBitmap.Freeze();

                return newBitmap;
            }
            catch
            {
                // If cropping fails, return original
                return source;
            }
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