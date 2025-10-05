using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace GameRes.Formats.GUI
{
    public partial class CreateVFFWidget : Grid, IExtensionChangeNotifier
    {
        public event EventHandler<ExtensionChangedEventArgs> ExtensionChanged;

        public CreateVFFWidget ()
        {
            InitializeComponent();
            ScrambleSeedBox.IsEnabled = ScrambleCheckBox.IsChecked ?? false;
            SeedLabel.IsEnabled = ScrambleCheckBox.IsChecked ?? false;
        }
        
        public string CurrentExtension
        {
            get 
            { 
                // If source EXE is specified and exists, suggest .exe extension
                if (!string.IsNullOrEmpty(SourceExeBox.Text) && File.Exists(SourceExeBox.Text))
                    return "exe";
                return "dat";  // Default extension
            }
        }

        public bool PreserveOriginalLayout
        {
            get { return PreserveLayoutCheckBox.IsChecked ?? false; }
        }

        public bool UseCompression 
        { 
            get { return CompressionCheckBox.IsChecked ?? false; }
        }

        public bool UseScrambling 
        { 
            get { return ScrambleCheckBox.IsChecked ?? false; }
        }

        public uint ScrambleSeed 
        { 
            get 
            { 
                uint seed;
                if (uint.TryParse (ScrambleSeedBox.Text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out seed))
                    return seed;
                else if (uint.TryParse (ScrambleSeedBox.Text, out seed))
                    return seed;
                else
                    return 0xF8EA;
            }
        }

        public string SourceExePath
        {
            get { return SourceExeBox.Text; }
        }

        private void ScrambleCheckBox_Checked (object sender, RoutedEventArgs e)
        {
            if (!PreserveOriginalLayout)  // Only enable if not preserving
            {
                ScrambleSeedBox.IsEnabled = true;
                SeedLabel.IsEnabled = true;
                if (string.IsNullOrWhiteSpace (ScrambleSeedBox.Text))
                {
                    ScrambleSeedBox.Text = "F8EA";
                }
            }
        }

        private void ScrambleCheckBox_Unchecked (object sender, RoutedEventArgs e)
        {
            ScrambleSeedBox.IsEnabled = false;
            SeedLabel.IsEnabled = false;
        }

        private void PreserveLayoutCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            CompressionSettingsGroup.IsEnabled = false;
        }
        
        private void PreserveLayoutCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            CompressionSettingsGroup.IsEnabled = true;

            ScrambleSeedBox.IsEnabled = ScrambleCheckBox.IsChecked ?? false;
            SeedLabel.IsEnabled = ScrambleCheckBox.IsChecked ?? false;
        }

        private void BrowseExeButton_Click (object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true
            };
            
            if (dlg.ShowDialog() == true)
            {
                SourceExeBox.Text = dlg.FileName;
                ExtensionChanged?.Invoke(this, new ExtensionChangedEventArgs(CurrentExtension));
            }
        }
        
        private void SourceExeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ExtensionChanged?.Invoke(this, new ExtensionChangedEventArgs(CurrentExtension));
        }
    }
}