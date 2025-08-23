using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameRes.Formats.Properties;
using GameRes.Formats.YuRis;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetYPF.xaml
    /// </summary>
    public partial class WidgetYPF : Grid
    {
        public WidgetYPF ()
        {
            InitializeComponent();
            var guess = new Dictionary<string, YpfScheme> { { Localization._T ("YPFTryGuess"), null } };
            Scheme.ItemsSource = guess.Concat (YpfOpener.KnownSchemes.OrderBy (x => x.Key));
            if (-1 == Scheme.SelectedIndex)
                Scheme.SelectedIndex = 0;
        }
    }
}
