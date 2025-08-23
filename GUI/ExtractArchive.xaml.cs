using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using Microsoft.WindowsAPICodePack.Dialogs;
using GARbro.GUI.Properties;
using GameRes;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for ExtractArchive.xaml
    /// </summary>
    public partial class ExtractArchiveDialog : ExtractDialog
    {
        public ExtractArchiveDialog (string filename, string destination)
        {
            InitializeComponent();
            ExtractLabel.Text = Localization.Format ("LabelExtractAllTo", filename);
            Destination = destination;
            DestinationDir.EnterKeyDown += acb_OnEnterKeyDown;

            ExtractText.IsEnabled = false;
            TextEncoding.IsEnabled = false;

            InitImageFormats (ImageConversionFormat);
        }

        private void BrowseExec (object sender, ExecutedRoutedEventArgs e)
        {
            string folder = ChooseFolder (Localization._T("TextChooseDestDir"), DestinationDir.Text);
            if (null != folder)
                DestinationDir.Text = folder;
        }

        void ExtractButton_Click (object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            ExportImageFormat (ImageConversionFormat);
        }
    }
}
