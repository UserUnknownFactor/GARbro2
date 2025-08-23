using System.Linq;
using System.Windows.Controls;
using GameRes.Formats.Cyberworks;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetBELL.xaml
    /// </summary>
    public partial class WidgetBELL : StackPanel
    {
        public WidgetBELL()
        {
            InitializeComponent();
            var keys = new string[] { Localization._T ("ArcIgnoreEncryption") };
            Title.ItemsSource = keys.Concat (DatOpener.KnownSchemes.Keys.OrderBy (x => x));
            if (-1 == Title.SelectedIndex)
                Title.SelectedIndex = 0;
        }
    }
}
