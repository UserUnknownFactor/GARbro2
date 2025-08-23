using System.Linq;
using System.Windows;
using System.Windows.Controls;

using GameRes.Formats.AirNovel;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetAIR.xaml
    /// </summary>
    public partial class WidgetAIR : Grid
    {
        public WidgetAIR()
        {
            InitializeComponent();
            LoadDictionary();
        }

        private void LoadDictionary()
        {
            var knownKeys = AirOpener.DefaultScheme.KnownKeys;
            KeyComboBox.ItemsSource = knownKeys.Keys;
            if (KeyComboBox.Items.Count > 0)
                KeyComboBox.SelectedIndex = 0;
            else
                KeyComboBox.Visibility = Visibility.Collapsed;
        }

        private void KeyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (KeyComboBox.SelectedItem != null)
            {
                string selectedKey = KeyComboBox.SelectedItem.ToString();
                if (AirOpener.DefaultScheme.KnownKeys.TryGetValue(selectedKey, out string value))
                    ValueTextBox.Text = value;
                else
                    ValueTextBox.Text = "";
            }
        }
    }
}