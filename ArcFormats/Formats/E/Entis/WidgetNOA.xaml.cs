using System.Linq;
using System.Windows.Controls;
using GameRes.Formats.Entis;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetNOA.xaml
    /// </summary>
    public partial class WidgetNOA : Grid
    {
        public WidgetNOA ()
        {
            InitializeComponent ();
            var keys = new string[] { Localization._T ("ArcIgnoreEncryption") };
            Scheme.ItemsSource = keys.Concat (NoaOpener.KnownKeys.Keys.OrderBy (x => x));
            // select first scheme as default
            if (-1 == Scheme.SelectedIndex)
                Scheme.SelectedIndex = 0;
        }
    }
}
