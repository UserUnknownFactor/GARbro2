using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using GameRes;

namespace GARbro.GUI.Preview
{
    public class AsyncImageLoader
    {
        private CancellationTokenSource _currentLoadCts;

        public async Task<ImageLoadResult> LoadImageAsync (Entry entry, CancellationToken cancellationToken)
        {
            try
            {
                return await Task.Run(() => LoadImageCore (entry, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return LoadResultBase.CreateCancelled<ImageLoadResult>();
            }
        }

        private ImageLoadResult LoadImageCore (Entry entry, CancellationToken cancellationToken)
        {
            IImageDecoder data = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Open and immediately process the image
                using (data = VFS.OpenImage (entry))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (data?.Image == null)
                        return LoadResultBase.CreateError<ImageLoadResult>("Failed to decode image");

                    if (data.Image is AnimatedImageData animData && animData.IsAnimated)
                    {
                        var frames = new List<BitmapSource>();
                        foreach (var frame in animData.Frames)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var frozenFrame = frame;
                            if (!frozenFrame.IsFrozen)
                            {
                                if (frozenFrame is WriteableBitmap wb)
                                    frozenFrame = BitmapFrame.Create (wb);
                                frozenFrame.Freeze();
                            }
                            frames.Add (frozenFrame);
                        }

                        return new ImageLoadResult
                        {
                            IsAnimated = true,
                            AnimatedFrames = frames,
                            FrameDelays = animData.FrameDelays,
                            SourceFormat = data.SourceFormat,
                            Info = data.Info as IImageComment
                        };
                    }
                    else
                    {
                        var bitmap = data.Image.Bitmap;
                        if (!bitmap.IsFrozen)
                        {
                            bitmap = BitmapFrame.Create (bitmap);
                            bitmap.Freeze();
                        }

                        return new ImageLoadResult
                        {
                            IsAnimated = false,
                            StaticImage = bitmap,
                            SourceFormat = data.SourceFormat,
                            Info = data.Info as IImageComment
                        };
                    }
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException ||
                                       ex is OperationCanceledException ||
                                       ex is AccessViolationException)
            {
                // Cancelation or archive was disposed/closed during operation
                return LoadResultBase.CreateCancelled<ImageLoadResult>();
            }
            catch (OutOfMemoryException)
            {
                return LoadResultBase.CreateError<ImageLoadResult>(
                    Localization.Format ("ImageTooLargeMemory", entry.Name));
            }
            catch (Exception ex)
            {
                return LoadResultBase.CreateError<ImageLoadResult>(ex.Message);
            }
        }

        public void CancelCurrentLoad()
        {
            _currentLoadCts?.Cancel();
            _currentLoadCts?.Dispose();
            _currentLoadCts = null;
        }
    }

    public class ImageLoadResult : LoadResultBase
    {
        public bool IsAnimated { get; set; }
        public BitmapSource StaticImage { get; set; }
        public List<BitmapSource> AnimatedFrames { get; set; }
        public List<int> FrameDelays { get; set; }
        public ImageFormat SourceFormat { get; set; }
        public IImageComment Info { get; set; }
    }
}