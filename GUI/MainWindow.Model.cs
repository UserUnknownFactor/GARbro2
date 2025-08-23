using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using GARbro.GUI.Preview;

namespace GARbro.GUI
{
    public partial class MainWindow
    {
        private ObservableCollection<ParameterViewModel> _modelParameters = new ObservableCollection<ParameterViewModel>();
        private IModelPlugin _currentModelPlugin;
        private WriteableBitmap _modelRenderTarget;
        private System.Windows.Threading.DispatcherTimer _modelRenderTimer;
        private TextBlock _modelInfoText;

        private bool      _isModelPlaying = false;
        private float      _modelAnimTime = 0;
        private float  _modelAnimDuration = 0;
        private int          _renderSizeX = 768;
        private int          _renderSizeY = 768;
        private float                _fps = 0.032f;

        private void InitializeModelControls()
        {
            ModelParametersList.ItemsSource = _modelParameters;

            _modelRenderTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(_fps) // 30 FPS
            };

            _modelRenderTimer.Tick += OnModelRenderFrame;

            _modelInfoText = new TextBlock
            {
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = System.Windows.Media.Colors.Black,
                    BlurRadius = 4,
                    ShadowDepth = 2,
                    Opacity = 1.0,
                    Direction = 315
                }
            };
            PreviewPane.Children.Add(_modelInfoText);
            Panel.SetZIndex(_modelInfoText, 150);
            _modelInfoText.Visibility = Visibility.Collapsed;
        }

        internal void ShowModelControls(ModelContext context, IModelPlugin plugin)
        {
            _currentModelPlugin = plugin;

            ModelControlPanel.Visibility = Visibility.Visible;
            MediaControlPanel.Visibility = Visibility.Visible;
            _mediaControl?.ConfigureForMediaType(MediaType.Model);

            // Update state machine
            _previewStateMachine.TransitionToMedia(MediaType.Model, null);

            // Clear and populate animations
            ModelAnimationSelector.Items.Clear();
            if (context.Animations != null && context.Animations.Count > 0)
            {
                foreach (var anim in context.Animations)
                    ModelAnimationSelector.Items.Add(anim);
                ModelAnimationSelector.IsEnabled = true;
                ModelAnimationSelector.SelectedIndex = 0;
                plugin.SetAnimation(context.Animations[0]);
                _modelAnimDuration = plugin.GetAnimationDuration(context.Animations[0]);
 
            }
            else
                ModelAnimationSelector.IsEnabled = false;

            _modelParameters.Clear();
            foreach (var param in context.Parameters.Values)
                _modelParameters.Add(new ParameterViewModel(param, plugin));

            ModelParametersButton.IsEnabled = _modelParameters.Count > 0;

            _modelRenderTarget = new WriteableBitmap(
                _renderSizeX, _renderSizeY, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32, null);

            ImageCanvas.Source = _modelRenderTarget;
            ImageCanvas.Visibility = Visibility.Visible;
            m_animated_image_viewer.Visibility = Visibility.Collapsed;
            TextView.Visibility = Visibility.Collapsed;

            if (_modelInfoText != null)
            {
                UpdateModelInfoText(context.Info);
                _modelInfoText.Visibility = Visibility.Visible;
            }

            StartModelPlayback();
        }

        internal void HideModelControls()
        {
            ModelControlPanel.Visibility = Visibility.Collapsed;
            ModelParametersPanel.Visibility = Visibility.Collapsed;
            if (_modelInfoText != null)
                _modelInfoText.Visibility = Visibility.Collapsed;
            StopModelPlayback(true);
            _currentModelPlugin = null;
            _modelRenderTarget = null;
            ImageCanvas.Source = null;
        }

        internal void StartModelPlayback()
        {
            _isModelPlaying = true;
            _modelRenderTimer?.Start();
            _mediaControl?.UpdateSpriteButtons(true);
        }

        internal void StopModelPlayback(bool final = false)
        {
            _isModelPlaying = false;
            _modelRenderTimer?.Stop();
            _mediaControl?.UpdateSpriteButtons(false);

            if (final)
                _modelAnimTime = 0;

            if (_currentModelPlugin != null && ModelAnimationSelector.SelectedItem != null)
                _currentModelPlugin.SetAnimation(ModelAnimationSelector.SelectedItem.ToString());
        }

        internal void ToggleModelPlayback()
        {
            if (_isModelPlaying)
                StopModelPlayback();
            else
                StartModelPlayback();
        }

        private void OnModelRenderFrame(object sender, EventArgs e)
        {
            if (_currentModelPlugin != null && _modelRenderTarget != null && _isModelPlaying)
            {
                try
                {
                    _currentModelPlugin.Update(_fps);
                    _currentModelPlugin.Render(_modelRenderTarget);

                    _modelAnimTime += _fps;
                    if (_modelAnimDuration > 0 && _modelAnimTime > _modelAnimDuration)
                        _modelAnimTime = _modelAnimTime % _modelAnimDuration;

                    UpdateModelInfoText(null);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"Model render error: {ex.Message}");
                    SetFileStatus($"Model render error: {ex.Message}");
                }
            }
        }

        private void UpdateModelInfoText(string baseInfo)
        {
            if (_modelInfoText == null) return;

            var animName = ModelAnimationSelector.SelectedItem?.ToString() ?? "None";
            var timeStr = $"{_modelAnimTime:F2}s";
            var durationStr = _modelAnimDuration > 0 ? $"{_modelAnimDuration:F2}s" : "N/A";

            var text = "";
            if (!string.IsNullOrEmpty(baseInfo))
            {
                text = baseInfo + "\n";
            }

            text += $"Animation: {animName}\n";
            text += $"Time: {timeStr} / {durationStr}";

            var debugInfo = _currentModelPlugin?.GetDebugInfo();
            if (!string.IsNullOrEmpty(debugInfo))
                text += "\n" + debugInfo;

            _modelInfoText.Text = text;
        }

        private void ModelAnimationSelector_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_currentModelPlugin != null && ModelAnimationSelector.SelectedItem != null)
            {
                var animName = ModelAnimationSelector.SelectedItem.ToString();
                _currentModelPlugin.SetAnimation(animName);
                _modelAnimTime = 0;
                _modelAnimDuration = _currentModelPlugin.GetAnimationDuration(animName);
                UpdateModelInfoText(null);
            }
        }

        private void ModelParametersButton_Click(object sender, RoutedEventArgs e)
        {
            ModelParametersPanel.Visibility = Visibility.Visible;
            ModelParametersButton.IsEnabled = false;
        }

        private void CloseModelParametersButton_Click(object sender, RoutedEventArgs e)
        {
            ModelParametersPanel.Visibility = Visibility.Collapsed;
            ModelParametersButton.IsEnabled = true;
        }

        private void ModelParameterSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var slider = sender as Slider;
            if (slider?.Tag != null && _currentModelPlugin != null)
                _currentModelPlugin.SetParameter(slider.Tag.ToString(), (float)e.NewValue);
        }
    }
}