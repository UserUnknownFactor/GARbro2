using System;
using System.Windows.Threading;
using GameRes;
using NAudio.Wave;

namespace GARbro.GUI.Preview
{
    public class AudioPreviewHandler : PreviewHandlerBase
    {
        private readonly MainWindow _mainWindow;
        private         IWavePlayer _audioDevice;
        private          WaveStream _currentAudio;
        private     DispatcherTimer _playbackTimer;
        private                bool _isAudioPaused = false;

        public override bool IsActive => _audioDevice != null;
        
        public bool IsPlaying => _audioDevice?.PlaybackState == PlaybackState.Playing;
        public bool IsPaused => _isAudioPaused;

        public AudioPreviewHandler(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public override void LoadContent(PreviewFile preview)
        {
            IBinaryStream input = null;
            SoundInput sound = null;
            try
            {
                input = VFS.OpenBinaryStream(preview.Entry);
                _mainWindow.RegisterStream(input);

                FormatCatalog.Instance.LastError = null;
                sound = AudioFormat.Read(input);
                if (null == sound)
                {
                    if (null != FormatCatalog.Instance.LastError)
                        throw FormatCatalog.Instance.LastError;
                    return;
                }

                Reset();

                _currentAudio = new WaveStreamImpl(sound);
                _mainWindow.RegisterStream(_currentAudio);

                SetUpAudioDevice(sound);
                _mainWindow.UpdateAudioControls();
                _mainWindow.UpdateMediaControlsVisibility(MediaType.Audio);

                var fmt = _currentAudio.WaveFormat;
                string autoStatus = _mainWindow.GetAutoPlayStatus();
                _mainWindow.SetPreviewStatus(
                    Localization.Format("MsgPlaying",
                    preview.Entry.Name, autoStatus, fmt.SampleRate, sound.SourceBitrate / 1000,
                    "0:00", _currentAudio.TotalTime.ToString("m':'ss")));

                SetUpStatusTimer(preview.Entry, sound, fmt);

                input = null; // Ownership transferred
            }
            catch (Exception X)
            {
                _mainWindow.SetFileStatus(X.Message);
                sound?.Dispose();
            }
            finally
            {
                if (input != null)
                {
                    _mainWindow.RemoveStream(input);
                    input.Dispose();
                }
            }
        }

        private void SetUpAudioDevice(SoundInput sound)
        {
            _audioDevice = new WaveOutEvent();
            _mainWindow.RegisterStream(_audioDevice);

            if ("wav" == sound.SourceFormat || 8 == sound.Format.BitsPerSample)
                _audioDevice.Init(_currentAudio.ToSampleProvider());
            else
                _audioDevice.Init(_currentAudio);
                
            _audioDevice.PlaybackStopped += _mainWindow.OnPlaybackStopped;
            _audioDevice.Volume = (float)_mainWindow.MediaVolumeSlider.Value;
            _audioDevice.Play();
            _isAudioPaused = false;
        }

        private void SetUpStatusTimer(Entry entry, SoundInput sound, NAudio.Wave.WaveFormat fmt)
        {
            _playbackTimer  = new DispatcherTimer();
            _playbackTimer.Interval = TimeSpan.FromMilliseconds(200);
            _playbackTimer.Tick += (s, e) => UpdatePlaybackTime(entry, fmt, sound.SourceBitrate);
            _playbackTimer.Start();
        }

        /// <summary>
        /// Update playback time display
        /// </summary>
        private void UpdatePlaybackTime(Entry entry, NAudio.Wave.WaveFormat fmt, int sourceBitrate)
        {
            if (this.IsActive)
            {
                var currentTime = _currentAudio.CurrentTime;
                var totalTime = _currentAudio.TotalTime;
                string autoStatus = _mainWindow.GetAutoPlayStatus();

                _mainWindow.SetPreviewStatus(
                    Localization.Format("MsgPlaying",
                    entry.Name, autoStatus, fmt.SampleRate, sourceBitrate / 1000,
                    currentTime.ToString("m':'ss"), totalTime.ToString("m':'ss")));
            }
        }

        public void Pause()
        {
            if (_audioDevice != null)
            {
                if (_isAudioPaused)
                {
                    _audioDevice.Play();
                    _isAudioPaused = false;
                    _playbackTimer?.Start();
                }
                else
                {
                    _audioDevice.Pause();
                    _isAudioPaused = true;
                    _playbackTimer?.Stop();
                }
                _mainWindow.UpdateAudioControls();
            }
        }

        public void SetVolume(float volume)
        {
            if (_audioDevice != null)
                _audioDevice.Volume = volume;
        }

        public override void Reset()
        {
            _playbackTimer?.Stop();
            _playbackTimer = null;

            if (_audioDevice != null)
            {
                _audioDevice.PlaybackStopped -= _mainWindow.OnPlaybackStopped;
                System.Threading.Thread.Sleep(200); // _audioDevice.DesiredLatency
                _audioDevice.Stop();
                _mainWindow.RemoveStream(_audioDevice);
                _audioDevice.Dispose();
                _audioDevice = null;
            }

            if (_currentAudio != null)
            {
                _mainWindow.RemoveStream(_currentAudio);
                _currentAudio.Dispose();
                _currentAudio = null;
            }

            _isAudioPaused = false;
        }

        #region IDisposable members
        protected override void Dispose (bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                    Reset();

                base.Dispose (disposing);
            }
        }
        #endregion
    }
}