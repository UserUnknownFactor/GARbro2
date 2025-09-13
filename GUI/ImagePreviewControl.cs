using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GARbro.GUI.Properties;

namespace GARbro.GUI
{
    /// <summary>
    /// Custom control for displaying images (with frame sequence animation support)
    /// </summary>
    public class ImagePreviewControl : Image, IDisposable
    {
        private List<BitmapSource> frames = new List<BitmapSource>();
        private List<int>          frameDelays = new List<int>();
        private int                currentFrameIndex = 0;
        private DispatcherTimer    animationTimer;
        private bool               isAnimated = false;
        private WriteableBitmap    displayBuffer;
        private readonly object    bufferLock = new object();

        public ImagePreviewControl ()
        {
            animationTimer = new DispatcherTimer();
            animationTimer.Tick += OnFrameChange;
            Unloaded += (s, e) => StopAnimation();

            RenderOptions.SetBitmapScalingMode (this, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode (this, EdgeMode.Aliased);
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
        }

        /// <summary>
        /// Load a static image
        /// </summary>
        public void LoadImage (BitmapSource image)
        {
            Reset();
            Source = image;
        }

        /// <summary>
        /// Load an animated image with multiple frames
        /// </summary>
        public void LoadAnimatedImage (List<BitmapSource> imageFrames, List<int> delays)
        {
            if (imageFrames == null || imageFrames.Count == 0)
                return;

            Reset();

            frames = imageFrames;
            frameDelays = delays ?? Enumerable.Repeat (100, frames.Count).ToList();
            isAnimated = frames.Count > 1;
            currentFrameIndex = 0;

            if (isAnimated)
            {
                var firstFrame = frames[0];

                // NOTE: Must ensure all frames are the same size in a decoder
                displayBuffer = new WriteableBitmap(
                    firstFrame.PixelWidth,
                    firstFrame.PixelHeight,
                    firstFrame.DpiX,
                    firstFrame.DpiY,
                    PixelFormats.Pbgra32,
                    null);

                CopyFrameToBuffer (frames[0]);
                Source = displayBuffer;

                StartAnimation();
            }
            else
            {
                // Single frame - just display it directly
                Source = frames[0];
            }
        }

        private void CopyFrameToBuffer (BitmapSource frame)
        {
            if (displayBuffer == null || frame == null)
                return;

            lock (bufferLock)
            {
                displayBuffer.Lock();
                try
                {
                    BitmapSource sourceFrame = frame;
                    if (frame.Format != PixelFormats.Pbgra32 && frame.Format != PixelFormats.Bgra32)
                    {
                        sourceFrame = new FormatConvertedBitmap (frame, PixelFormats.Pbgra32, null, 0);
                    }

                    var sourceRect = new Int32Rect (0, 0, 
                        Math.Min (sourceFrame.PixelWidth, displayBuffer.PixelWidth),
                        Math.Min (sourceFrame.PixelHeight, displayBuffer.PixelHeight));

                    sourceFrame.CopyPixels (sourceRect, displayBuffer.BackBuffer, 
                        displayBuffer.BackBufferStride * displayBuffer.PixelHeight, 
                        displayBuffer.BackBufferStride);

                    displayBuffer.AddDirtyRect (sourceRect);
                }
                finally
                {
                    displayBuffer.Unlock();
                }
            }
        }

        private void OnFrameChange (object sender, EventArgs e)
        {
            if (!isAnimated || frames.Count <= 1)
                return;

            currentFrameIndex = (currentFrameIndex + 1) % frames.Count;

            if (displayBuffer != null)
                CopyFrameToBuffer (frames[currentFrameIndex]);
            else
                Source = frames[currentFrameIndex];

            int delay = currentFrameIndex < frameDelays.Count ? 
                frameDelays[currentFrameIndex] : 100;

            if (delay <= 0) delay = 10;

            animationTimer.Interval = TimeSpan.FromMilliseconds (delay);
        }

        /// <summary>
        /// Start animation playback
        /// </summary>
        public void StartAnimation ()
        {
            if (isAnimated && frames.Count > 1)
            {
                int initialDelay = currentFrameIndex < frameDelays.Count ? 
                    frameDelays[currentFrameIndex] : 100;

                if (initialDelay <= 0) initialDelay = 10;

                animationTimer.Interval = TimeSpan.FromMilliseconds (initialDelay);
                animationTimer.Start();
            }
        }

        /// <summary>
        /// Stop animation playback
        /// </summary>
        public void StopAnimation ()
        {
            if (animationTimer != null && animationTimer.IsEnabled)
                animationTimer.Stop();
        }

        /// <summary>
        /// Reset the control to initial state
        /// </summary>
        public void Reset ()
        {
            StopAnimation();

            lock (bufferLock)
            {
                displayBuffer = null;
            }

            if (frames != null)
            {
                bool collect = frames?.Count > 7;

                for (int i = 0; i < frames.Count; i++)
                    frames[i] = null;

                frames.Clear();

                if (collect)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
            }

            frameDelays?.Clear();
            isAnimated = false;
            Source = null;
        }

        /// <summary>
        /// Get frame count
        /// </summary>
        public int  FrameCount { get { return frames.Count; } }

        /// <summary>
        /// Check if animated
        /// </summary>
        public bool IsAnimated { get { return isAnimated; } }

        /// <summary>
        /// Check if animation is paused
        /// </summary>
        public bool IsPaused {
            get { return animationTimer != null && !animationTimer.IsEnabled && isAnimated; }
        }

        public void Dispose ()
        {
            animationTimer?.Stop();
            animationTimer = null;
            Reset();
        }
    }
}