using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.WindowsAPICodePack.Dialogs;
using GARbro.GUI.Properties;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for ExtractFile.xaml
    /// </summary>
    public partial class ExtractFile : ExtractDialog
    {
        public ExtractFile (EntryViewModel entry, string destination)
        {
            InitializeComponent();
            ExtractLabel.Text = Localization.Format ("LabelExtractFileTo", entry.Name);
            Destination = destination;
            DestinationDir.EnterKeyDown += acb_OnEnterKeyDown;
            if ("image" == entry.Type)
            {
                ActiveOption = ImageConversionOptions;
                InitImageFormats (ImageConversionFormat);
            }
            else if ("script" == entry.Type || "text" == entry.Type || "config" == entry.Type)
            {
                ActiveOption = TextConversionOptions;
                TextEncoding.IsEnabled = false;
            }
            else if ("audio" == entry.Type)
            {
                ActiveOption = AudioConversionOptions;
            }
            else
            {
                ActiveOption = null;
            }
        }

        private UIElement m_active_option;
        public UIElement ActiveOption
        {
            get { return m_active_option; }
            set
            {
                m_active_option = value;
                if (null != m_active_option)
                    m_active_option.Visibility = Visibility.Visible;
                foreach (var c in ConversionTypePanel.Children.Cast<UIElement>())
                {
                    if (c != m_active_option)
                        c.Visibility = Visibility.Collapsed;
                }
            }
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
            if (ImageConversionOptions == ActiveOption)
            {
                ExportImageFormat (ImageConversionFormat);
            }
        }
    }
}
