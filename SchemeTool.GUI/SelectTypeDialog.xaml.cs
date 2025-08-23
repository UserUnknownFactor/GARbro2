using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SchemeEditor
{
    public partial class SelectTypeDialog : Window
    {
        public Type SelectedType { get; private set; }

        public SelectTypeDialog(Type[] types)
        {
            InitializeComponent();
            TypeListBox.ItemsSource = types;
            if (types.Length > 0)
                TypeListBox.SelectedIndex = 0;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            SelectedType = TypeListBox.SelectedItem as Type;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void TypeListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TypeListBox.SelectedItem != null)
            {
                SelectedType = TypeListBox.SelectedItem as Type;
                DialogResult = true;
            }
        }
    }
}