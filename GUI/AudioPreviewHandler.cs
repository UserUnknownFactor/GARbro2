using System;
using System.Windows.Threading;
using System.Threading.Tasks;

using GameRes;
using NAudio.Wave;
using System.Threading;
using NAudio.CoreAudioApi;

namespace GARbro.GUI.Preview
{
    public class AudioPreviewHandler : PreviewHandlerBase
    {
        private readonly MainWindow _mainWindow;
        private         IWavePlayer _audioDevice;
        private          WaveStream _currentAudio;
        private     DispatcherTimer _playbackTimer;
        private                bool _isAudioPaused = false;
        private                bool _useWasapi = false;
        //private                 int _desiredLatency = 200;

        public override bool IsActive => _audioDevice != null;
        
        public bool IsPlaying => _audioDevice?.PlaybackState == PlaybackState.Playing;
        public bool IsPaused  => _isAudioPaused;

        public AudioPreviewHandler (MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public override async Task LoadContentAsync (PreviewFile preview, CancellationToken cancellationToken)
        {
            IBinaryStream input = null;
            SoundInput sound    = null;
            try
            {
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    input = VFS.OpenBinaryStream (preview.Entry);
                    _mainWindow.RegisterStream (input);
                    FormatCatalog.Instance.LastError = null;
                    sound = AudioFormat.Read (input);

                    if (null == sound)
                    {
                        if (null != FormatCatalog.Instance.LastError)
                            throw FormatCatalog.Instance.LastError;
                        throw new InvalidFormatException (Localization._T ("FormatNotRecognized"));
                    }
                }, cancellationToken);

                Reset();

                _currentAudio = new WaveStreamImpl (sound);
                _mainWindow.RegisterStream (_currentAudio);

                SetUpAudioDevice (sound);
                _mainWindow.UpdateAudioControls();

                var fmt = _currentAudio.WaveFormat;
                string autoStatus = _mainWindow.GetAutoPlayStatus();

                string filename = VFS.GetFileName(preview.Entry.Name);

                _mainWindow.SetPreviewStatus(
                    Localization.Format ("MsgPlaying",
                    filename, autoStatus, fmt.SampleRate, sound.SourceBitrate / 1000,
                    "0:00", _currentAudio.TotalTime.ToString ("m':'ss")));

                SetUpStatusTimer (preview.Entry, sound, fmt);
            }
            catch (Exception ex) when (ex is ObjectDisposedException || ex is OperationCanceledException)
            {
                CleanupOnError (input, sound);
            }
            catch (Exception ex)
            {
                _mainWindow.SetFileStatus (ex.Message);
                CleanupOnError (input, sound);
                throw;
            }
        }

        private void CleanupOnError (IBinaryStream input, SoundInput sound)
        {
            sound?.Dispose();
            if (input != null)
            {
                _mainWindow.RemoveStream (input);
                input.Dispose();
            }
        }

        private void SetUpAudioDevice (SoundInput sound)
        {
            /*try
            {
                // NOTE: it hiccups on delays and handles end of track detection worse even in Shared mode
                _audioDevice = new WasapiOut(AudioClientShareMode.Shared, false, _desiredLatency);
                _useWasapi   = true;
            }
            catch
            {*/
                _audioDevice = new WaveOutEvent();
                _useWasapi   = false;
            //}
            _mainWindow.RegisterStream (_audioDevice);

            if ("wav" == sound.SourceFormat || 8 == sound.Format.BitsPerSample)
                _audioDevice.Init (_currentAudio.ToSampleProvider());
            else
                _audioDevice.Init (_currentAudio);
                
            _audioDevice.PlaybackStopped += _mainWindow.OnPlaybackStopped;
            
            SetVolume((float)_mainWindow.MediaVolumeSlider.Value);

            _audioDevice.Play();
            _isAudioPaused = false;
        }

        private void SetUpStatusTimer (Entry entry, SoundInput sound, NAudio.Wave.WaveFormat fmt)
        {
            _playbackTimer = new DispatcherTimer();
            _playbackTimer.Interval = TimeSpan.FromMilliseconds (200);
            _playbackTimer.Tick += (s, e) => UpdatePlaybackTime (entry, fmt, sound.SourceBitrate);
            _playbackTimer.Start();
        }

        /// <summary>
        /// Updates playback time string
        /// </summary>
        private void UpdatePlaybackTime (Entry entry, NAudio.Wave.WaveFormat fmt, int sourceBitrate)
        {
            if (this.IsActive)
            {
                var currentTime   = _currentAudio.CurrentTime;
                var totalTime     = _currentAudio.TotalTime;
                string autoStatus = _mainWindow.GetAutoPlayStatus();
                string filename   = VFS.GetFileName(entry.Name);

                _mainWindow.SetPreviewStatus(
                    Localization.Format ("MsgPlaying",
                    filename, autoStatus, fmt.SampleRate, sourceBitrate / 1000,
                    currentTime.ToString ("m':'ss"), totalTime.ToString ("m':'ss")));
            }
        }

        public void Pause ()
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
                _audioDevice.Volume = _useWasapi ? volume * 0.4f : volume;
        }

        public override void Reset ()
        {
            _playbackTimer?.Stop();
            _playbackTimer = null;

            if (_audioDevice != null)
            {
                _audioDevice.PlaybackStopped -= _mainWindow.OnPlaybackStopped;
                System.Threading.Thread.Sleep (200); // roughly _audioDevice.DesiredLatency
                _audioDevice.Stop();
                _mainWindow.RemoveStream (_audioDevice);
                _audioDevice.Dispose();
                _audioDevice = null;
                _mainWindow.SetPreviewStatus("");
            }

            if (_currentAudio != null)
            {
                _mainWindow.RemoveStream (_currentAudio);
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