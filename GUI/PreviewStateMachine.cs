using System;
using System.Collections.Generic;
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
        private MediaType _currentMediaType = MediaType.None;
        private SpriteMode _currentSpriteMode = SpriteMode.None;
        private bool _audioPlaybackActive = false;

        public MediaType   CurrentMediaType { get { return _currentMediaType; } }
        public SpriteMode CurrentSpriteMode { get { return _currentSpriteMode; } }

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
                break;

            case MediaType.Text:
                _mainWindow._imagePreviewHandler.Reset();
                _currentMediaType = MediaType.Text;
                _currentSpriteMode = SpriteMode.None;
                break;

            case MediaType.Image:
                _currentMediaType = MediaType.Image;
                if (_audioPlaybackActive)
                {
                    // Keep audio controls visible
                    if (loadAction != null)
                        loadAction();
                    return;
                }
                break;

            case MediaType.None:
                _currentSpriteMode = SpriteMode.None;
                if (_audioPlaybackActive)
                {
                    // Keep audio controls visible
                    if (loadAction != null)
                        loadAction();
                    return;
                }
                else
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
                _currentMediaType = MediaType.None;
                _currentSpriteMode = SpriteMode.None;
                UpdateMediaControlsVisibility();
                ResetPreviewPanes();
            }
            else
            {
                ResetPreviewPanes();
            }
        }

        private void ResetPreviewPanes ()
        {
            _mainWindow._imagePreviewHandler?.Reset();
            _mainWindow._videoPreviewHandler?.Reset();
            _mainWindow._textPreviewHandler?.Reset();
        }

        public void StartAudioPlayback (Entry entry)
        {
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
            UpdateMediaControlsVisibility();

            _mainWindow._audioPreviewHandler.LoadContent (preview);
            _mainWindow._mediaControl.SetPauseButtonIcon();
        }

        public void StartVideoPlayback (Entry entry)
        {
            var preview = new PreviewFile
            {
                Entry = entry,
                Name = entry.Name,
                Path = _mainWindow.ViewModel.Path,
                TempFile = null
            };

            TransitionToMedia(MediaType.Video, () => 
            {
                try
                {
                    _mainWindow.ShowVideoPreview();
                    _mainWindow._videoPreviewHandler.LoadContent(preview);
                    _mainWindow.SetFileStatus("");
                }
                catch (Exception ex)
                {
                    _mainWindow._videoPreviewHandler.Reset();
                    _mainWindow.SetFileStatus(ex.Message);
                    _currentMediaType = MediaType.None;
                    UpdateMediaControlsVisibility();
                }
            });
        }

        public void StopAllPlayback ()
        {
            StopAudioPlayback();
            StopVideoPlayback();
            StopAnimationPlayback();
        }

        public void StopCurrentPlayback ()
        {
            switch (_currentMediaType)
            {
            case MediaType.Audio:
                StopAudioPlayback();
                break;
            case MediaType.Video:
                StopVideoPlayback();
                break;
            case MediaType.Sprite:
                _mainWindow.StopSpriteAnimation();
                //_mainWindow.SpriteLayoutCombo.SelectedIndex = 0;
                break;
            case MediaType.Model:
                _mainWindow.StopModelPlayback();
                break;
            }
        }

        public void PauseCurrentPlayback ()
        {
            switch (_currentMediaType)
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
        }

        public void SetVolume (float volume)
        {
            if (_currentMediaType == MediaType.Audio && _mainWindow._audioPreviewHandler != null)
                _mainWindow._audioPreviewHandler.SetVolume (volume);
            else if (_currentMediaType == MediaType.Video && _mainWindow._videoPreviewHandler != null)
                _mainWindow._videoPreviewHandler.SetVolume (volume);
        }

        private void StopAudioPlayback ()
        {
            if (_mainWindow._audioPreviewHandler != null && _mainWindow._audioPreviewHandler.IsActive)
            {
                _mainWindow._audioPreviewHandler.Reset();
                _audioPlaybackActive = false;
                if (_currentMediaType == MediaType.Audio)
                {
                    _currentMediaType = MediaType.None;
                    UpdateMediaControlsVisibility();
                    _mainWindow.SetPreviewStatus ("");
                }
            }
        }

        private void StopVideoPlayback ()
        {
            if (_mainWindow._videoPreviewHandler != null && _mainWindow._videoPreviewHandler.IsActive)
            {
                _mainWindow._videoPreviewHandler.Reset();
                if (_currentMediaType == MediaType.Video && !_audioPlaybackActive)
                {
                    _currentMediaType = MediaType.None;
                    UpdateMediaControlsVisibility();
                    _mainWindow.SetPreviewStatus ("");
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
            _mainWindow._mediaControl.SetPlayButtonIcon();
            _mainWindow._audioPreviewHandler?.Pause();
        }

        public void UpdateAudioControls ()
        {
            bool isPaused = _mainWindow._audioPreviewHandler?.IsPaused ?? false;
            if (_currentMediaType == MediaType.Video)
                isPaused = !(_mainWindow._videoPreviewHandler?.IsPlaying ?? false);

            _mainWindow._mediaControl.UpdateButtonStates (isPaused, _mainWindow._isAutoPlaying, _mainWindow._isAutoCycling);
        }

        private void UpdateMediaControlsVisibility()
        {
            _mainWindow.Dispatcher.Invoke (() =>
                _mainWindow._mediaControl.ConfigureForMediaType (_currentMediaType));
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
                            _mainWindow.SetFileStatus (Localization._T("MsgReachedLastAudio"));
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
                _mainWindow.CurrentDirectory.ScrollIntoView(nextEntry);

            StartAudioPlayback (nextEntry.Source);
            return true;
        }

        public void OnMediaEnded ()
        {
            if (_currentMediaType == MediaType.Video)
                StopVideoPlayback();
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