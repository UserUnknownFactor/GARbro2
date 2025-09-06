using System;
using System.Windows;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using GARbro.GUI.Preview;
using GameRes;
using System.Threading;

namespace GARbro.GUI
{
    public class VideoPreviewControl : Grid
    {
        private  MainWindow _mainWindow;
        private    DateTime _loadStartTime;

        private    MediaElement mediaPlayer;
        private       VideoData currentVideo;
        private          string currentVideoFile;
        private    List<string> tempFiles = new List<string>();
        private DispatcherTimer positionTimer;
        private    static float lastVolume = 0.8f;

        private const byte MAX_FILENAME = 70;

        #region P/Invoke for codec detection

        [DllImport("mfplat.dll", CharSet = CharSet.Unicode)]
        private static extern int MFStartup (uint version, uint flags);

        [DllImport("mfplat.dll")]
        private static extern int MFShutdown();

        private const uint MF_VERSION = 0x00020070; // Version 2.70
        private const uint MFSTARTUP_FULL = 0;

        #endregion

        public string CurrentCodecInfo { get; set; }

        public VideoPreviewControl (MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            try
            {
                MFStartup (MF_VERSION, MFSTARTUP_FULL);
            }
            catch { }

            mediaPlayer = new MediaElement
            {
                LoadedBehavior   = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                Stretch          = Stretch.Uniform,
                IsMuted          = false,
                Volume           = Math.Min (Math.Max (lastVolume, 0f), 1f),
                ScrubbingEnabled = true
            };

            // Enable hardware acceleration if available
            RenderOptions.SetBitmapScalingMode (mediaPlayer, BitmapScalingMode.HighQuality);

            mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
            mediaPlayer.MediaEnded  += MediaPlayer_MediaEnded;
            mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;

            mediaPlayer.Loaded += (s, e) =>
            {
                if (IsPlaying && _mainWindow != null)
                {
                    Task.Delay(400).ContinueWith(_ => 
                        _mainWindow?.Dispatcher.Invoke(() => _mainWindow.SetFileStatus("")));
                }
            };

            Children.Add (mediaPlayer);

            positionTimer = new DispatcherTimer();
            positionTimer.Interval = TimeSpan.FromMilliseconds (100);
            positionTimer.Tick += PositionTimer_Tick;

            Unloaded += (s, e) => CleanupVideoAsync();
        }

        public event Action<string> StatusChanged;
        public event Action<TimeSpan, TimeSpan> PositionChanged;
        public event Action MediaEnded;
        public event Action<bool> PlaybackStateChanged;

        public bool IsPlaying { get; private set; }
        public TimeSpan Position => mediaPlayer.Position;
        public TimeSpan Duration => mediaPlayer.NaturalDuration.HasTimeSpan ? mediaPlayer.NaturalDuration.TimeSpan : TimeSpan.Zero;

        private void SetVideoStatus (string text)
        {
            StatusChanged?.Invoke (text);
        }

        public void LoadVideo (VideoData videoData)
        {
            if (videoData == null)
                return;

            bool isSameVideo = currentVideo != null &&
                ((!string.IsNullOrEmpty (videoData.TempFile) && currentVideo.TempFile == videoData.TempFile) ||
                (string.IsNullOrEmpty (videoData.TempFile) && currentVideo.FileName == videoData.FileName));

            if (isSameVideo)
            {
                mediaPlayer.Position = TimeSpan.Zero;
                mediaPlayer.Play();
                positionTimer.Start();
                return;
            }

            CleanupVideoAsync();

            try
            {
                currentVideo = videoData;

                if (!string.IsNullOrEmpty (videoData.TempFile) && File.Exists (videoData.TempFile))
                {
                    currentVideoFile = videoData.TempFile;
                    if (currentVideoFile.StartsWith (Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                        tempFiles.Add (currentVideoFile);
                }
                else
                    currentVideoFile = videoData.FileName;

                UpdateCodecInfo (videoData);

                _loadStartTime = DateTime.Now;

                mediaPlayer.Source = new Uri (currentVideoFile);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty (currentVideoFile))
                    TryAlternativePlayer (currentVideoFile, ex.Message);
                else
                    throw new InvalidFormatException (ex.Message);
            }
        }

        private string GetVideoExtension (VideoData videoData)
        {
            if (!string.IsNullOrEmpty (videoData.Codec))
            {
                var codec = videoData.Codec.ToLowerInvariant();
                if (codec.Contains ("h264") || codec.Contains ("avc"))
                    return ".mp4";
                else if (codec.Contains ("vp8") || codec.Contains ("vp9"))
                    return ".webm";
                else if (codec.Contains ("wmv"))
                    return ".wmv";
                else if (codec.Contains ("wma"))
                    return ".wma";
            }
            return ".mp4";
        }

        private void UpdateCodecInfo (VideoData videoData)
        {
            string video_name = videoData.FileName ?? Localization._T ("Stream");

            if (video_name.Length > MAX_FILENAME)
            {
                int lastSeparator = Math.Max (video_name.LastIndexOf('\\'), video_name.LastIndexOf (VFS.DIR_DELIMITER));
                if (lastSeparator >= 0)
                    video_name = video_name.Substring (lastSeparator + 1);
                if (video_name.Length > MAX_FILENAME)
                    video_name = "..." + video_name.Substring (video_name.Length - MAX_FILENAME + 3);
            }

            uint width = videoData.Width > 0 ? videoData.Width : (uint)mediaPlayer.NaturalVideoWidth;
            uint height = videoData.Height > 0 ? videoData.Height : (uint)mediaPlayer.NaturalVideoHeight;

            if (width == 0 || height == 0)
            {
                CurrentCodecInfo = Localization.Format ("VideoCodecInfo",
                    video_name, videoData.Codec, "?", "?", 
                    videoData.FrameRate > 0 ? videoData.FrameRate.ToString("F1") : "?");
            }
            else
            {
                CurrentCodecInfo = Localization.Format ("VideoCodecInfo",
                    video_name, videoData.Codec, width, height,
                    videoData.FrameRate > 0 ? videoData.FrameRate.ToString("F1") : "?");
            }
        }

        private void TryAlternativePlayer (string videoFile, string errorMessage = "")
        {
            var result = MessageBox.Show(
                Localization.Format ("VideoPlaybackErrorText", errorMessage),
                Localization._T ("VideoPlaybackError"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start (new ProcessStartInfo
                    {
                        FileName = videoFile,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    throw new InvalidFormatException(
                        Localization.Format ("FailedToOpenExternalPlayer", ex.Message));
                }
            }
        }

        private MessageBoxResult ShowError (string message, bool cancallable = false)
        {
            return MessageBox.Show (message, Localization._T ("VideoPlaybackError"),
                cancallable ? MessageBoxButton.OKCancel : MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public async void CleanupVideoAsync ()
        {
            Stop();
            positionTimer.Stop();
            mediaPlayer.Source = null;

            var videoToDispose = currentVideo;
            var filesToDelete = new List<string>(tempFiles);

            currentVideo = null;
            tempFiles.Clear();

            if (videoToDispose != null || filesToDelete.Count > 0)
            {
                await Task.Run (() =>
                {
                    videoToDispose?.Dispose();

                    foreach (var file in filesToDelete)
                    {
                        try
                        {
                            if (File.Exists (file))
                                File.Delete (file);
                        }
                        catch { }
                    }
                    try
                    {
                        // WPF can create temporary cache for buffering here
                        var wpfFolder = Path.Combine (Path.GetTempPath(), "WPF");
                        if (Directory.Exists (wpfFolder))
                            Directory.Delete (wpfFolder, true);
                    }
                    catch { }
                });
            }
        }

        private void MediaPlayer_MediaOpened (object sender, RoutedEventArgs e)
        {
            if (currentVideo != null && (currentVideo.Width == 0 || currentVideo.Height == 0))
            {
                currentVideo.Width = (uint)mediaPlayer.NaturalVideoWidth;
                currentVideo.Height = (uint)mediaPlayer.NaturalVideoHeight;
                UpdateCodecInfo (currentVideo);
            }

            SetVideoStatus (CurrentCodecInfo);

            // Auto-play on load
            Play();
        }

        private void MediaPlayer_MediaEnded (object sender, RoutedEventArgs e)
        {
            MediaEnded?.Invoke();
            CleanupVideoAsync();
        }

        private void MediaPlayer_MediaFailed (object sender, ExceptionRoutedEventArgs e)
        {
            string errorMsg = Localization._T ("MediaFailedToLoad");
            if (e.ErrorException != null)
            {
                errorMsg += $": {e.ErrorException.Message}";
                // Check for common codec issues
                if (e.ErrorException.HResult == unchecked((int)0xC00D11B1))
                    errorMsg += "\n\n" + Localization._T ("AdditionalCodecsNeeded");
            }

            if (!string.IsNullOrEmpty (currentVideoFile))
                TryAlternativePlayer (currentVideoFile, errorMsg);
            else
            {
                CleanupVideoAsync();
                throw new InvalidFormatException (errorMsg);
            }
        }

        private void PositionTimer_Tick (object sender, EventArgs e)
        {
            if (mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                PositionChanged?.Invoke (mediaPlayer.Position, mediaPlayer.NaturalDuration.TimeSpan);
            }
        }

        public void Play ()
        {
            mediaPlayer.Play();
            IsPlaying = true;
            positionTimer.Start();
            PlaybackStateChanged?.Invoke (true);
        }

        public void Pause ()
        {
            mediaPlayer.Pause();
            IsPlaying = false;
            positionTimer.Stop();
            PlaybackStateChanged?.Invoke (false);
        }

        public void Stop ()
        {
            mediaPlayer.Stop();
            mediaPlayer.Position = TimeSpan.Zero;
            IsPlaying = false;
            positionTimer.Stop();
            PlaybackStateChanged?.Invoke (false);
        }

        public void Seek (TimeSpan position)
        {
            mediaPlayer.Position = position;
        }

        public void SetVolume (double volume)
        {
            mediaPlayer.Volume = Math.Max (0, Math.Min (1, volume));
            lastVolume = (float)volume;
        }

        ~VideoPreviewControl()
        {
            try
            {
                MFShutdown();
            }
            catch { }
        }
    }

    public class VideoPreviewHandler : PreviewHandlerBase
    {
        private readonly          MainWindow _mainWindow;
        private readonly VideoPreviewControl _videoControl;

        public override bool IsActive => _videoControl.IsVisible;
        public         bool IsPlaying => _videoControl.IsPlaying;

        public VideoPreviewHandler (MainWindow mainWindow, VideoPreviewControl videoControl)
        {
            _mainWindow   = mainWindow;
            _videoControl = videoControl;
        }

        public override async Task LoadContentAsync (PreviewFile preview, CancellationToken cancellationToken)
        {
            VideoData videoData = null;

            try
            {
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using (var input = VFS.OpenBinaryStream (preview.Entry))
                    {
                        videoData = VideoFormat.Read (input);
                        if (videoData == null)
                            throw new InvalidFormatException (Localization._T ("CodecNotFound"));
                    }
                }, cancellationToken);

                _mainWindow.ShowVideoPreview();
                _videoControl.LoadVideo (videoData);
            }
            catch (Exception ex) when (ex is ObjectDisposedException || ex is OperationCanceledException)
            {
                videoData?.Dispose();
            }
            catch
            {
                videoData?.Dispose();
                throw;
            }
        }

        public void Play()  => _videoControl.Play();
        public void Pause() => _videoControl.Pause();
        public void Stop()  => _videoControl.Stop();
        public void SetVolume (double volume) => _videoControl.SetVolume (volume);

        public override void Reset ()
        {
            _videoControl.CleanupVideoAsync();
            _videoControl.Visibility = Visibility.Collapsed;
        }
    }
}