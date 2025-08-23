using System;
using System.Windows.Markup;
using System.Windows.Data;
using System.Globalization;

namespace GameRes.Extensions
{
    [MarkupExtensionReturnType(typeof(string))]
    public class LocalizeExtension : MarkupExtension
    {
        public string Key { get; set; }
        public string Default { get; set; }

        public LocalizeExtension() { }
        public LocalizeExtension(string key) { Key = key; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key))
                return Default ?? string.Empty;

            var localized = Localization.Format(Key);
            return localized ?? Default ?? Key;
        }
    }

    public class LocalizeBinding : System.Windows.Data.Binding
    {
        public LocalizeBinding(string key) : base("[" + key + "]")
        {
            Source = LocalizationManager.Instance;
            Mode = System.Windows.Data.BindingMode.OneWay;
        }
    }

    public class LocalizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return null;

            string key = value.ToString();
            return Localization.Format(key);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class LocalizationManager : System.ComponentModel.INotifyPropertyChanged
    {
        private static LocalizationManager _instance;
        public static LocalizationManager Instance => _instance ?? (_instance = new LocalizationManager());

        public string this[string key] => Localization.Format(key);

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public void RefreshUI()
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        }

        public static void ChangeLanguage(System.Globalization.CultureInfo culture)
        {
            Localization.ReloadForCulture(culture);
            Instance.RefreshUI();
        }
    }
}

/*
// Change language at runtime
private void ChangeToJapanese()
{
    LocalizationManager.ChangeLanguage(new CultureInfo("ja-JP"));
}
*/