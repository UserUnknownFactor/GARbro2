using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetPFS.xaml
    /// </summary>
    public partial class WidgetPFS : Grid
    {
        public WidgetPFS()
        {
            InitializeComponent();
            InitEncodingCombo();
        }
        
        public int Version
        {
            get 
            { 
                switch (VersionCombo.SelectedIndex)
                {
                    case 0: return 2;
                    case 1: return 4;
                    case 2: return 5;
                    case 3: return 6;
                    case 4: return 8;
                    case 5: return 9;
                    default: return 6;
                }
            }
            set 
            { 
                switch (value)
                {
                    case 2: VersionCombo.SelectedIndex = 0; break;
                    case 4: VersionCombo.SelectedIndex = 1; break;
                    case 5: VersionCombo.SelectedIndex = 2; break;
                    case 6: VersionCombo.SelectedIndex = 3; break;
                    case 8: VersionCombo.SelectedIndex = 4; break;
                    case 9: VersionCombo.SelectedIndex = 5; break;
                    default: VersionCombo.SelectedIndex = 3; break;
                }
            }
        }
        
        public Encoding GetEncoding()
        {
            var selected = EncodingCombo.SelectedValue as Encoding;
            if (selected != null)
                return selected;

            return Encoding.UTF8;
        }
        
        void InitEncodingCombo()
        {
            var encodings = new List<EncodingItem>();
            encodings.Add(new EncodingItem { EncodingName = "UTF-8", Encoding = Encoding.UTF8 });
            encodings.Add(new EncodingItem { EncodingName = "Shift-JIS (CP932)", Encoding = Encodings.cp932 });
            EncodingCombo.DataContext = encodings;
            EncodingCombo.SelectedIndex = 0;
        }
        
        class EncodingItem
        {
            public string EncodingName { get; set; }
            public Encoding Encoding { get; set; }
        }
    }
}