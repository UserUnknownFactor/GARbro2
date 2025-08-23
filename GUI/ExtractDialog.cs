using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.WindowsAPICodePack.Dialogs;
using GARbro.GUI.Properties;
using GameRes;
using System.Windows.Input;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;

namespace GARbro.GUI
{
    public partial class ExtractDialog : Window
    {
        public string Destination { get; set; }

        public void InitImageFormats (ComboBox image_format)
        {
            var default_format = Settings.Default.appImageFormat;
            var formats = FormatCatalog.Instance.ImageFormats.Where (f => f.IsBuiltin);
            ImageFormatModel[] default_model = { new ImageFormatModel() };
            var models = default_model.Concat (formats.Select (f => new ImageFormatModel (f))).ToList();

            var selected = models.FirstOrDefault (f => f.Tag.Equals (default_format));
            image_format.ItemsSource = models;
            if (null != selected)
                image_format.SelectedItem = selected;
            else if (models.Any())
                image_format.SelectedIndex = 0;
        }

        public ImageFormat GetImageFormat (ComboBox image_format)
        {
            var selected = image_format.SelectedItem as ImageFormatModel;
            if (null != selected)
                return selected.Source;
            else
                return null;
        }

        public void ExportImageFormat (ComboBox image_format)
        {
            var format = GetImageFormat (image_format);
            if (null != format)
                Settings.Default.appImageFormat = format.Tag;
            else
                Settings.Default.appImageFormat = "";
        }

        public string ChooseFolder (string title, string initial)
        {
            var dlg = new CommonOpenFileDialog();
            dlg.Title = title;
            dlg.IsFolderPicker = true;
            dlg.InitialDirectory = initial;

            dlg.AddToMostRecentlyUsedList = false;
            dlg.AllowNonFileSystemItems = false;
            dlg.EnsureFileExists = true;
            dlg.EnsurePathExists = true;
            dlg.EnsureReadOnly = false;
            dlg.EnsureValidNames = true;
            dlg.Multiselect = false;
            dlg.ShowPlacesList = true;

            if (dlg.ShowDialog (this) == CommonFileDialogResult.Ok)
                return dlg.FileName;
            else
                return null;
        }

        protected void acb_OnEnterKeyDown (object sender, KeyEventArgs e)
        {
            string path = (sender as AutoCompleteBox).Text;
            if (!string.IsNullOrEmpty (path))
                this.DialogResult = true;
        }

        public void CanExecuteAlways (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }
    }
}
