using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GARbro.GUI
{
    /// <summary>
    /// Manages XAML-defined media playback controls UI elements
    /// </summary>
    public class MediaControl
    {
        private readonly StackPanel _controlPanel;
        private readonly StackPanel _volumeControlPanel;
        private readonly Button     _pauseButton;
        private readonly Button     _stopButton;
        private readonly Button     _cycleButton;
        private readonly Button     _autoButton;
        private readonly Slider     _volumeSlider;

        private readonly Path       _pauseIcon;
        private readonly Path       _cycleIcon;
        private readonly Path       _autoIcon;

        private readonly Ellipse    _pauseEllipse;
        private readonly Ellipse    _cycleEllipse;
        private readonly Ellipse    _autoEllipse;

        private double _savedVolume = 0.8;

        private readonly Brush _pauseActiveColor = new SolidColorBrush (Color.FromRgb (0xFF, 0xCC, 0x80));  // brighter pastel orange
        private readonly Brush _playActiveColor  = new SolidColorBrush (Color.FromRgb (0x80, 0xE6, 0x80));  // green for play
        private readonly Brush _cycleActiveColor = new SolidColorBrush (Color.FromRgb (0xCC, 0x99, 0xE6));  // brighter pastel purple
        private readonly Brush _autoActiveColor  = new SolidColorBrush (Color.FromRgb (0x80, 0xE6, 0x80));  // brighter pastel green
        private readonly Brush _inactiveColor    = new SolidColorBrush (Color.FromRgb (0xB0, 0xB0, 0xB0));  // gray

        // Public properties for PreviewStateMachine to access
        public StackPanel ControlPanel => _controlPanel;
        public Button PauseButton => _pauseButton;
        public Button StopButton => _stopButton;
        public Button CycleButton => _cycleButton;
        public Button AutoButton => _autoButton;
        public StackPanel VolumeControlPanel => _volumeControlPanel;
        public Slider VolumeSlider => _volumeSlider;

        public double Volume
        {
            get { return _volumeSlider?.Value ?? _savedVolume; }
            set
            {
                _savedVolume = value;
                if (_volumeSlider != null)
                    _volumeSlider.Value = value;
            }
        }

        public MediaControl (StackPanel controlPanel, Button pauseButton, Button stopButton, 
                          Button cycleButton, Button autoButton, Slider volumeSlider, StackPanel volumeControlPanel)
        {
            _controlPanel       = controlPanel;
            _pauseButton        = pauseButton;
            _stopButton         = stopButton;
            _cycleButton        = cycleButton;
            _autoButton         = autoButton;
            _volumeSlider       = volumeSlider;
            _volumeControlPanel = volumeControlPanel;

            ExtractButtonElements (_pauseButton, out _pauseIcon, out _pauseEllipse);
            ExtractButtonElements (_cycleButton, out _cycleIcon, out _cycleEllipse);
            ExtractButtonElements (_autoButton,  out _autoIcon,  out _autoEllipse);
        }

        private void ExtractButtonElements (Button button, out Path icon, out Ellipse ellipse)
        {
            icon = null;
            ellipse = null;

            if (button?.Content is Viewbox viewbox && viewbox.Child is Canvas canvas)
            {
                foreach (var child in canvas.Children)
                {
                    if (child is Path path) icon = path;
                    else if (child is Ellipse ell) ellipse = ell;
                }
            }
        }

        /// <summary>
        /// Save current volume before controls are hidden
        /// </summary>
        public void SaveVolume()
        {
            if (_volumeSlider != null && _volumeSlider.Visibility == Visibility.Visible)
                _savedVolume = _volumeSlider.Value;
        }

        /// <summary>
        /// Restore saved volume when controls become visible
        /// </summary>
        public void RestoreVolume()
        {
            if (_volumeSlider != null && _volumeSlider.Visibility == Visibility.Visible)
                _volumeSlider.Value = _savedVolume;
        }

        /// <summary>
        /// Update button states and tooltips based on playback state
        /// </summary>
        public void UpdateButtonStates (bool isPaused, bool isAutoPlaying, bool isAutoCycling)
        {
            _pauseButton.ToolTip = isPaused ? Localization._T("TooltipResume") : Localization._T("TooltipPause");
            if (_pauseEllipse != null)
                _pauseEllipse.Fill = isPaused ? _inactiveColor : _pauseActiveColor;

            _autoButton.ToolTip = isAutoPlaying ? Localization._T("TooltipAutoOn") : Localization._T("TooltipAutoOff");
            if (_autoEllipse != null)
                _autoEllipse.Fill = isAutoPlaying ? _autoActiveColor : _inactiveColor;

            _cycleButton.ToolTip = isAutoCycling ? Localization._T("TooltipCycleOn") : Localization._T("TooltipCycleOff");
            if (_cycleEllipse != null)
                _cycleEllipse.Fill = isAutoCycling ? _cycleActiveColor : _inactiveColor;
        }

        /// <summary>
        /// Update sprite animation buttons
        /// </summary>
        public void UpdateSpriteButtons (bool isPlaying)
        {
            if (isPlaying)
            {
                SetPauseButtonIcon();
                _pauseButton.ToolTip = Localization._T("PauseAnimation");
                if (_pauseEllipse != null)
                    _pauseEllipse.Fill = _pauseActiveColor;
            }
            else
            {
                SetPlayButtonIcon();
                _pauseButton.ToolTip = Localization._T("PlayAnimation");
                if (_pauseEllipse != null)
                    _pauseEllipse.Fill = _playActiveColor;
            }
        }

        /// <summary>
        /// Set pause button to show pause icon
        /// </summary>
        public void SetPauseButtonIcon()
        {
            if (_pauseIcon != null)
            {
                // Pause icon (two vertical bars)
                _pauseIcon.Data = Geometry.Parse ("M 10 10 L 10 20 L 13 20 L 13 10 Z M 17 10 L 17 20 L 20 20 L 20 10 Z");
            }
        }

        /// <summary>
        /// Set pause button to show play icon
        /// </summary>
        public void SetPlayButtonIcon()
        {
            if (_pauseIcon != null)
            {
                // Play icon (triangle pointing right)
                _pauseIcon.Data = Geometry.Parse ("M 10 8 L 10 22 L 22 15 Z");
            }
        }

        /// <summary>
        /// Update pause button appearance for current media type
        /// </summary>
        public void UpdatePauseButtonForMediaType (MediaType mediaType, bool isPaused = false)
        {
            switch (mediaType)
            {
                case MediaType.Audio:
                case MediaType.Video:
                    if (isPaused)
                        SetPlayButtonIcon();
                    else
                        SetPauseButtonIcon();
                    break;
                    
                case MediaType.Sprite:
                case MediaType.Model:
                    SetPlayButtonIcon();
                    break;
            }
        }
    }
}