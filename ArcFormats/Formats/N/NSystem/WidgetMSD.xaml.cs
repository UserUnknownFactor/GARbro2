using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using GameRes.Formats.NSystem;
using GameRes.Formats.Properties;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetMSD.xaml
    /// </summary>
    public partial class WidgetMSD : StackPanel
    {
        public WidgetMSD ()
        {
            InitializeComponent ();
            var first = new Dictionary<string, string> { { Localization._T ("ArcNoEncryption"), "" } };
            Title.ItemsSource = first.Concat (FjsysOpener.KnownPasswords.OrderBy (x => x.Key));
        }
    }
}
