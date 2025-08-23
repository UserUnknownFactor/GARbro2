using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using GameRes.Formats.Ags;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetAGS.xaml
    /// </summary>
    public partial class WidgetAGS : StackPanel
    {
        public WidgetAGS (IEnumerable<string> known_titles)
        {
            InitializeComponent();
            var keys = new string[] { Localization._T ("ArcNoEncryption") };
            Scheme.ItemsSource = keys.Concat (known_titles.OrderBy (x => x));
            if (-1 == Scheme.SelectedIndex)
                Scheme.SelectedIndex = 0;
        }
    }
}
