using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using GameRes;

namespace GARbro.GUI
{
    public enum MediaType
    {
        None,
        Audio,
        Video,
        Text,
        Image,
        Sprite,
        Model
    }

    public enum SpriteMode
    {
        None,
        Static,
        Grid,
        Overlay
    }

    /// <summary>
    /// Manages preview state transitions and media control visibility
    /// </summary>
    public class PreviewStateMachine
    {
        private readonly MainWindow _mainWindow;

        private    MediaType _currentMediaType = MediaType.None;
        private MediaType _backgroundMediaType = MediaType.None;
        private  SpriteMode _currentSpriteMode = SpriteMode.None;
        private      bool _audioPlaybackActive = false;

        public MediaType CurrentMediaType   => _currentMediaType;
        public SpriteMode CurrentSpriteMode => _currentSpriteMode;
        public bool IsAudioActive           => _audioPlaybackActive;

        public PreviewStateMachine (MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public void TransitionToMedia (string entryType, Action loadAction)
        {
            TransitionToMedia (GetMediaType (entryType), loadAction);
        }

        public void TransitionToMedia (MediaType newMediaType, Action loadAction)
        {
            var previousMediaType = _currentMediaType;

            if (previousMediaType != newMediaType)
            {
                switch (previousMediaType)
                {
                case MediaType.Video:
                    StopVideoPlayback();
                    break;
                case MediaType.Image:
                case MediaType.Sprite:
                    _mainWindow.StopSpriteAnimation();
                    _mainWindow.HideSpriteSheetControls();
                    break;
                case MediaType.Model:
                    _mainWindow._modelPreviewHandler?.Reset();
                    break;
                }
            }

            switch (newMediaType)
            {
            case MediaType.Audio:
                _audioPlaybackActive = true;
                _currentMediaType = MediaType.Audio;
                _backgroundMediaType = MediaType.None;
                break;

            case MediaType.Video:
                StopAudioPlayback();
                _currentMediaType = MediaType.Video;
                _currentSpriteMode = SpriteMode.None;
                break;

            case MediaType.Model:
                StopAudioPlayback();
                StopVideoPlayback();
                _currentMediaType = MediaType.Model;
                _currentSpriteMode = SpriteMode.None;
                break;

            case MediaType.Sprite:
                _currentMediaType = MediaType.Sprite;
                // Don't stop audio if it's playing in background
                if (!_audioPlaybackActive)
                    StopAudioPlayback();
                else
                    _backgroundMediaType = MediaType.Audio;
                break;

            case MediaType.Text:
                _currentMediaType = MediaType.Text;
                _currentSpriteMode = SpriteMode.None;
                if (_audioPlaybackActive)
                    _backgroundMediaType = MediaType.Audio;
                break;

            case MediaType.Image:
                _currentMediaType = MediaType.Image;
                if (_audioPlaybackActive)
                    _backgroundMediaType = MediaType.Audio;
                break;

            case MediaType.None:
                _currentSpriteMode = SpriteMode.None;
                if (!_audioPlaybackActive)
                {
                    _currentMediaType = MediaType.None;
                    _mainWindow.SetFileStatus ("");
                    _mainWindow.SetPreviewStatus ("");
                }
                break;
            }

            UpdateMediaControlsVisibility();

            if (loadAction != null)
                loadAction();
        }

        public void TransitionToSpriteMode (SpriteMode newMode)
        {
            if (_currentSpriteMode == newMode && newMode != SpriteMode.Grid)
                return;

            var previousMode = _currentSpriteMode;
            _currentSpriteMode = newMode;

            _mainWindow.OnSpriteModeSwitched (previousMode, newMode);

            // Update media type based on sprite mode
            switch (newMode)
            {
            case SpriteMode.Static:
            case SpriteMode.Overlay:
                _currentMediaType = MediaType.Image;
                break;
            case SpriteMode.Grid:
                _currentMediaType = MediaType.Sprite;
                break;
            }

            UpdateMediaControlsVisibility();
        }

        public void ResetToDefault ()
        {
            if (!_audioPlaybackActive)
            {
                _currentMediaType    = MediaType.None;
                _currentSpriteMode   = SpriteMode.None;
                _backgroundMediaType = MediaType.None;
                UpdateMediaControlsVisibility();
            }
            ResetPreviewPanes();
        }

        private void ResetPreviewPanes ()
        {
            _mainWindow._imagePreviewHandler?.Reset();
            _mainWindow._videoPreviewHandler?.Reset();
            _mainWindow._textPreviewHandler?.Reset();
        }

        /// <summary>
        /// Configure media controls visibility based on current state
        /// </summary>
        private void ConfigureMediaControlsVisibility (MediaType mediaType)
        {
            var mc = _mainWindow._mediaControl;
            mc.SaveVolume();

            switch (mediaType)
            {
            case MediaType.Audio:
                mc.ControlPanel.Visibility = Visibility.Visible;
                mc.CycleButton.Visibility = Visibility.Visible;
                mc.AutoButton.Visibility = Visibility.Visible;
                mc.VolumeControlPanel.Visibility = Visibility.Visible;
                mc.UpdatePauseButtonForMediaType (MediaType.Audio, _mainWindow._audioPreviewHandler?.IsPaused ?? false);
                break;

            case MediaType.Video:
                mc.ControlPanel.Visibility = Visibility.Visible;
                mc.CycleButton.Visibility = Visibility.Visible;
                mc.AutoButton.Visibility = Visibility.Visible;
                mc.VolumeControlPanel.Visibility = Visibility.Visible;
                mc.UpdatePauseButtonForMediaType (MediaType.Video, !(_mainWindow._videoPreviewHandler?.IsPlaying ?? false));
                break;

            case MediaType.Sprite:
                mc.ControlPanel.Visibility = Visibility.Visible;
                mc.CycleButton.Visibility = Visibility.Collapsed;
                mc.AutoButton.Visibility = Visibility.Collapsed;
                mc.VolumeControlPanel.Visibility = Visibility.Collapsed;
                mc.UpdatePauseButtonForMediaType (MediaType.Sprite);
                break;

            case MediaType.Model:
                mc.ControlPanel.Visibility = Visibility.Visible;
                mc.CycleButton.Visibility = Visibility.Collapsed;
                mc.AutoButton.Visibility = Visibility.Collapsed;
                mc.VolumeControlPanel.Visibility = Visibility.Collapsed;
                mc.UpdatePauseButtonForMediaType (MediaType.Model);
                break;

            case MediaType.Text:
            case MediaType.Image:
            case MediaType.None:
            default:
                mc.ControlPanel.Visibility = Visibility.Collapsed;
                break;
            }

            mc.RestoreVolume();
        }

        /// <summary>
        /// Determine which media type controls should be shown
        /// </summary>
        private MediaType DeterminePlayingMediaType()
        {
            // If we have a background media type (audio playing during other preview), show those controls
            if (_backgroundMediaType != MediaType.None)
                return _backgroundMediaType;

            // Otherwise show controls for current media type
            return _currentMediaType;
        }

        /// <summary>
        /// Update media controls visibility based on current state
        /// </summary>
        private void UpdateMediaControlsVisibility()
        {
            _mainWindow.Dispatcher.Invoke (() =>
            {
                MediaType displayType = DeterminePlayingMediaType();
                ConfigureMediaControlsVisibility (displayType);
            });
        }

        public async void StartAudioPlayback (Entry entry)
        {
            _mainWindow._previewLoader.CancelCurrent();

            StopVideoPlayback();
            StopAudioPlayback();

            var preview = new PreviewFile
            {
                Entry = entry,
                Name = entry.Name,
                Path = _mainWindow.ViewModel.Path,
                TempFile = null
            };

            _audioPlaybackActive = true;
            _currentMediaType = MediaType.Audio;
            _backgroundMediaType = MediaType.None;
            UpdateMediaControlsVisibility();

            try
            {
                await _mainWindow._audioPreviewHandler.LoadContentAsync (preview, CancellationToken.None);
                UpdateAudioControls();
                _mainWindow.SetFileStatus ("");
            }
            catch (Exception ex)
            {
                _mainWindow.SetFileStatus (ex.Message);
                _audioPlaybackActive = false;
                _currentMediaType = MediaType.None;
                UpdateMediaControlsVisibility();
            }
        }

        public async void StartVideoPlayback (Entry entry)
        {
            _mainWindow._previewLoader.CancelCurrent();

            StopAllPlayback();

            var preview = new PreviewFile
            {
                Entry = entry,
                Name = entry.Name,
                Path = _mainWindow.ViewModel.Path,
                TempFile = null
            };

            _currentMediaType = MediaType.Video;
            UpdateMediaControlsVisibility();

            try
            {
                _mainWindow.ShowVideoPreview();
                await _mainWindow._videoPreviewHandler.LoadContentAsync (preview, CancellationToken.None);
                _mainWindow.SetFileStatus ("");
            }
            catch (Exception ex)
            {
                _mainWindow._videoPreviewHandler.Reset();
                _mainWindow.SetFileStatus (ex.Message);
                _currentMediaType = MediaType.None;
                UpdateMediaControlsVisibility();
            }
        }

        public void StopAllPlayback ()
        {
            StopAudioPlayback();
            StopVideoPlayback();
            StopAnimationPlayback();
        }

        public void PauseCurrentPlayback()
        {
            MediaType effectiveType = DeterminePlayingMediaType();
            switch (effectiveType)
            {
            case MediaType.Audio:
                PauseAudioPlayback();
                break;
            case MediaType.Video:
                if (_mainWindow._videoPreviewHandler.IsPlaying)
                    _mainWindow._videoPreviewHandler.Pause();
                else
                    _mainWindow._videoPreviewHandler.Play();
                break;
            case MediaType.Sprite:
                _mainWindow.ToggleSpriteAnimation();
                break;
            case MediaType.Model:
                _mainWindow.ToggleModelPlayback();
                break;
            }

            UpdateAudioControls();
        }

        public void StopCurrentPlayback()
        {
            MediaType effectiveType = DeterminePlayingMediaType();
            switch (effectiveType)
            {
            case MediaType.Audio:
                StopAudioPlayback();
                break;
            case MediaType.Video:
                StopVideoPlayback();
                break;
            case MediaType.Sprite:
                _mainWindow.StopSpriteAnimation();
                break;
            case MediaType.Model:
                _mainWindow.StopModelPlayback();
                break;
            }
            UpdateAudioControls();
        }

        public void SetVolume (float volume)
        {
            if (_mainWindow._audioPreviewHandler?.IsActive == true)
                _mainWindow._audioPreviewHandler.SetVolume (volume);
            if (_mainWindow._videoPreviewHandler?.IsActive == true)
                _mainWindow._videoPreviewHandler.SetVolume (volume);
        }

        private void StopAudioPlayback ()
        {
            if (_mainWindow._audioPreviewHandler != null && _mainWindow._audioPreviewHandler.IsActive)
            {
                _mainWindow._audioPreviewHandler.Reset();
                _audioPlaybackActive = false;
                _backgroundMediaType = MediaType.None;

                if (_currentMediaType == MediaType.Audio)
                {
                    _currentMediaType = MediaType.None;
                    _mainWindow.SetPreviewStatus ("");
                }

                UpdateMediaControlsVisibility();
            }
        }

        private void StopVideoPlayback ()
        {
            if (_mainWindow._videoPreviewHandler != null && _mainWindow._videoPreviewHandler.IsActive)
            {
                _mainWindow._videoPreviewHandler.Reset();
                if (_currentMediaType == MediaType.Video)
                {
                    _currentMediaType = MediaType.None;
                    _mainWindow.SetPreviewStatus ("");
                    UpdateMediaControlsVisibility();
                }
            }
        }

        private void StopAnimationPlayback ()
        {
            if (_mainWindow.m_animated_image_viewer != null)
                _mainWindow.m_animated_image_viewer.StopAnimation();
        }

        private void PauseAudioPlayback ()
        {
            _mainWindow._audioPreviewHandler?.Pause();
        }

        public void UpdateAudioControls ()
        {
            bool isPaused = false;
            MediaType controlType = DeterminePlayingMediaType();

            switch (controlType)
            {
                case MediaType.Audio:
                    isPaused = _mainWindow._audioPreviewHandler?.IsPaused ?? false;
                    break;
                case MediaType.Video:
                    isPaused = !(_mainWindow._videoPreviewHandler?.IsPlaying ?? false);
                    break;
                case MediaType.Sprite:
                    isPaused = !_mainWindow._isSpriteAnimating;
                    break;
            }

            _mainWindow._mediaControl.UpdateButtonStates (isPaused, _mainWindow._isAutoPlaying, _mainWindow._isAutoCycling);

            // Update pause button icon based on current state
            if (controlType == MediaType.Sprite)
                _mainWindow._mediaControl.UpdateSpriteButtons(!isPaused);
            else
                _mainWindow._mediaControl.UpdatePauseButtonForMediaType (controlType, isPaused);
        }

        public void OnAudioPlaybackStopped ()
        {
            try
            {
                if (_mainWindow._isAutoCycling || _mainWindow._isAutoPlaying)
                {
                    if (PlayNextAudio())
                        return;
                    else if (_mainWindow._isAutoPlaying && !_mainWindow._isAutoCycling)
                        _mainWindow.SetFileStatus (Localization.Format ("MsgReachedLast", Localization._T("Type_audio") ));
                }
                StopAudioPlayback();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine (ex.Message, "[OnPlaybackStopped]");
            }
        }

        private bool PlayNextAudio ()
        {
            int nextIndex = _mainWindow.GetNextFileIndex(
                _mainWindow.CurrentDirectory.SelectedIndex,
                allowCycling: _mainWindow._isAutoCycling,
                skipCurrent: !(_mainWindow._isAutoCycling && !_mainWindow._isAutoPlaying),
                fileFilter: entry => entry.Type == "audio");

            if (nextIndex < 0)
                return false;

            var nextEntry = _mainWindow.CurrentDirectory.Items[nextIndex] as EntryViewModel;
            _mainWindow.CurrentDirectory.SelectedIndex = nextIndex;

            if (_mainWindow.CurrentDirectory.IsFocused || _mainWindow.CurrentDirectory.IsKeyboardFocusWithin)
                _mainWindow.CurrentDirectory.ScrollIntoView (nextEntry);

            StartAudioPlayback (nextEntry.Source);
            return true;
        }

        public void OnMediaEnded ()
        {
            if (_currentMediaType == MediaType.Video)
            {
                if (_mainWindow._isAutoPlaying)
                {
                    if (PlayNextVideo())
                        return;
                    else if (!_mainWindow._isAutoCycling)
                    {
                        StopVideoPlayback();
                        _mainWindow.SetFileStatus (Localization.Format ("MsgReachedLast", Localization._T ("Type_video")));
                        return;
                    }
                }
                else if (_mainWindow._isAutoCycling)
                {
                    _mainWindow._videoPreviewHandler.Restart();
                    return;
                }
                StopVideoPlayback();
            }
        }

        private bool PlayNextVideo ()
        {
            int nextIndex = _mainWindow.GetNextFileIndex(
                _mainWindow.CurrentDirectory.SelectedIndex,
                allowCycling: _mainWindow._isAutoCycling,
                skipCurrent: !(_mainWindow._isAutoCycling && !_mainWindow._isAutoPlaying),
                fileFilter: entry => entry.Type == "video");

            if (nextIndex < 0)
                return false;

            var nextEntry = _mainWindow.CurrentDirectory.Items[nextIndex] as EntryViewModel;
            _mainWindow.CurrentDirectory.SelectedIndex = nextIndex;

            if (_mainWindow.CurrentDirectory.IsFocused || _mainWindow.CurrentDirectory.IsKeyboardFocusWithin)
                _mainWindow.CurrentDirectory.ScrollIntoView (nextEntry);

            StartVideoPlayback (nextEntry.Source);
            return true;
        }

        public void OnPlaybackStateChanged (bool isPlaying)
        {
            UpdateAudioControls();
        }

        private MediaType GetMediaType (string entryType)
        {
            switch (entryType)
            {
            case "audio":
                return MediaType.Audio;
            case "video":
                return MediaType.Video;
            case "sprite":
                return MediaType.Sprite;
            case "script":
            case "text":
                return MediaType.Text;
            case "image":
                return MediaType.Image;
            case "model":
                return MediaType.Model;
            default:
                return MediaType.None;
            }
        }
    }
}