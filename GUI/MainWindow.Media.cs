using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using NAudio.Wave;
using GameRes;

namespace GARbro.GUI
{
    public partial class MainWindow
    {
        private readonly List<IDisposable> _activeStreams = new List<IDisposable>();
        internal MediaControl _mediaControl;

        internal bool _isAutoPlaying = false;
        internal bool _isAutoCycling = false;

        private void InitializeMediaSystem()
        {
            InitializeMediaControls();
            InitializeSpriteAnimation();
        }

        private void InitializeMediaControls()
        {
            var volumeControlPanel = FindName ("VolumeControlPanel") as StackPanel;
            _mediaControl = new MediaControl(
                MediaControlPanel,
                MediaPauseButton,
                MediaStopButton,
                MediaCycleButton,
                MediaAutoButton,
                MediaVolumeSlider,
                volumeControlPanel
            );

            _mediaControl.ConfigureForMediaType (MediaType.None);
        }

        private void CleanupMediaPlayback()
        {
            _previewStateMachine?.StopAllPlayback();
        }

        internal void RegisterStream (IDisposable stream)
        {
            lock (_activeStreams)
            {
                _activeStreams.Add (stream);
            }
        }

        internal void RemoveStream (IDisposable stream)
        {
            lock (_activeStreams)
            {
                _activeStreams.Remove (stream);
            }
        }

        private void DisposeAllStreams ()
        {
            lock (_activeStreams)
            {
                foreach (var stream in _activeStreams)
                {
                    try
                    {
                        stream.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine (string.Format ("Error disposing stream: {0}", ex.Message));
                    }
                }
                _activeStreams.Clear();
            }
        }

        private void MediaVolumeSlider_ValueChanged (object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _savedVolume = e.NewValue;
            _previewStateMachine.SetVolume((float)e.NewValue);
        }

        private void StopPlaybackExec (object sender, ExecutedRoutedEventArgs e)
        {
            _previewStateMachine?.StopCurrentPlayback();
        }

        private void PausePlaybackExec (object sender, ExecutedRoutedEventArgs e)
        {
            _previewStateMachine?.PauseCurrentPlayback();
        }

        private void CyclePlaybackExec (object sender, ExecutedRoutedEventArgs e)
        {
            _isAutoCycling = !_isAutoCycling;
            _previewStateMachine?.UpdateAudioControls();
        }

        private void AutoPlaybackExec (object sender, ExecutedRoutedEventArgs e)
        {
            _isAutoPlaying = !_isAutoPlaying;
            _previewStateMachine?.UpdateAudioControls();
        }

        internal string GetAutoPlayStatus ()
        {
            if (_isAutoCycling)
            {
                if (_isAutoPlaying)
                    return Localization._T ("RepeatDir");
                else
                    return Localization._T ("RepeatFile");
            }
            else if (_isAutoPlaying)
            {
                return Localization._T ("AutoDir");
            }
            return Localization._T ("Manual");
        }

        internal void OnPlaybackStopped (object sender, StoppedEventArgs e)
        {
            if (_isShuttingDown)
                return;

            _previewStateMachine?.OnAudioPlaybackStopped();
        }

        private void CycleToNextItem (bool forward)
        {
            int nextIndex;

            if (forward)
            {
                nextIndex = GetNextFileIndex(
                    CurrentDirectory.SelectedIndex,
                    allowCycling: true,
                    skipCurrent:  true,
                    fileFilter:   null
                );
            }
            else
            {
                nextIndex = GetPreviousFileIndex(
                    CurrentDirectory.SelectedIndex,
                    allowCycling: true,
                    skipCurrent:  true,
                    fileFilter:   null
                );
            }

            if (nextIndex >= 0)
            {
                CurrentDirectory.SelectedIndex = nextIndex;
                lv_SelectItem (CurrentDirectory.Items[nextIndex] as EntryViewModel);
            }
        }

        internal int GetNextFileIndex (int currentIndex, bool allowCycling, bool skipCurrent, Func<EntryViewModel, bool> fileFilter = null)
        {
            if (currentIndex < 0 || CurrentDirectory.Items.Count == 0)
                return -1;

            if (!skipCurrent && IsValidEntry (currentIndex, fileFilter))
                return currentIndex;

            for (int i = currentIndex + 1; i < CurrentDirectory.Items.Count; i++)
            {
                if (IsValidEntry (i, fileFilter))
                    return i;
            }

            if (allowCycling)
            {
                for (int i = 0; i < currentIndex; i++)
                {
                    if (IsValidEntry (i, fileFilter))
                        return i;
                }
            }

            return -1;
        }

        private int GetPreviousFileIndex (int currentIndex, bool allowCycling, bool skipCurrent, Func<EntryViewModel, bool> fileFilter = null)
        {
            if (currentIndex < 0 || CurrentDirectory.Items.Count == 0)
                return -1;

            if (!skipCurrent && IsValidEntry (currentIndex, fileFilter))
                return currentIndex;

            for (int i = currentIndex - 1; i >= 0; i--)
            {
                if (IsValidEntry (i, fileFilter))
                    return i;
            }

            if (allowCycling)
            {
                for (int i = CurrentDirectory.Items.Count - 1; i > currentIndex; i--)
                {
                    if (IsValidEntry (i, fileFilter))
                        return i;
                }
            }

            return -1;
        }

        private bool IsValidEntry (int index, Func<EntryViewModel, bool> fileFilter)
        {
            var entry = CurrentDirectory.Items[index] as EntryViewModel;
            if (entry == null)
                return false;

            if (fileFilter != null)
                return fileFilter (entry);

            if (entry.Name == VFS.DIR_PARENT)
                return false;

            return true;
        }

        internal void UpdateVideoPosition (TimeSpan position, TimeSpan duration)
        {
            if (m_video_preview_ctl == null || !m_video_preview_ctl.IsVisible)
                return;
            var timeInfo = string.Format ("{0} / {1}", FormatTimeSpan (position), FormatTimeSpan (duration));
            if (!string.IsNullOrEmpty (m_video_preview_ctl.CurrentCodecInfo))
                SetPreviewStatus (string.Format ("{0} | {1}", m_video_preview_ctl.CurrentCodecInfo, timeInfo));
            else
                SetPreviewStatus (Localization.Format ("VideoInfo", timeInfo));
        }

        private string FormatTimeSpan (TimeSpan timeSpan)
        {
            return timeSpan.Hours > 0
                ? string.Format ("{0:D2}:{1:D2}:{2:D2}", timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds)
                : string.Format ("{0:D2}:{1:D2}", timeSpan.Minutes, timeSpan.Seconds);
        }

        internal void UpdateAudioControls ()
        {
            _previewStateMachine?.UpdateAudioControls();
        }

        internal void UpdateMediaControlsVisibility (MediaType mediaType)
        {
            _currentMediaType = mediaType;
            Dispatcher.Invoke (() => _mediaControl.ConfigureForMediaType (mediaType));
        }

        private MediaType _currentMediaType = MediaType.None;
    }
}