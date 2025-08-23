using System;
using System.Collections.Generic;
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

        public override bool IsActive => _imageCanvas.Visibility == Visibility.Visible || 
                                         _animatedImageViewer.Visibility == Visibility.Visible;

        public ImagePreviewHandler (MainWindow mainWindow, Image imageCanvas, ImagePreviewControl animatedImageViewer)
        {
            _mainWindow = mainWindow;
            _imageCanvas = imageCanvas;
            _animatedImageViewer = animatedImageViewer;
        }

        public override void LoadContent (PreviewFile preview)
        {
            using (var data = VFS.OpenImage (preview.Entry))
            {
                if (data.Image is AnimatedImageData animData && animData.IsAnimated)
                    SetAnimatedImage (preview, data);
                else
                    SetStaticImage (preview, data);
            }
        }

       private void SetStaticImage (PreviewFile preview, IImageDecoder image)
        {
            if (image?.Image == null)
                return;

            var bitmap = image.Image.Bitmap;
            if (!bitmap.IsFrozen)
                bitmap.Freeze();

            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow.ShowImagePreview();
                _imageCanvas.Source = bitmap;

                // Apply DPI scaling transform to show at pixel dimensions
                double dpiScaleX = bitmap.DpiX / Desktop.DpiX;
                double dpiScaleY = bitmap.DpiY / Desktop.DpiY;

                if (Math.Abs(dpiScaleX - 1.0) > 0.001 || Math.Abs(dpiScaleY - 1.0) > 0.001)
                    _imageCanvas.LayoutTransform = new ScaleTransform(dpiScaleX, dpiScaleY);
                else
                    _imageCanvas.LayoutTransform = Transform.Identity;

                _mainWindow.ApplyDownScaleSetting();

                _mainWindow.RemoveGridOverlay();
                _mainWindow.StopSpriteAnimation();

                _mainWindow._spriteAnimator = new SpriteSheetAnimator(bitmap);

                if (_mainWindow.SpriteSheetPanel.Visibility != Visibility.Visible)
                    _mainWindow.ShowSpriteSheetControls();

                _mainWindow.SetPreviewStatus ((image.SourceFormat?.Tag ?? "?") +
                    ((IImageComment)image.Info)?.GetComment() ?? "");
            });
        }

        private void SetAnimatedImage(PreviewFile preview, IImageDecoder image)
        {
            if (image?.Image == null)
                return;

            var animation = image.Image as AnimatedImageData;
            var frames = animation.Frames;
            var delays = animation.FrameDelays;

            var processedFrames = new List<BitmapSource>();
            foreach (var frame in frames)
            {
                if (!frame.IsFrozen)
                {
                    // For writable bitmaps, we need to freeze them
                    if (frame is WriteableBitmap)
                        frame.Freeze();
                }
                processedFrames.Add (frame);
            }

            _mainWindow.Dispatcher.Invoke (() =>
            {
                _mainWindow.ShowAnimatedImagePreview();

                if (processedFrames.Count > 0)
                {
                    var firstFrame = processedFrames[0];
                    double dpiScaleX = firstFrame.DpiX / Desktop.DpiX;
                    double dpiScaleY = firstFrame.DpiY / Desktop.DpiY;

                    if (Math.Abs(dpiScaleX - 1.0) > 0.001 || Math.Abs(dpiScaleY - 1.0) > 0.001)
                        _animatedImageViewer.LayoutTransform = new ScaleTransform(dpiScaleX, dpiScaleY);
                    else
                        _animatedImageViewer.LayoutTransform = Transform.Identity;
                }

                _animatedImageViewer.LoadAnimatedImage (processedFrames, delays);
                _mainWindow.ApplyScalingToAnimatedViewer();

                _mainWindow.SetPreviewStatus ((image.SourceFormat?.Tag ?? "?") + 
                    ((IImageComment)image.Info)?.GetComment() ?? "");
            });
        }

        public override void Reset ()
        {
            _imageCanvas.Source = null;
            _animatedImageViewer.Reset();
            _animatedImageViewer.Visibility = Visibility.Collapsed;
            _imageCanvas.Visibility = Visibility.Visible;

            _mainWindow.HideSpriteSheetControls();
            _mainWindow._spriteAnimator = null;
        }
    }
}