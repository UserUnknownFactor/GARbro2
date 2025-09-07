using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GARbro.GUI
{
    public class ImageOverlayHandler
    {
        private readonly Canvas _overlayCanvas;
        private readonly List<OverlayImage> _images = new List<OverlayImage>();
        private OverlayImage _previewImage;

        public ImageOverlayHandler (Canvas canvas)
        {
            _overlayCanvas = canvas;
        }

        public bool IsPreviewImage(string name)
        {
            return _previewImage != null && _previewImage.Name == name;
        }

        public void AddImage (BitmapSource image, string name)
        {
            _images.Add (new OverlayImage 
            { 
                Source = image, 
                Name = name,
                Opacity = 1.0,
                IsVisible = true,
                ZIndex = _images.Count
            });

            UpdateComposite();
        }


        public void RemoveImage (int index)
        {
            var image = _images.FirstOrDefault (i => i.ZIndex == index);
            if (image != null)
            {
                _images.Remove (image);

                UpdateZIndices();
                UpdateComposite();
            }
        }

        public void SetImageOpacity (int index, double opacity)
        {
            var image = _images.FirstOrDefault (i => i.ZIndex == index);
            if (image != null)
            {
                image.Opacity = opacity;
                UpdateComposite();
            }
        }

        public void SetImageVisibility (int index, bool visible)
        {
            var image = _images.FirstOrDefault (i => i.ZIndex == index);
            if (image != null)
            {
                image.IsVisible = visible;
                UpdateComposite();
            }
        }

        public void MoveImageUp (int index)
        {
            var imageToMove = _images.FirstOrDefault (i => i.ZIndex == index);
            var imageAbove = _images.FirstOrDefault (i => i.ZIndex == index + 1);

            if (imageToMove != null && imageAbove != null)
            {
                imageToMove.ZIndex = index + 1;
                imageAbove.ZIndex = index;
                UpdateComposite();
            }
        }

        public void MoveImageDown (int index)
        {
            var imageToMove = _images.FirstOrDefault (i => i.ZIndex == index);
            var imageBelow = _images.FirstOrDefault (i => i.ZIndex == index - 1);

            if (imageToMove != null && imageBelow != null)
            {
                imageToMove.ZIndex = index - 1;
                imageBelow.ZIndex = index;
                UpdateComposite();
            }
        }

        public void MoveToTop (int currentZIndex)
        {
            var image = _images.FirstOrDefault (i => i.ZIndex == currentZIndex);
            if (image != null)
            {
                // Move all items with higher z-index down by 1
                foreach (var img in _images.Where (i => i.ZIndex > currentZIndex))
                    img.ZIndex--;

                // Set this image to highest z-index
                image.ZIndex = _images.Count - 1;
                UpdateComposite();
            }
        }

        public void MoveToBottom (int currentZIndex)
        {
            var image = _images.FirstOrDefault (i => i.ZIndex == currentZIndex);
            if (image != null)
            {
                // Move all items with lower z-index up by 1
                foreach (var img in _images.Where (i => i.ZIndex < currentZIndex))
                    img.ZIndex++;

                // Set this image to lowest z-index
                image.ZIndex = 0;
                UpdateComposite();
            }
        }

        public void ReorderLayers (int sourceIndex, int targetIndex)
        {
            var sourceImage = _images.FirstOrDefault (i => i.ZIndex == sourceIndex);
            var targetImage = _images.FirstOrDefault (i => i.ZIndex == targetIndex);

            if (sourceImage != null && targetImage != null)
            {
                int tempIndex = sourceImage.ZIndex;
                sourceImage.ZIndex = targetImage.ZIndex;
                targetImage.ZIndex = tempIndex;
                UpdateComposite();
            }
        }

        public void SetPreviewImage (BitmapSource image, string name)
        {
            _previewImage = new OverlayImage
            {
                Source = image,
                Name = name,
                Opacity = 1.0,
                IsVisible = true,
                ZIndex = int.MaxValue,
                IsPreview = true
            };
            UpdateComposite();
        }

        public void ClearPreview ()
        {
            _previewImage = null;
            UpdateComposite();
        }

        private void UpdateZIndices ()
        {
            var sortedImages = _images.OrderBy (i => i.ZIndex).ToList();
            for (int i = 0; i < sortedImages.Count; i++)
                sortedImages[i].ZIndex = i;
        }

        public void UpdateComposite ()
        {
            var allImages = new List<OverlayImage>(_images);
            if (_previewImage != null)
                allImages.Add (_previewImage);

            if (!allImages.Any (img => img.IsVisible))
            {
                _overlayCanvas.Children.Clear();
                return;
            }

            double maxWidth = 0;
            double maxHeight = 0;

            foreach (var img in allImages.Where (img => img.IsVisible && img.Source != null))
            {
                maxWidth = Math.Max (maxWidth, img.Source.PixelWidth);
                maxHeight = Math.Max (maxHeight, img.Source.PixelHeight);
            }

            if (maxWidth == 0 || maxHeight == 0)
            {
                _overlayCanvas.Children.Clear();
                return;
            }

            _overlayCanvas.Children.Clear();
            _overlayCanvas.Width  = maxWidth;
            _overlayCanvas.Height = maxHeight;

            foreach (var img in allImages.OrderBy (i => i.ZIndex))
            {
                if (!img.IsVisible || img.Source == null) continue;

                var image = new Image
                {
                    Source = img.Source,
                    Opacity = img.Opacity,
                    Stretch = Stretch.None,
                    SnapsToDevicePixels = true,
                    UseLayoutRounding = true,
                    Tag = img, // Store reference to OverlayImage
                    Cursor = System.Windows.Input.Cursors.Arrow,
                    IsHitTestVisible = !img.IsPreview
                };

                double dpiScaleX = img.Source.DpiX / Desktop.DpiX;
                double dpiScaleY = img.Source.DpiY / Desktop.DpiY;

                if (Math.Abs (dpiScaleX - 1.0) > 0.001 || Math.Abs (dpiScaleY - 1.0) > 0.001)
                {
                    image.LayoutTransform = new ScaleTransform (dpiScaleX, dpiScaleY);
                }

                // Apply position with offset
                double baseX = (maxWidth - img.Source.PixelWidth) / 2.0;
                double baseY = (maxHeight - img.Source.PixelHeight) / 2.0;

                Canvas.SetLeft (image, baseX + img.OffsetX);
                Canvas.SetTop (image, baseY + img.OffsetY);

                Panel.SetZIndex (image, img.ZIndex);
                _overlayCanvas.Children.Add (image);
            }
        }

        public void SetImagePosition (int zIndex, double offsetX, double offsetY)
        {
            var image = _images.FirstOrDefault (i => i.ZIndex == zIndex);
            if (image != null)
            {
                image.OffsetX = offsetX;
                image.OffsetY = offsetY;
                UpdateComposite();
            }
        }

        public BitmapSource GetCompositeBitmap ()
        {
            if (_images.Count == 0 || _overlayCanvas.Width == 0 || _overlayCanvas.Height == 0) 
                return null;

            // Temporarily remove preview for export
            var hadPreview = _previewImage != null;
            var tempPreview = _previewImage;
            _previewImage = null;

            UpdateComposite();

            _overlayCanvas.Measure(new Size(_overlayCanvas.Width, _overlayCanvas.Height));
            _overlayCanvas.Arrange(new Rect(0, 0, _overlayCanvas.Width, _overlayCanvas.Height));
            _overlayCanvas.UpdateLayout();

            var renderTarget = new RenderTargetBitmap (
                (int)_overlayCanvas.Width,
                (int)_overlayCanvas.Height,
                Desktop.DpiX, Desktop.DpiY, PixelFormats.Pbgra32);

            renderTarget.Render (_overlayCanvas);

            // Restore preview
            if (hadPreview)
            {
                _previewImage = tempPreview;
                UpdateComposite();
            }

            return renderTarget;
        }

        public List<OverlayImage> GetImages ()
        {
            return new List<OverlayImage>(_images);
        }

        public void Clear()
        {
            _images.Clear();
            _previewImage = null;
            _overlayCanvas.Children.Clear();
        }
    }

    public class OverlayImage
    {
        public BitmapSource Source { get; set; }
        public string         Name { get; set; }
        public double      Opacity { get; set; }
        public bool      IsVisible { get; set; }
        public int          ZIndex { get; set; }
        public double      OffsetX { get; set; } = 0;
        public double      OffsetY { get; set; } = 0;
        public bool      IsPreview { get; set; } = false;
        public double    DpiScaleX => Source != null ? Source.DpiX / Desktop.DpiX : 1.0;
        public double    DpiScaleY => Source != null ? Source.DpiY / Desktop.DpiY : 1.0;
    }
}