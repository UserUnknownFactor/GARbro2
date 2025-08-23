using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using GameRes.Formats.Marble;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetMBL.xaml
    /// </summary>
    public partial class WidgetMBL : Grid
    {
        public WidgetMBL ()
        {
            InitializeComponent ();
            var keys = new[] { new KeyValuePair<string, string> (Localization._T ("ArcDefault"), "") };
            EncScheme.ItemsSource = keys.Concat (MblOpener.KnownKeys);
            if (-1 == EncScheme.SelectedIndex)
                EncScheme.SelectedIndex = 0;
        }
    }
}
