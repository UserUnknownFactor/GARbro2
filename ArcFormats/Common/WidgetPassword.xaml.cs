using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InputKey = System.Windows.Input.Key;

namespace GameRes.Formats
{
    /// <summary>
    /// Control logic for WidgetPassword.xaml
    /// </summary>
    public partial class WidgetPassword : StackPanel, INotifyPropertyChanged
    {
        private ObservableCollection<string> _passwordHistory;
        private const int MaxHistoryItems = 10;
        private string _currentPassword;
        private static Dictionary<string, string> _successfulPasswords = new Dictionary<string, string>();

        public static readonly DependencyProperty FormatTagProperty =
            DependencyProperty.Register("FormatTag", typeof(string),
                typeof(WidgetPassword),
                new PropertyMetadata(string.Empty, OnFormatTagChanged));

        public static readonly DependencyProperty SchemeProperty =
            DependencyProperty.Register("Scheme", typeof(ResourceScheme),
                typeof(WidgetPassword),
                new PropertyMetadata(null, OnSchemeChanged));

        public static string passKnownSeparator = "--- Known Passwords ---";

        public string FormatTag
        {
            get { return (string)GetValue(FormatTagProperty); }
            set { SetValue(FormatTagProperty, value); }
        }

        public ResourceScheme Scheme
        {
            get { return (ResourceScheme)GetValue(SchemeProperty); }
            set { SetValue(SchemeProperty, value); }
        }

        public ObservableCollection<string> PasswordHistory
        {
            get => _passwordHistory;
            set
            {
                _passwordHistory = value;
                OnPropertyChanged();
            }
        }

        public string Password
        {
            get
            {
                if (PasswordComboBox != null)
                    return PasswordComboBox.Text;
                return _currentPassword ?? string.Empty;
            }
            set
            {
                _currentPassword = value;
                if (PasswordComboBox != null)
                    PasswordComboBox.Text = value;
                OnPropertyChanged();
            }
        }

        public WidgetPassword()
        {
            InitializeComponent();
            DataContext = this;
            _passwordHistory = new ObservableCollection<string>();

            this.Loaded += (s, e) => LoadPasswordHistory();
        }

        private static void OnFormatTagChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var widget = d as WidgetPassword;
            if (widget != null && widget.IsLoaded)
            {
                widget.LoadPasswordHistory();
            }
        }

        private static void OnSchemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var widget = d as WidgetPassword;
            if (widget != null && widget.IsLoaded)
            {
                widget.LoadPasswordHistory();
            }
        }

        protected virtual void LoadPasswordHistory()
        {
            var combinedPasswords = new List<string>();

            // First, add recent passwords from history
            string historyKey = GetHistorySettingKey();
            var history = GetSettingValue(historyKey);

            if (!string.IsNullOrEmpty(history))
            {
                var recentPasswords = history.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                combinedPasswords.AddRange(recentPasswords);
            }

            var knownPasswords = GetKnownPasswordsFromScheme();
            if (knownPasswords != null && knownPasswords.Any())
            {
                if (combinedPasswords.Count > 0)
                    combinedPasswords.Add(passKnownSeparator);

                foreach (var knownPassword in knownPasswords)
                {
                    if (!combinedPasswords.Contains(knownPassword))
                    {
                        combinedPasswords.Add(knownPassword);
                    }
                }
            }

            PasswordHistory = new ObservableCollection<string>(combinedPasswords);

            // Load the last used password for this format
            var lastPasswordKey = GetPasswordSettingKey();
            var lastPassword = GetSettingValue(lastPasswordKey);
            if (!string.IsNullOrEmpty(lastPassword))
            {
                Password = lastPassword;
            }
            else if (!string.IsNullOrEmpty(FormatTag) && _successfulPasswords.ContainsKey(FormatTag))
            {
                // Use last successful password from current session
                Password = _successfulPasswords[FormatTag];
            }
        }

        private IEnumerable<string> GetKnownPasswordsFromScheme()
        {
            if (Scheme == null)
                return Enumerable.Empty<string>();

            var passwords = new HashSet<string>();
            var type = Scheme.GetType();

            // Check all properties and fields
            var members = type.GetProperties().Cast<MemberInfo>()
                .Concat(type.GetFields().Cast<MemberInfo>());

            foreach (var member in members)
            {
                var name = member.Name.ToLowerInvariant();
                if (!name.Contains("password") && !name.Contains("key"))
                    continue;

                object value = null;
                if (member is PropertyInfo prop)
                    value = prop.GetValue(Scheme);
                else if (member is FieldInfo field)
                    value = field.GetValue(Scheme);

                if (value == null)
                    continue;

                if (value is string str && !string.IsNullOrEmpty(str))
                {
                    passwords.Add(str);
                }
                else if (value is IDictionary dict)
                {
                    foreach (var val in dict.Values)
                    {
                        if (val is string s && !string.IsNullOrEmpty(s))
                            passwords.Add(s);
                    }
                }
                else if (value is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item is string s && !string.IsNullOrEmpty(s))
                        {
                            passwords.Add(s);
                        }
                        /*else if (item is byte[] bytes && bytes.Length > 0)
                        {
                            passwords.Add(BitConverter.ToString(bytes).Replace("-", ""));
                        }
                        */else if (item != null)
                        {
                            var itemType = item.GetType();

                            var passwordProp = itemType.GetProperty("Password");
                            if (passwordProp != null)
                            {
                                var pwd = passwordProp.GetValue(item) as string;
                                if (!string.IsNullOrEmpty(pwd))
                                    passwords.Add(pwd);
                            }

                            var keyProp = itemType.GetProperty("Key");
                            if (keyProp != null)
                            {
                                var keyValue = keyProp.GetValue(item);
                                if (keyValue is string keyStr && !string.IsNullOrEmpty(keyStr))
                                {
                                    passwords.Add(keyStr);
                                }
                                /*else if (keyValue is byte[] keyBytes && keyBytes.Length > 0)
                                {
                                    passwords.Add(BitConverter.ToString(keyBytes).Replace("-", ""));
                                }*/
                            }
                        }
                    }
                }
                /*else if (value is byte[] keyBytes && keyBytes.Length > 0)
                {
                    passwords.Add(BitConverter.ToString(keyBytes).Replace("-", ""));
                }*/
            }

            return passwords;
        }

        /// <summary>
        /// Call this method when a password successfully opens an archive
        /// </summary>
        public void MarkPasswordAsSuccessful()
        {
            var password = Password;
            if (string.IsNullOrWhiteSpace(password))
                return;

            if (!string.IsNullOrEmpty(FormatTag))
            {
                _successfulPasswords[FormatTag] = password;
            }

            AddPasswordToHistory(password);
            SavePasswordHistory();
        }

        /// <summary>
        /// Static method to mark a password as successful for a format
        /// Can be called from anywhere without widget reference
        /// </summary>
        public static void MarkPasswordAsSuccessful(string formatTag, string password)
        {
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(formatTag))
                return;

            _successfulPasswords[formatTag] = password;
            SavePasswordToHistory(formatTag, password);
        }

        private static void SavePasswordToHistory(string formatTag, string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return;

            string historyKey = string.IsNullOrEmpty(formatTag)
                ? "PasswordHistory"
                : $"{formatTag}_PasswordHistory";

            // Get existing history
            var history = GetSettingValueStatic(historyKey);
            var passwords = string.IsNullOrEmpty(history)
                ? new List<string>()
                : history.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();

            // Update history
            passwords.Remove(password);
            passwords.Insert(0, password);
            while (passwords.Count > MaxHistoryItems)
                passwords.RemoveAt(passwords.Count - 1);

            // Save
            SetSettingValueStatic(historyKey, string.Join(";", passwords));

            // Also save as last used password
            string passwordKey = string.IsNullOrEmpty(formatTag)
                ? "Password"
                : $"{formatTag}_Password";
            SetSettingValueStatic(passwordKey, password);

            try
            {
                Properties.Settings.Default.Save();
            }
            catch { }
        }

        private static string GetSettingValueStatic(string key)
        {
            try
            {
                var settings = Properties.Settings.Default;
                var property = settings.GetType().GetProperty(key);
                if (property != null)
                    return property.GetValue(settings) as string ?? string.Empty;

                if (settings[key] != null)
                    return settings[key].ToString();
            }
            catch { }
            return string.Empty;
        }

        private static void SetSettingValueStatic(string key, string value)
        {
            try
            {
                var settings = Properties.Settings.Default;
                var property = settings.GetType().GetProperty(key);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(settings, value);
                    return;
                }

                settings[key] = value;
            }
            catch { }
        }

        private void SavePasswordHistory()
        {
            // Only save recent passwords, not known ones
            var recentPasswords = PasswordHistory
                .Where(p => p != passKnownSeparator && !IsKnownPassword(p))
                .Take(MaxHistoryItems)
                .ToList();

            string historyKey = GetHistorySettingKey();
            string historyValue = string.Join(";", recentPasswords);
            SetSettingValue(historyKey, historyValue);

            // Also save the current password as the last used for this format
            if (!string.IsNullOrWhiteSpace(Password))
            {
                string passwordKey = GetPasswordSettingKey();
                SetSettingValue(passwordKey, Password);
            }

            try
            {
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        private bool IsKnownPassword(string password)
        {
            var knownPasswords = GetKnownPasswordsFromScheme();
            return knownPasswords != null && knownPasswords.Contains(password);
        }

        private void AddPasswordToHistory(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password == passKnownSeparator)
                return;

            if (IsKnownPassword(password))
                return;

            // Get current recent passwords only
            var recentPasswords = PasswordHistory
                .Where(p => p != passKnownSeparator && !IsKnownPassword(p))
                .ToList();

            if (recentPasswords.Contains(password))
                recentPasswords.Remove(password);

            recentPasswords.Insert(0, password);

            while (recentPasswords.Count > MaxHistoryItems)
                recentPasswords.RemoveAt(recentPasswords.Count - 1);

            // Save and reload to maintain proper order with known passwords
            string historyKey = GetHistorySettingKey();
            string historyValue = string.Join(";", recentPasswords);
            SetSettingValue(historyKey, historyValue);

            try
            {
                Properties.Settings.Default.Save();
            }
            catch { }

            LoadPasswordHistory();
        }

        private string GetHistorySettingKey()
        {
            return string.IsNullOrEmpty(FormatTag)
                ? "PasswordHistory"
                : $"{FormatTag}_PasswordHistory";
        }

        private string GetPasswordSettingKey()
        {
            return string.IsNullOrEmpty(FormatTag)
                ? "Password"
                : $"{FormatTag}_Password";
        }

        private string GetSettingValue(string key)
        {
            try
            {
                var settings = Properties.Settings.Default;

                var property = settings.GetType().GetProperty(key);
                if (property != null)
                {
                    return property.GetValue(settings) as string ?? string.Empty;
                }

                if (settings[key] != null)
                    return settings[key].ToString();
            }
            catch
            {
                // Setting doesn't exist or error accessing it
            }
            return string.Empty;
        }

        private void SetSettingValue(string key, string value)
        {
            try
            {
                var settings = Properties.Settings.Default;

                var property = settings.GetType().GetProperty(key);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(settings, value);
                    return;
                }

                settings[key] = value;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set setting {key}: {ex.Message}");
            }
        }

        private void PasswordComboBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == InputKey.Enter)
            {
                var comboBox = sender as ComboBox;
                if (comboBox != null && !string.IsNullOrWhiteSpace(comboBox.Text))
                {
                    // Just close dropdown on Enter, don't add to history yet
                    // Password will be added when successfully used
                    comboBox.IsDropDownOpen = false;

                    // Save as current password for format
                    if (!string.IsNullOrWhiteSpace(comboBox.Text))
                    {
                        string passwordKey = GetPasswordSettingKey();
                        SetSettingValue(passwordKey, comboBox.Text);

                        try
                        {
                            Properties.Settings.Default.Save();
                        }
                        catch { }
                    }
                }
            }
        }

        private void PasswordComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            if (comboBox != null && !string.IsNullOrWhiteSpace(comboBox.Text))
            {
                string passwordKey = GetPasswordSettingKey();
                SetSettingValue(passwordKey, comboBox.Text);

                try
                {
                    Properties.Settings.Default.Save();
                }
                catch { }
            }
        }

        private void PasswordComboBox_DropDownOpened(object sender, EventArgs e)
        {
            //LoadPasswordHistory();
        }

        private void PasswordComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            if (comboBox != null && comboBox.SelectedItem != null)
            {
                var selectedPassword = comboBox.SelectedItem.ToString();
                if (selectedPassword == passKnownSeparator)
                {
                    e.Handled = true;
                    comboBox.SelectedIndex = -1;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

// ====================== Usage Examples ======================
/*
namespace GameRes.Formats.Examples
{
    // Options class to hold the password
    public class EncryptedFormatOptions : ResourceOptions
    {
        public string Password { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class EncryptedPakOpener : ArchiveFormat
    {
        // Method 1: Using GetAccessWidget and GetOptions
        public override object GetAccessWidget()
        {
            return new WidgetPassword 
            { 
                FormatTag = this.Tag,
                Scheme = this.Scheme  // Optional: will auto-load known passwords
            };
        }

        public override object GetOptions(object widget)
        {
            var passwordWidget = widget as WidgetPassword;
            if (passwordWidget != null)
            {
                return new EncryptedFormatOptions
                {
                    Password = passwordWidget.Password  // Get current password from widget
                };
            }
            return GetDefaultOptions();
        }

        public override ResourceOptions GetDefaultOptions()
        {
            return new EncryptedFormatOptions 
            { 
                Password = Properties.Settings.Default.Password 
            };
        }

        // Method 2: Using Query<T> in TryOpen with password history
        public override ArcFile TryOpen(ArcView file)
        {
            List<Entry> dir = null;
            if (IsEncrypted(file)) 
            {
                var options = Query<EncryptedFormatOptions>(Localization._T ("ArcEncryptedNotice"));
                if (options == null)
                    return null;

                string password = options.Password;
                if (string.IsNullOrEmpty(password))
                    return null;
    
                dir = DecryptAndReadIndex(file, password);
                if (dir == null)
                    return null;
                    
                // Password worked! Save it to history
                WidgetPassword.MarkPasswordAsSuccessful(this.Tag, password);
            }
            else 
            {
                dir = ReadIndex(file);
                if (dir == null)
                    return null;
            }

            return new ArcFile(file, this, dir);
        }

        // Method 3: Manual widget handling with success tracking
        public override ArcFile TryOpen(ArcView file)
        {
            if (!IsEncrypted(file))
                return null;

            // Create widget directly
            var widget = new WidgetPassword 
            { 
                FormatTag = this.Tag,
                Scheme = this.Scheme
            };
            
            // Show dialog with widget
            var options = Query<EncryptedFormatOptions>(Localization._T ("ArcEncryptedNotice"));
            if (options == null)
                return null;
            
            string password = options.Password;
            if (string.IsNullOrEmpty(password))
                return null;
            
            var arc = OpenWithPassword(file, password);
            if (arc != null)
            {
                // Success - save password to history
                widget.Password = password;
                widget.MarkPasswordAsSuccessful();
            }
            
            return arc;
        }

        // Method 4: Using ResourceScheme with known passwords
        public class EncryptedScheme : ResourceScheme
        {
            public Dictionary<string, string> KnownPasswords { get; set; }
            public List<string> CommonKeys { get; set; }
            
            public EncryptedScheme()
            {
                KnownPasswords = new Dictionary<string, string>
                {
                    { "Game1", "password123" },
                    { "Game2", "secretkey" }
                };
                
                CommonKeys = new List<string>
                {
                    "defaultpass",
                    "12345678"
                };
            }
            
            // Optional: Method for widget to discover passwords
            public IEnumerable<string> GetPasswords()
            {
                foreach (var kvp in KnownPasswords)
                    yield return kvp.Value;
                foreach (var key in CommonKeys)
                    yield return key;
            }
        }

        // Method 5: Getting password in different scenarios
        private string GetPasswordForFile(ArcView file)
        {
            // Try last successful password for this format
            if (WidgetPassword._successfulPasswords.ContainsKey(this.Tag))
            {
                var lastPassword = WidgetPassword._successfulPasswords[this.Tag];
                if (TestPassword(file, lastPassword))
                    return lastPassword;
            }
            
            // Try saved password for format
            var savedPassword = Properties.Settings.Default[$"{this.Tag}_Password"] as string;
            if (!string.IsNullOrEmpty(savedPassword) && TestPassword(file, savedPassword))
            {
                return savedPassword;
            }
            
            // Ask user
            var options = Query<EncryptedFormatOptions>(Localization._T ("ArcEncryptedNotice"));
            return options?.Password;
        }
    }
}
*/