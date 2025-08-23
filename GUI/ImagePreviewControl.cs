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
    public class ImagePreviewControl : Image
    {
        private List<BitmapSource> frames = new List<BitmapSource>();
        private List<int>          frameDelays = new List<int>();
        private int                currentFrameIndex = 0;
        private DispatcherTimer    animationTimer;
        private bool               isAnimated = false;


        public ImagePreviewControl()
        {
            animationTimer       = new DispatcherTimer();
            animationTimer.Tick += OnFrameChange;
            Unloaded            += (s, e) => StopAnimation();
        }

        /// <summary>
        /// Load a static image
        /// </summary>
        public void LoadImage (BitmapSource image)
        {
            StopAnimation();
            frames.Clear();
            frameDelays.Clear();
            isAnimated = false;
            Source = image;
        }

        /// <summary>
        /// Load an animated image with multiple frames
        /// </summary>
        public void LoadAnimatedImage (List<BitmapSource> imageFrames, List<int> delays)
        {
            if (imageFrames == null || imageFrames.Count == 0)
                return;

            StopAnimation();
            frames = imageFrames;
            frameDelays = delays ?? Enumerable.Repeat (100, frames.Count).ToList();
            isAnimated = frames.Count > 1;
            currentFrameIndex = 0;

            // Show first frame
            Source = frames[0];

            if (isAnimated)
                StartAnimation();
        }

        private void OnFrameChange (object sender, EventArgs e)
        {
            if (!isAnimated || frames.Count <= 1)
                return;

            currentFrameIndex = (currentFrameIndex + 1) % frames.Count;
            Source = frames[currentFrameIndex];

            // Set the timer for the next frame
            animationTimer.Interval = TimeSpan.FromMilliseconds (frameDelays[currentFrameIndex]);
        }

        /// <summary>
        /// Start animation playback
        /// </summary>
        public void StartAnimation ()
        {
            if (isAnimated && frames.Count > 1)
            {
                animationTimer.Interval = TimeSpan.FromMilliseconds (frameDelays[currentFrameIndex]);
                animationTimer.Start();
            }
        }

        /// <summary>
        /// Stop animation playback
        /// </summary>
        public void StopAnimation ()
        {
            if (animationTimer != null && isAnimated && animationTimer.IsEnabled)
                animationTimer.Stop();
        }

        /// <summary>
        /// Reset the control to initial state
        /// </summary>
        public void Reset ()
        {
            StopAnimation();
            frames.Clear();
            frameDelays.Clear();
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
        public bool IsPaused 
        { 
            get { return animationTimer != null && !animationTimer.IsEnabled && isAnimated; }
        }
    }
}