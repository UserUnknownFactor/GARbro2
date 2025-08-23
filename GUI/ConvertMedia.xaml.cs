using System.Linq;
using System.Windows;
using System.Windows.Input;
using GameRes;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for ConvertMedia.xaml
    /// </summary>
    public partial class ConvertMedia : Window
    {
        public ConvertMedia ()
        {
            InitializeComponent ();
            ImageConversionFormat.ItemsSource = FormatCatalog.Instance.ImageFormats.Where (f => f.CanWrite);
            AudioConversionFormat.ItemsSource = FormatCatalog.Instance.AudioFormats.Where (f => f.CanWrite);
            //VideoConversionFormat.ItemsSource = FormatCatalog.Instance.VideoFormats.Where (f => f.CanWrite);
        }

        private void BrowseExec (object sender, ExecutedRoutedEventArgs e)
        {
            var dlg = new CommonOpenFileDialog
            {
                Title = Localization._T("TextChooseDestDir"),
                IsFolderPicker = true,
                InitialDirectory = DestinationDir.Text,

                AddToMostRecentlyUsedList = false,
                AllowNonFileSystemItems = false,
                EnsureFileExists = true,
                EnsurePathExists = true,
                EnsureReadOnly = false,
                EnsureValidNames = true,
                Multiselect = false,
                ShowPlacesList = true,
            };
            if (dlg.ShowDialog (this) == CommonFileDialogResult.Ok)
                DestinationDir.Text = dlg.FileName;
        }

        public void CanExecuteAlways (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void ConvertButton_Click (object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }

        private void acb_OnEnterKeyDown (object sender, KeyEventArgs e)
        {
            this.DialogResult = true;
        }
    }
}
