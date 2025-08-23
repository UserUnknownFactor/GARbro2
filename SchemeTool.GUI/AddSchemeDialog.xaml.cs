using System;
using System.Linq;
using System.Windows;
using GameRes;

namespace SchemeEditor
{
    public partial class AddSchemeDialog : Window
    {
        public string SchemeName { get; private set; }
        public Type SelectedSchemeType { get; private set; }

        public AddSchemeDialog()
        {
            InitializeComponent();
            LoadSchemeTypes();
        }

        private void LoadSchemeTypes()
        {
            // Get all types that derive from ResourceScheme
            var schemeTypes = TypeHelper.GetDerivedTypes(typeof(ResourceScheme))
                .OrderBy(t => t.Name)
                .ToArray();

            SchemeTypeComboBox.ItemsSource = schemeTypes;
            if (schemeTypes.Length > 0)
                SchemeTypeComboBox.SelectedIndex = 0;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SchemeNameTextBox.Text))
            {
                MessageBox.Show("Please enter a scheme name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (SchemeTypeComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a scheme type.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SchemeName = SchemeNameTextBox.Text.Trim();
            SelectedSchemeType = SchemeTypeComboBox.SelectedItem as Type;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}