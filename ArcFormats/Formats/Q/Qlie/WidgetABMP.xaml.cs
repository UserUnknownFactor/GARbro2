using System.Windows.Controls;

namespace GameRes.Formats.GUI
{
    public partial class WidgetABMP : Grid
    {
        public int Version 
        { 
            get 
            { 
                var item = this.VersionCombo.SelectedItem as ComboBoxItem;
                return item != null ? int.Parse(item.Tag.ToString()) : 11;
            }
        }

        public WidgetABMP()
        {
            InitializeComponent();
        }
    }
}