using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

using GameRes;
using GARbro.GUI.Properties;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow ()
        {
            InitializeComponent();

            this.DataContext = this.ViewModel = CreateSettingsTree();
            this.Closing += (s, e) => {
                var section = SectionsPane.SelectedItem as SettingsSectionView;
                if (section != null)
                    LastSelectedSection = section.Label;
            };
        }

        static readonly IEnumerable<IResourceSetting> ViewerSettings = new [] {
            MainWindow.DownScaleImage,
        };

        SettingsViewModel ViewModel;

        static string LastSelectedSection = null;

        private void OnSectionChanged (object sender, System.Windows.RoutedEventArgs e)
        {
            this.SettingsPane.Child = null;
            var section = SectionsPane.SelectedValue as SettingsSectionView;
            if (section != null && section.Panel != null)
                this.SettingsPane.Child = section.Panel;
        }

        private void Button_ClickApply (object sender, System.Windows.RoutedEventArgs e)
        {
            ApplyChanges();
        }

        private void Button_ClickOk (object sender, System.Windows.RoutedEventArgs e)
        {
            ApplyChanges();
            DialogResult = true;
        }

        private void ApplyChanges ()
        {
            if (!ViewModel.HasChanges)
                return;
            if (OnApplyChanges != null)
                OnApplyChanges (this, EventArgs.Empty);
            ViewModel.HasChanges = false;
        }

        private SettingsViewModel CreateSettingsTree ()
        {
            SettingsSectionView[] list = {
                new SettingsSectionView {
                    Label = Localization._T("TextViewer"),
                    Panel = CreateSectionPanel (ViewerSettings)
                },
                new SettingsSectionView {
                    Label = Localization._T("TextFormats"),
                    Children = EnumerateFormatsSettings(),
                },
            };
            SettingsSectionView selected_section = null;
            if (LastSelectedSection != null)
                selected_section = EnumerateSections (list).FirstOrDefault (s => s.Label == LastSelectedSection);
            if (null == selected_section)
                selected_section = list[0];
            selected_section.IsSelected = true;
            return new SettingsViewModel { Root = list };
        }

        IEnumerable<SettingsSectionView> EnumerateFormatsSettings ()
        {
            var list = new List<SettingsSectionView>();
            var formats = FormatCatalog.Instance.Formats.Where (f => f.Settings != null && f.Settings.Any());
            foreach (var format in formats.OrderBy (f => f.Tag))
            {
                var pane = CreateSectionPanel (format.Settings);
                if (pane.Children.Count > 0)
                {
                    var section = new SettingsSectionView {
                        Label = format.Tag,
                        SectionTitle = Localization._T("TextFormats")+" :: "+format.Tag,
                        Panel = pane
                    };
                    list.Add (section);
                }
            }
            return list;
        }

        Panel CreateSectionPanel (IEnumerable<IResourceSetting> settings)
        {
            var pane = new WrapPanel();
            foreach (var setting in settings)
            {
                var widget = CreateSettingWidget (setting, setting.Value);
                if (widget != null)
                    pane.Children.Add (widget);
            }
            return pane;
        }

        UIElement CreateCheckBoxWidget (IResourceSetting setting)
        {
            return new CheckBox {
                Template = (ControlTemplate)this.Resources["BoundCheckBox"],
                DataContext = CreateSettingView<bool> (setting),
            };
        }

        UIElement CreateEncodingWidget (IResourceSetting setting)
        {
            var view = CreateEncodingSettingView (setting);
            return new ContentControl {
                Template = (ControlTemplate)this.Resources["BoundEncodingSelector"],
                DataContext = view,
            };
        }

        UIElement CreateGaugeWidget (FixedGaugeSetting setting)
        {
            return new Slider {
                Template = (ControlTemplate)this.Resources["BoundSlider"],
                DataContext = CreateSettingView<int> (setting),
                Ticks = new DoubleCollection (setting.ValuesSet.Select (x => (double)x)),
            };
        }

        UIElement CreateDropDownWidget (FixedSetSetting setting)
        {
            return new ComboBox {
                Template = (ControlTemplate)this.Resources["BoundDropDownList"],
                DataContext = CreateSettingView<object> (setting),
            };
        }

        UIElement CreateTextBoxWidget (IResourceSetting setting)
        {
            return new ContentControl {
                Template = (ControlTemplate)this.Resources["BoundTextBox"],
                DataContext = CreateSettingView<string> (setting),
            };
        }

        UIElement CreateNumericWidget (IResourceSetting setting, Type numericType)
        {
            if (numericType == typeof(int) || numericType == typeof(uint))
            {
                return new ContentControl {
                    Template = (ControlTemplate)this.Resources["BoundNumericBox"],
                    DataContext = CreateSettingView<int> (setting),
                };
            }
            else if (numericType == typeof(long) || numericType == typeof(ulong))
            {
                return new ContentControl {
                    Template = (ControlTemplate)this.Resources["BoundNumericBox"],
                    DataContext = CreateSettingView<long> (setting),
                };
            }
            else if (numericType == typeof(short) || numericType == typeof(ushort))
            {
                return new ContentControl {
                    Template = (ControlTemplate)this.Resources["BoundNumericBox"],
                    DataContext = CreateSettingView<int> (setting),
                };
            }
            else if (numericType == typeof(byte) || numericType == typeof(sbyte))
            {
                return new ContentControl {
                    Template = (ControlTemplate)this.Resources["BoundNumericBox"],
                    DataContext = CreateSettingView<int> (setting),
                };
            }
            else if (numericType == typeof(double) || numericType == typeof(float) || numericType == typeof(decimal))
            {
                return new ContentControl {
                    Template = (ControlTemplate)this.Resources["BoundTextBox"],
                    DataContext = CreateSettingView<double> (setting),
                };
            }
            return null;
        }

        UIElement CreateEnumWidget (IResourceSetting setting, Type enumType)
        {
            var viewType = typeof(EnumSettingView<>).MakeGenericType(enumType);
            var view = Activator.CreateInstance(viewType, setting) as ISettingView;

            view.ValueChanged += (s, e) => ViewModel.HasChanges = true;
            this.OnApplyChanges += (s, e) => view.Apply();

            return new ComboBox {
                Template = (ControlTemplate)this.Resources["BoundEnumSelector"],
                DataContext = view,
            };
        }

        UIElement CreateSettingWidget<TUnknown> (IResourceSetting setting, TUnknown value)
        {
            // Check for specific setting types
            if (setting is FixedGaugeSetting)
                return CreateGaugeWidget (setting as FixedGaugeSetting);

            if (setting is FixedSetSetting)
                return CreateDropDownWidget (setting as FixedSetSetting);

            // Check value type
            var valueType = value?.GetType();
            if (valueType == null)
                return null;

            // Boolean
            if (value is bool)
                return CreateCheckBoxWidget (setting);

            // Encoding
            if (value is Encoding)
                return CreateEncodingWidget (setting);

            // Enum
            if (valueType.IsEnum)
                return CreateEnumWidget (setting, valueType);

            // Numeric types
            if (IsNumericType(valueType))
                return CreateNumericWidget (setting, valueType);

            // String (and fallback for other types)
            if (value is string || CanConvertToString(valueType))
                return CreateTextBoxWidget (setting);

            Trace.WriteLine (string.Format ("Unsupported setting type {0}", valueType), "[GUI]");
            return null;
        }

        bool IsNumericType (Type type)
        {
            return type == typeof(int) || type == typeof(uint) ||
                   type == typeof(long) || type == typeof(ulong) ||
                   type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(byte) || type == typeof(sbyte) ||
                   type == typeof(double) || type == typeof(float) ||
                   type == typeof(decimal);
        }

        bool CanConvertToString (Type type)
        {
            // Types that have meaningful ToString() and can parse from string
            return type.IsPrimitive || type == typeof(decimal) || type == typeof(Guid);
        }

        ISettingView CreateSettingView<TValue> (IResourceSetting setting)
        {
            var view = new ResourceSettingView<TValue> (setting);
            view.ValueChanged   += (s, e) => ViewModel.HasChanges = true;
            this.OnApplyChanges += (s, e) => view.Apply();
            return view;
        }

        ISettingView CreateEncodingSettingView (IResourceSetting setting)
        {
            var view = new EncodingSettingView (setting);
            view.ValueChanged += (s, e) => ViewModel.HasChanges = true;
            this.OnApplyChanges += (s, e) => view.Apply();
            return view;
        }

        static IEnumerable<SettingsSectionView> EnumerateSections (IEnumerable<SettingsSectionView> list)
        {
            foreach (var section in list)
            {
                yield return section;
                if (section.Children != null)
                {
                    foreach (var child in EnumerateSections (section.Children))
                        yield return child;
                }
            }
        }

        private void tvi_MouseRightButtonDown (object sender, MouseButtonEventArgs e)
        {
            var item = sender as TreeViewItem;
            if (item != null && e.RightButton == MouseButtonState.Pressed)
            {
                item.Focus();
                item.IsSelected = true;
                e.Handled = true;
            }
        }

        // Event handlers for numeric up/down buttons
        private void NumericUpButton_Click (object sender, RoutedEventArgs e)
        {
            var button = sender as RepeatButton;
            if (button?.Tag is ISettingView view)
            {
                try {
                    var prop = view.GetType().GetProperty("Value");
                    if (prop != null)
                    {
                        var currentValue = prop.GetValue(view);
                        if (currentValue is int intVal)
                            prop.SetValue(view, intVal + 1);
                        else if (currentValue is long longVal)
                            prop.SetValue(view, longVal + 1);
                        else if (currentValue is short shortVal)
                            prop.SetValue(view, (short)(shortVal + 1));
                        else if (currentValue is byte byteVal && byteVal < byte.MaxValue)
                            prop.SetValue(view, (byte)(byteVal + 1));
                    }
                } catch { }
            }
        }

        private void NumericDownButton_Click (object sender, RoutedEventArgs e)
        {
            var button = sender as RepeatButton;
            if (button?.Tag is ISettingView view)
            {
                try {
                    var prop = view.GetType().GetProperty("Value");
                    if (prop != null)
                    {
                        var currentValue = prop.GetValue(view);
                        if (currentValue is int intVal)
                            prop.SetValue(view, intVal - 1);
                        else if (currentValue is long longVal)
                            prop.SetValue(view, longVal - 1);
                        else if (currentValue is short shortVal)
                            prop.SetValue(view, (short)(shortVal - 1));
                        else if (currentValue is byte byteVal && byteVal > byte.MinValue)
                            prop.SetValue(view, (byte)(byteVal - 1));
                    }
                } catch { }
            }
        }

        private void NumericTextBox_PreviewTextInput (object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextNumeric(e.Text);
        }

        private static bool IsTextNumeric (string text)
        {
            System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex("[^0-9.-]+");
            return !regex.IsMatch(text);
        }

        public delegate void ApplyEventHandler (object sender, EventArgs e);

        public event ApplyEventHandler OnApplyChanges;
    }

    public class SettingsViewModel : INotifyPropertyChanged
    {
        public IEnumerable<SettingsSectionView> Root { get; set; }

        bool    m_has_changes;
        public bool HasChanges {
            get { return m_has_changes; }
            set {
                if (value != m_has_changes)
                {
                    m_has_changes = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged ([CallerMemberName] string propertyName = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged (this, new PropertyChangedEventArgs (propertyName));
            }
        }
    }

    public class SettingsSectionView
    {
        public string        Label { get; set; }
        public bool     IsSelected { get; set; }
        public UIElement     Panel { get; set; }

        string m_title;
        public string SectionTitle {
            get { return m_title ?? Label; }
            set { m_title = value; }
        }

        public IEnumerable<SettingsSectionView> Children { get; set; }
    }

    public interface ISettingView
    {
        IResourceSetting Source { get; }
        bool          IsChanged { get; }
        string             Text { get; }
        string      Description { get; }

        void Apply ();

        event PropertyChangedEventHandler ValueChanged;
    }

    public class ResourceSettingView<TValue> : ISettingView
    {
        public IResourceSetting Source { get; private set; }
        public bool          IsChanged { get; private set; }
        public string             Text { get { return Source.Text; } }
        public string      Description { get { return Source.Description; } }

        TValue m_value;
        public TValue Value {
            get { return m_value; }
            set {
                if (!EqualityComparer<TValue>.Default.Equals (m_value, value))
                {
                    m_value = value;
                    IsChanged = true;
                    OnValueChanged();
                }
            }
        }

        public ResourceSettingView (IResourceSetting src)
        {
            Source = src;
            m_value = (TValue)src.Value;
        }

        public void Apply ()
        {
            if (IsChanged)
            {
                Source.Value = m_value;
                IsChanged = false;
            }
        }

        public event PropertyChangedEventHandler ValueChanged;

        void OnValueChanged ()
        {
            if (ValueChanged != null)
            {
                ValueChanged (this, new PropertyChangedEventArgs ("Value"));
            }
        }
    }

    public class EncodingSettingView : ResourceSettingView<Encoding>
    {
        public IEnumerable<Encoding> EncodingList { get; }

        public EncodingSettingView (IResourceSetting setting) : base (setting)
        {
            EncodingList = MainWindow.GetEncodingList (true);
        }
    }

    public class EnumSettingView<TEnum> : ISettingView where TEnum : struct, Enum
    {
        public IResourceSetting Source { get; private set; }
        public bool IsChanged { get; private set; }
        public string Text { get { return Source.Text; } }
        public string Description { get { return Source.Description; } }

        TEnum m_value;
        public TEnum Value {
            get { return m_value; }
            set {
                if (!EqualityComparer<TEnum>.Default.Equals (m_value, value))
                {
                    m_value = value;
                    IsChanged = true;
                    OnValueChanged();
                }
            }
        }

        public class EnumDisplay
        {
            public TEnum Value { get; set; }
            public string Display { get; set; }
        }

        public IEnumerable<EnumDisplay> EnumValues { get; }

        public EnumSettingView (IResourceSetting src)
        {
            Source = src;
            m_value = (TEnum)src.Value;

            EnumValues = Enum.GetValues(typeof(TEnum))
                .Cast<TEnum>()
                .Select(e => new EnumDisplay { 
                    Value = e, 
                    Display = GetEnumDescription(e) ?? e.ToString() 
                });
        }

        string GetEnumDescription (TEnum value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = field?.GetCustomAttribute<DescriptionAttribute>();
            return attribute?.Description;
        }

        public void Apply ()
        {
            if (IsChanged)
            {
                Source.Value = m_value;
                IsChanged = false;
            }
        }

        public event PropertyChangedEventHandler ValueChanged;

        void OnValueChanged ()
        {
            ValueChanged?.Invoke (this, new PropertyChangedEventArgs ("Value"));
        }
    }

    public static class TreeViewItemExtensions
    {
        /// <returns>Depth of the given TreeViewItem</returns>
        public static int GetDepth (this TreeViewItem item)
        {
            var tvi = item.GetParent() as TreeViewItem;
            if (tvi != null)
                return tvi.GetDepth() + 1;
            return 0;
        }

        /// <returns>Control that contains specified TreeViewItem
        /// (either TreeView or another TreeViewItem).</returns>
        public static ItemsControl GetParent (this TreeViewItem item)
        {
            return ItemsControl.ItemsControlFromItemContainer (item);
        }
    }

    public class LeftMarginMultiplierConverter : IValueConverter
    {
        public double Length { get; set; }

        public object Convert (object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var item = value as TreeViewItem;
            if (item == null)
                return new Thickness (0);
            double thickness = Length * item.GetDepth();

            return new Thickness (thickness, 0, 0, 0);
        }

        public object ConvertBack (object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new System.NotImplementedException();
        }
    }

    public class GuiResourceSetting : ResourceSettingBase, INotifyPropertyChanged
    {
        public override object Value {
            get { return Settings.Default[Name]; }
            set {
                if (!Settings.Default[Name].Equals (value))
                {
                    Settings.Default[Name] = value;
                    OnPropertyChanged();
                }
            }
        }

        public GuiResourceSetting () { }

        public GuiResourceSetting (string name)
        {
            Name = name;
            Text = Localization._T(name) ?? name;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged ([CallerMemberName] string propertyName = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged (this, new PropertyChangedEventArgs (propertyName));
            }
        }
    }
}
