using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameRes.Formats.Qlie;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for CreateQLIEWidget.xaml
    /// </summary>
    public partial class CreateQLIEWidget : Grid
    {
        public CreateQLIEWidget ()
        {
            InitializeComponent ();
            
            // Populate scheme combo with "(None)" option
            var schemes = new string[] { Localization._T("QLIEDefaultScheme") }
                .Concat(PackOpener.KnownKeys.Keys.OrderBy(x => x));
            SchemeCombo.ItemsSource = schemes;
            
            // Set default selections
            if (SchemeCombo.Items.Count > 0)
                SchemeCombo.SelectedIndex = 0;
            
            // Version 3.1 is selected by default (index 3)
            if (VersionCombo.Items.Count > 3)
                VersionCombo.SelectedIndex = 3;
            
            SchemeCombo.SelectionChanged += OnSchemeChanged;
        }
        
        void OnSchemeChanged (object sender, SelectionChangedEventArgs e)
        {
            var scheme = SchemeCombo.SelectedItem as string;
            if (scheme != null && scheme != Localization._T("QLIEDefaultScheme"))
            {
                GameKeyText.Text = scheme;
            }
            else
            {
                GameKeyText.Text = "";
            }
        }
        
        public Version GetVersion()
        {
            string selectedVersion = VersionCombo.SelectedItem as string;
            if (!string.IsNullOrEmpty(selectedVersion))
            {
                var parts = selectedVersion.Split('.');
                if (parts.Length == 2)
                {
                    if (int.TryParse(parts[0], out int major) && 
                        int.TryParse(parts[1], out int minor))
                    {
                        return new Version(major, minor);
                    }
                }
            }
            return new Version(3, 1); // Default to 3.1
        }
        
        public byte[] GetGameKey()
        {
            var scheme = GameKeyText.Text;
            if (!string.IsNullOrEmpty(scheme))
            {
                byte[] keyData;
                if (PackOpener.KnownKeys.TryGetValue(scheme, out keyData))
                {
                    return keyData;
                }
            }
            return null;
        }
        
        public bool CompressFiles
        {
            get { return CompressCheck.IsChecked ?? false; }
        }
    }
}