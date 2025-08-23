using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.Win32;

using GameRes;
using GameRes.Formats.GUI;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for CreateArchive.xaml
    /// </summary>
    public partial class CreateArchiveDialog : Window
    {
        public CreateArchiveDialog (string initial_name = "")
        {
            InitializeComponent ();

            if (!string.IsNullOrEmpty (initial_name))
            {
                var format = this.ArchiveFormat.SelectedItem as ArchiveFormat;
                if (this.ArchiveOptions is IExtensionProvider extensionProvider)
                    initial_name = extensionProvider.GetExtension();
                else if (null != format)
                    initial_name = Path.ChangeExtension (initial_name, format.Extensions.FirstOrDefault());
            }

            ArchiveName.Text = initial_name;
            ButtonOk.IsEnabled = false;
        }

        private readonly IEnumerable<ArchiveFormat> m_formats = FormatCatalog.Instance.ArcFormats.Where (f => f.CanWrite).OrderBy (f => f.Tag);

        public IEnumerable<ArchiveFormat> ArcFormats { get { return m_formats; } }

        public ResourceOptions ArchiveOptions { get; private set; }

        void Button_Click (object sender, RoutedEventArgs e)
        {
            string arc_name = Path.GetFullPath (ArchiveName.Text);
            if (File.Exists (arc_name))
            {
                string text = Localization.Format ("MsgOverwrite", arc_name);
                var rc = MessageBox.Show (this, text, Localization._T("TextConfirmOverwrite"), MessageBoxButton.YesNo,
                                          MessageBoxImage.Question);
                if (MessageBoxResult.Yes != rc)
                    return;
            }
            var format = this.ArchiveFormat.SelectedItem as ArchiveFormat;
            if (null != format)
                ArchiveOptions = format.GetOptions (OptionsWidget.Content);
            DialogResult = true;
        }

        void BrowseExec (object sender, ExecutedRoutedEventArgs e)
        {
            string file = ChooseFile (Localization._T("TextChooseArchive"), ArchiveName.Text);
            if (!string.IsNullOrEmpty (file))
                ArchiveName.Text = file;
        }

        string GetFilters ()
        {
            var filters = new StringBuilder();

            var format = this.ArchiveFormat.SelectedItem as ArchiveFormat;
            if (null != format && format.Extensions.Any())
            {
                var patterns = format.Extensions.Select (ext => "*."+ext);
                filters.Append (format.Description);
                filters.Append (" (");
                filters.Append (string.Join (", ", patterns));
                filters.Append (")|");
                filters.Append (string.Join (";", patterns));
            }

            if (filters.Length > 0)
                filters.Append ('|');
            filters.Append (string.Format ("{0} (*.*)|*.*", Localization._T("TextAllFiles")));
            return filters.ToString();
        }

        public string ChooseFile (string title, string initial)
        {
            string dir = ".";
            if (!string.IsNullOrEmpty (initial))
            {
                var parent = Directory.GetParent (initial);
                if (null != parent)
                    dir = parent.FullName;
            }
            dir = Path.GetFullPath (dir);
            var dlg = new SaveFileDialog {
                AddExtension = true,
                CheckPathExists = true,
                FileName = initial,
                Filter = GetFilters(),
                InitialDirectory = dir,
                Title = Localization._T("TextChooseArchive"),
                OverwritePrompt = false
            };
            return dlg.ShowDialog (this).Value ? dlg.FileName : null;
        }

        void OnFormatSelect (object sender, SelectionChangedEventArgs e)
        {
            if (OptionsWidget.Content is IExtensionChangeNotifier oldNotifier)
                oldNotifier.ExtensionChanged -= OnWidgetExtensionChanged;

            OptionsWidget.Content = null;
            OptionsWidget.Visibility = Visibility.Hidden;

            var format = this.ArchiveFormat.SelectedItem as ArchiveFormat;
            if (null == format)
            {
                ButtonOk.IsEnabled = false;
                return;
            }

            ButtonOk.IsEnabled = format.CanWrite;

            var widget = format.GetCreationWidget();
            if (widget is UIElement ui_widget)
            {
                OptionsWidget.Content = ui_widget;
                OptionsWidget.Visibility = Visibility.Visible;

                if (widget is IExtensionChangeNotifier newNotifier)
                {
                    newNotifier.ExtensionChanged += OnWidgetExtensionChanged;
                    UpdateArchiveExtension(newNotifier.CurrentExtension);
                }
                else if (!string.IsNullOrEmpty(ArchiveName.Text))
                {
                    if (this.ArchiveOptions is IExtensionProvider extensionProvider)
                        UpdateArchiveExtension(extensionProvider.GetExtension());
                    else 
                        UpdateArchiveExtension(format.Extensions.FirstOrDefault());
                }
            }
            else if (!string.IsNullOrEmpty(ArchiveName.Text))
            {
                UpdateArchiveExtension(format.Extensions.FirstOrDefault());
            }
        }

        /// <summary>
        /// Called by any widget that fires the ExtensionChanged event.
        /// </summary>
        private void OnWidgetExtensionChanged(object sender, ExtensionChangedEventArgs e)
        {
            UpdateArchiveExtension(e.NewExtension);
        }

        /// <summary>
        /// Helper method to centralize the logic for changing the extension.
        /// </summary>
        public void UpdateArchiveExtension(string newExtension)
        {
            if (!string.IsNullOrEmpty(newExtension) && !string.IsNullOrEmpty(ArchiveName.Text))
            {
                ArchiveName.Text = Path.ChangeExtension(ArchiveName.Text, newExtension);
            }
        }

        void CanExecuteAlways (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void ArchiveName_TextChanged (object sender, RoutedEventArgs e)
        {
            this.ButtonOk.IsEnabled = ArchiveName.Text.Length > 0 && ArchiveFormat.SelectedItem != null;
        }
    }
}
