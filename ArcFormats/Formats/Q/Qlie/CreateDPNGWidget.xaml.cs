using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

using GameRes.Formats.Properties;
using GameRes.Formats.Qlie;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for CreateDpngWidget.xaml
    /// </summary>
    public partial class CreateDpngWidget : Grid
    {
        public CreateDpngWidget()
        {
            InitializeComponent();
            
            // Set up validation
            CanvasWidthBox.PreviewTextInput += NumberValidationTextBox;
            CanvasHeightBox.PreviewTextInput += NumberValidationTextBox;
            
            // Load saved settings or defaults
            //if (Settings.Default.DPNGCanvasWidth == 0)
                //Settings.Default.DPNGCanvasWidth = 1920;
            //if (Settings.Default.DPNGCanvasHeight == 0)
                //Settings.Default.DPNGCanvasHeight = 1080;
                
            // Bind auto-detect event
            AutoDetectCheckBox.Checked += AutoDetectCheckBox_Changed;
            AutoDetectCheckBox.Unchecked += AutoDetectCheckBox_Changed;
        }
        
        private void NumberValidationTextBox(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // Only allow numeric input
            e.Handled = !IsTextNumeric(e.Text);
        }
        
        private static bool IsTextNumeric(string text)
        {
            return uint.TryParse(text, out _);
        }
        
        private void AutoDetectCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool isChecked = AutoDetectCheckBox.IsChecked ?? false;
            CanvasWidthBox.IsEnabled = !isChecked;
            CanvasHeightBox.IsEnabled = !isChecked;
            
            if (isChecked)
            {
                CanvasWidthBox.Text = "Auto";
                CanvasHeightBox.Text = "Auto";
            }
            else
            {
                CanvasWidthBox.Text = "1920";//Settings.Default.DPNGCanvasWidth.ToString();
                CanvasHeightBox.Text = "1080"; //Settings.Default.DPNGCanvasHeight.ToString();
            }
        }
        
        public DpngOptions GetOptions()
        {
            var options = new DpngOptions();
            
            if (AutoDetectCheckBox.IsChecked ?? false)
            {
                // Return 0,0 to signal auto-detection
                options.CanvasWidth = 0;
                options.CanvasHeight = 0;
            }
            else
            {
                if (uint.TryParse(CanvasWidthBox.Text, out uint width))
                    options.CanvasWidth = width;
                else
                    options.CanvasWidth = 1920;
                    
                if (uint.TryParse(CanvasHeightBox.Text, out uint height))
                    options.CanvasHeight = height;
                else
                    options.CanvasHeight = 1080;
            }
            
            return options;
        }
    }
}