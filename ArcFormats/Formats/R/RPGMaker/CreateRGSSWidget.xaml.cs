using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GameRes.Formats.RPGMaker;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for RGSS widget
    /// </summary>
    public partial class CreateRGSSWidget : StackPanel, IExtensionChangeNotifier
    {
        public event EventHandler<ExtensionChangedEventArgs> ExtensionChanged;

        public string CurrentExtension
        {
            get
            {
                if (Version.SelectedItem is ComboBoxItem item && item.Tag is string versionTag)
                {
                    switch (versionTag)
                    {
                        case "1": return "rgssad";
                        case "2": return "rgss2a";
                        case "3": return "rgss3a";
                    }
                }
                return "rgss3a"; // Default
            }
        }

        public CreateRGSSWidget()
        {
            InitializeComponent();
        }

        private void Version_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ExtensionChanged?.Invoke(this, new ExtensionChangedEventArgs(this.CurrentExtension));
        }
    }

    public class RGSSVersionToIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Byte version)
                return (Int32)Math.Max(0, Math.Min(2, version - 1));
            return (Int32)2;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Int32 index)
                return (Byte)index + 1;
            return (Byte)3;
        }
    }
}