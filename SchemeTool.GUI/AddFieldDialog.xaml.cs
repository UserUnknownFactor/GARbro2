using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using GameRes;

namespace SchemeEditor
{
    public partial class AddFieldDialog : Window
    {
        public string FieldName { get; private set; }
        public Type FieldType { get; private set; }
        public object DefaultValue { get; private set; }

        private FrameworkElement defaultValueEditor;
        private ControlFactory controlFactory;

        private class TypeOption
        {
            public string Category { get; set; }
            public string DisplayName { get; set; }
            public Type Type { get; set; }
            public bool IsCustom { get; set; }

            public override string ToString() => DisplayName;
        }

        public AddFieldDialog()
        {
            InitializeComponent();
            controlFactory = new ControlFactory();
            InitializeFieldTypes();
        }

        private void InitializeFieldTypes()
        {
            var typeOptions = new List<TypeOption>();

            // Basic types
            typeOptions.Add(new TypeOption { Category = "Basic", DisplayName = "String", Type = typeof(string) });
            typeOptions.Add(new TypeOption { Category = "Basic", DisplayName = "Integer (Int32)", Type = typeof(int) });
            typeOptions.Add(new TypeOption { Category = "Basic", DisplayName = "Unsigned Integer (UInt32)", Type = typeof(uint) });
            typeOptions.Add(new TypeOption { Category = "Basic", DisplayName = "Long (Int64)", Type = typeof(long) });
            typeOptions.Add(new TypeOption { Category = "Basic", DisplayName = "Unsigned Long (UInt64)", Type = typeof(ulong) });
            typeOptions.Add(new TypeOption { Category = "Basic", DisplayName = "Short (Int16)", Type = typeof(short) });
            typeOptions.Add(new TypeOption { Category = "Basic", DisplayName = "Unsigned Short (UInt16)", Type = typeof(ushort) });
            typeOptions.Add(new TypeOption { Category = "Basic", DisplayName = "Byte", Type = typeof(byte) });
            typeOptions.Add(new TypeOption { Category = "Basic", DisplayName = "Signed Byte (SByte)", Type = typeof(sbyte) });
            typeOptions.Add(new TypeOption { Category = "Basic", DisplayName = "Boolean", Type = typeof(bool) });
            typeOptions.Add(new TypeOption { Category = "Basic", DisplayName = "Float", Type = typeof(float) });
            typeOptions.Add(new TypeOption { Category = "Basic", DisplayName = "Double", Type = typeof(double) });
            typeOptions.Add(new TypeOption { Category = "Basic", DisplayName = "Decimal", Type = typeof(decimal) });

            // Arrays
            typeOptions.Add(new TypeOption { Category = "Arrays", DisplayName = "Byte Array (byte[])", Type = typeof(byte[]) });
            typeOptions.Add(new TypeOption { Category = "Arrays", DisplayName = "Integer Array (int[])", Type = typeof(int[]) });
            typeOptions.Add(new TypeOption { Category = "Arrays", DisplayName = "UInt32 Array (uint[])", Type = typeof(uint[]) });
            typeOptions.Add(new TypeOption { Category = "Arrays", DisplayName = "String Array (string[])", Type = typeof(string[]) });

            // Collections
            typeOptions.Add(new TypeOption { Category = "Collections", DisplayName = "List<string>", Type = typeof(List<string>) });
            typeOptions.Add(new TypeOption { Category = "Collections", DisplayName = "List<int>", Type = typeof(List<int>) });
            typeOptions.Add(new TypeOption { Category = "Collections", DisplayName = "List<uint>", Type = typeof(List<uint>) });
            typeOptions.Add(new TypeOption { Category = "Collections", DisplayName = "List<byte[]>", Type = typeof(List<byte[]>) });
            typeOptions.Add(new TypeOption { Category = "Collections", DisplayName = "Dictionary<string, string>", Type = typeof(Dictionary<string, string>) });
            typeOptions.Add(new TypeOption { Category = "Collections", DisplayName = "Dictionary<string, uint>", Type = typeof(Dictionary<string, uint>) });
            typeOptions.Add(new TypeOption { Category = "Collections", DisplayName = "Dictionary<string, uint[]>", Type = typeof(Dictionary<string, uint[]>) });
            typeOptions.Add(new TypeOption { Category = "Collections", DisplayName = "HashSet<string>", Type = typeof(HashSet<string>) });
            typeOptions.Add(new TypeOption { Category = "Collections", DisplayName = "HashSet<uint>", Type = typeof(HashSet<uint>) });

            // Find all serializable classes in loaded assemblies
            var serializableTypes = FindSerializableTypes();
            foreach (var type in serializableTypes)
            {
                string category = "Custom Types";
                if (type.Namespace != null)
                {
                    if (type.Namespace.StartsWith("GameRes"))
                        category = "GameRes Types";
                    else if (type.Namespace.StartsWith("System"))
                        category = "System Types";
                }

                typeOptions.Add(new TypeOption 
                { 
                    Category = category, 
                    DisplayName = $"{type.Name} ({type.Namespace})", 
                    Type = type,
                    IsCustom = true
                });
            }

            // Find enums
            var enumTypes = FindEnumTypes();
            foreach (var enumType in enumTypes)
            {
                typeOptions.Add(new TypeOption 
                { 
                    Category = "Enumerations", 
                    DisplayName = $"{enumType.Name} (enum)", 
                    Type = enumType 
                });
            }

            // Group by category
            var groupedTypes = typeOptions
                .GroupBy(t => t.Category)
                .OrderBy(g => g.Key == "Basic" ? 0 : g.Key == "Arrays" ? 1 : g.Key == "Collections" ? 2 : 3);

            var allItems = new List<object>();
            foreach (var group in groupedTypes)
            {
                // Add category header
                allItems.Add(new ComboBoxItem 
                { 
                    Content = $"--- {group.Key} ---", 
                    IsEnabled = false,
                    FontWeight = FontWeights.Bold
                });

                // Add items in category
                foreach (var item in group.OrderBy(t => t.DisplayName))
                {
                    allItems.Add(item);
                }
            }

            FieldTypeComboBox.ItemsSource = allItems;
            
            // Select first actual type (skip category header)
            for (int i = 0; i < allItems.Count; i++)
            {
                if (allItems[i] is TypeOption)
                {
                    FieldTypeComboBox.SelectedIndex = i;
                    break;
                }
            }
        }

        private List<Type> FindSerializableTypes()
        {
            var types = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var assemblyTypes = assembly.GetTypes()
                        .Where(t => t.IsClass && 
                                   !t.IsAbstract && 
                                   !t.IsGenericType &&
                                   t.IsPublic &&
                                   !t.IsNested &&
                                   !typeof(Exception).IsAssignableFrom(t) &&
                                   !typeof(Attribute).IsAssignableFrom(t) &&
                                   !typeof(Delegate).IsAssignableFrom(t) &&
                                   (t.IsSerializable || 
                                    t.GetCustomAttribute<SerializableAttribute>() != null ||
                                    t.GetCustomAttribute<DataContractAttribute>() != null ||
                                    typeof(ResourceScheme).IsAssignableFrom(t) ||
                                    (t.GetConstructor(Type.EmptyTypes) != null && // Has parameterless constructor
                                     t.Namespace != null &&
                                     !t.Namespace.StartsWith("System.") &&
                                     !t.Namespace.StartsWith("Microsoft."))))
                        .ToList();

                    types.AddRange(assemblyTypes);
                }
                catch
                {
                    // Skip assemblies that can't be loaded
                }
            }

            return types.Distinct().OrderBy(t => t.Name).ToList();
        }

        private List<Type> FindEnumTypes()
        {
            var types = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var enumTypes = assembly.GetTypes()
                        .Where(t => t.IsEnum && t.IsPublic)
                        .ToList();

                    types.AddRange(enumTypes);
                }
                catch
                {
                    // Skip assemblies that can't be loaded
                }
            }

            return types.Distinct().OrderBy(t => t.Name).ToList();
        }

        private void FieldTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FieldTypeComboBox.SelectedItem is TypeOption typeOption)
            {
                FieldType = typeOption.Type;
                CreateDefaultValueEditor();
            }
        }

        private void CreateDefaultValueEditor()
        {
            DefaultValueContainer.Children.Clear();

            if (FieldType == null) return;

            defaultValueEditor = CreateEditorForType(FieldType, null, (value) => DefaultValue = value);
            DefaultValueContainer.Children.Add(defaultValueEditor);

            // Set initial default value
            if (FieldType == typeof(string))
            {
                DefaultValue = "";
            }
            else if (FieldType == typeof(bool))
            {
                DefaultValue = false;
            }
            else if (FieldType.IsPrimitive)
            {
                DefaultValue = Activator.CreateInstance(FieldType);
            }
            else if (FieldType == typeof(byte[]))
            {
                DefaultValue = new byte[0];
            }
            else if (FieldType.IsArray)
            {
                DefaultValue = Array.CreateInstance(FieldType.GetElementType(), 0);
            }
            else if (FieldType.IsGenericType)
            {
                try
                {
                    DefaultValue = Activator.CreateInstance(FieldType);
                }
                catch
                {
                    DefaultValue = null;
                }
            }
        }

        private FrameworkElement CreateEditorForType(Type type, object currentValue, Action<object> onValueChanged)
        {
            if (type == typeof(string))
            {
                var textBox = controlFactory.CreateTextBox(currentValue?.ToString() ?? "", 
                    (text) => onValueChanged(text));
                return textBox;
            }
            else if (type == typeof(bool))
            {
                var checkBox = controlFactory.CreateCheckBox((bool)(currentValue ?? false),
                    (isChecked) => onValueChanged(isChecked));
                return checkBox;
            }
            else if (type.IsEnum)
            {
                var comboBox = controlFactory.CreateEnumComboBox(type, currentValue ?? Enum.GetValues(type).GetValue(0),
                    (selected) => onValueChanged(selected));
                return comboBox;
            }
            else if (type.IsPrimitive || type == typeof(decimal))
            {
                var textBox = controlFactory.CreateNumericTextBox(type, currentValue ?? Activator.CreateInstance(type),
                    (value) => onValueChanged(value));
                return textBox;
            }
            else if (type == typeof(byte[]))
            {
                return CreateByteArrayEditor(onValueChanged);
            }
            else if (type.IsArray)
            {
                return CreateArrayEditor(type, onValueChanged);
            }
            else if (type.IsGenericType)
            {
                var genericDef = type.GetGenericTypeDefinition();
                if (genericDef == typeof(List<>))
                {
                    return CreateListEditor(type, onValueChanged);
                }
                else if (genericDef == typeof(Dictionary<,>))
                {
                    return CreateDictionaryEditor(type, onValueChanged);
                }
                else if (genericDef == typeof(HashSet<>))
                {
                    return CreateHashSetEditor(type, onValueChanged);
                }
            }
            
            // For complex types
            return CreateComplexTypeEditor(type, onValueChanged);
        }

        private FrameworkElement CreateByteArrayEditor(Action<object> onValueChanged)
        {
            var stackPanel = new StackPanel();
            
            var radioPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            var emptyRadio = new RadioButton { Content = "Empty array", IsChecked = true, Margin = new Thickness(0, 0, 10, 0) };
            var dataRadio = new RadioButton { Content = "With data", Margin = new Thickness(0, 0, 10, 0) };
            radioPanel.Children.Add(emptyRadio);
            radioPanel.Children.Add(dataRadio);
            stackPanel.Children.Add(radioPanel);

            var dataPanel = new StackPanel { Visibility = Visibility.Collapsed };
            
            var textBox = new TextBox
            {
                Height = 60,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var modeCombo = new ComboBox
            {
                ItemsSource = new[] { "Hex", "Text", "Base64" },
                SelectedIndex = 0,
                Margin = new Thickness(0, 5, 0, 0)
            };

            Action updateValue = () =>
            {
                try
                {
                    if (emptyRadio.IsChecked == true)
                    {
                        onValueChanged(new byte[0]);
                    }
                    else
                    {
                        byte[] bytes = null;
                        var text = textBox.Text.Trim();
                        
                        switch (modeCombo.SelectedItem as string)
                        {
                            case "Hex":
                                bytes = StringToByteArray(text);
                                break;
                            case "Text":
                                bytes = System.Text.Encoding.UTF8.GetBytes(text);
                                break;
                            case "Base64":
                                bytes = Convert.FromBase64String(text);
                                break;
                        }
                        
                        onValueChanged(bytes ?? new byte[0]);
                    }
                }
                catch 
                {
                    onValueChanged(new byte[0]);
                }
            };

            emptyRadio.Checked += (s, e) => { dataPanel.Visibility = Visibility.Collapsed; updateValue(); };
            dataRadio.Checked += (s, e) => { dataPanel.Visibility = Visibility.Visible; updateValue(); };
            textBox.TextChanged += (s, e) => updateValue();
            modeCombo.SelectionChanged += (s, e) => updateValue();

            dataPanel.Children.Add(new TextBlock { Text = "Enter data:", Margin = new Thickness(0, 5, 0, 5) });
            dataPanel.Children.Add(textBox);
            dataPanel.Children.Add(modeCombo);

            stackPanel.Children.Add(dataPanel);

            // Initialize with empty array
            onValueChanged(new byte[0]);

            return stackPanel;
        }

        private FrameworkElement CreateArrayEditor(Type arrayType, Action<object> onValueChanged)
        {
            var elementType = arrayType.GetElementType();
            var stackPanel = new StackPanel();

            stackPanel.Children.Add(new TextBlock 
            { 
                Text = $"Default: Empty {elementType.Name}[]",
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 0, 0, 5)
            });

            if (elementType.IsPrimitive || elementType == typeof(string) || elementType == typeof(decimal))
            {
                var checkBox = new CheckBox 
                { 
                    Content = "Initialize with values",
                    Margin = new Thickness(0, 0, 0, 5)
                };

                var valuesPanel = new StackPanel { Visibility = Visibility.Collapsed };
                
                valuesPanel.Children.Add(new TextBlock 
                { 
                    Text = "Enter comma-separated values:",
                    Margin = new Thickness(0, 0, 0, 5)
                });

                var textBox = new TextBox
                {
                    Height = 60,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true
                };

                Action updateValue = () =>
                {
                    try
                    {
                        if (checkBox.IsChecked != true)
                        {
                            var emptyArray = Array.CreateInstance(elementType, 0);
                            onValueChanged(emptyArray);
                        }
                        else
                        {
                            var values = textBox.Text.Split(new[] { ',', '\n', '\r' }, 
                                StringSplitOptions.RemoveEmptyEntries)
                                .Select(v => v.Trim())
                                .Where(v => !string.IsNullOrEmpty(v))
                                .ToArray();

                            var array = Array.CreateInstance(elementType, values.Length);
                            for (int i = 0; i < values.Length; i++)
                            {
                                if (elementType == typeof(string))
                                    array.SetValue(values[i], i);
                                else
                                    array.SetValue(Convert.ChangeType(values[i], elementType), i);
                            }
                            
                            onValueChanged(array);
                        }
                    }
                    catch 
                    {
                        var emptyArray = Array.CreateInstance(elementType, 0);
                        onValueChanged(emptyArray);
                    }
                };

                checkBox.Checked += (s, e) => { valuesPanel.Visibility = Visibility.Visible; updateValue(); };
                checkBox.Unchecked += (s, e) => { valuesPanel.Visibility = Visibility.Collapsed; updateValue(); };
                textBox.TextChanged += (s, e) => updateValue();

                valuesPanel.Children.Add(textBox);

                stackPanel.Children.Add(checkBox);
                stackPanel.Children.Add(valuesPanel);

                // Initialize with empty array
                var empty = Array.CreateInstance(elementType, 0);
                onValueChanged(empty);
            }
            else
            {
                // For complex element types, just create empty array
                var empty = Array.CreateInstance(elementType, 0);
                onValueChanged(empty);
                
                stackPanel.Children.Add(new TextBlock 
                { 
                    Text = "Complex array types start empty. Add items after field creation.",
                    FontStyle = FontStyles.Italic,
                    Foreground = System.Windows.Media.Brushes.Gray
                });
            }

            return stackPanel;
        }

        private FrameworkElement CreateListEditor(Type listType, Action<object> onValueChanged)
        {
            var elementType = listType.GetGenericArguments()[0];
            var stackPanel = new StackPanel();

            stackPanel.Children.Add(new TextBlock 
            { 
                Text = $"Default: Empty List<{elementType.Name}>",
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 0, 0, 5)
            });

            // Create empty list by default
            var list = Activator.CreateInstance(listType);
            onValueChanged(list);

            stackPanel.Children.Add(new TextBlock 
            { 
                Text = "List will be created empty. Add items after field creation.",
                FontStyle = FontStyles.Italic,
                Foreground = System.Windows.Media.Brushes.Gray
            });

            return stackPanel;
        }

        private FrameworkElement CreateDictionaryEditor(Type dictType, Action<object> onValueChanged)
        {
            var genericArgs = dictType.GetGenericArguments();
            var keyType = genericArgs[0];
            var valueType = genericArgs[1];

            var stackPanel = new StackPanel();

            stackPanel.Children.Add(new TextBlock 
            { 
                Text = $"Default: Empty Dictionary<{keyType.Name}, {valueType.Name}>",
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 0, 0, 5)
            });

            // Create empty dictionary by default
            var dict = Activator.CreateInstance(dictType);
            onValueChanged(dict);

            stackPanel.Children.Add(new TextBlock 
            { 
                Text = "Dictionary will be created empty. Add entries after field creation.",
                FontStyle = FontStyles.Italic,
                Foreground = System.Windows.Media.Brushes.Gray
            });

            return stackPanel;
        }

        private FrameworkElement CreateHashSetEditor(Type setType, Action<object> onValueChanged)
        {
            var elementType = setType.GetGenericArguments()[0];
            var stackPanel = new StackPanel();

            stackPanel.Children.Add(new TextBlock 
            { 
                Text = $"Default: Empty HashSet<{elementType.Name}>",
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 0, 0, 5)
            });

            // Create empty set by default
            var set = Activator.CreateInstance(setType);
            onValueChanged(set);

            stackPanel.Children.Add(new TextBlock 
            { 
                Text = "HashSet will be created empty. Add items after field creation.",
                FontStyle = FontStyles.Italic,
                Foreground = System.Windows.Media.Brushes.Gray
            });

            return stackPanel;
        }

        private FrameworkElement CreateComplexTypeEditor(Type type, Action<object> onValueChanged)
        {
            var stackPanel = new StackPanel();

            var radioPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            var nullRadio = new RadioButton { Content = "Null", IsChecked = true, Margin = new Thickness(0, 0, 10, 0) };
            var createRadio = new RadioButton { Content = "Create instance", Margin = new Thickness(0, 0, 10, 0) };
            radioPanel.Children.Add(nullRadio);
            radioPanel.Children.Add(createRadio);
            stackPanel.Children.Add(radioPanel);

            var instancePanel = new StackPanel { Visibility = Visibility.Collapsed };

            if (type.IsInterface || type.IsAbstract)
            {
                instancePanel.Children.Add(new TextBlock 
                { 
                    Text = "Select Implementation:",
                    Margin = new Thickness(0, 5, 0, 5)
                });

                var types = TypeHelper.GetDerivedTypes(type);
                var typeCombo = new ComboBox
                {
                    ItemsSource = types,
                    DisplayMemberPath = "Name",
                    SelectedIndex = types.Length > 0 ? 0 : -1,
                    MaxWidth = 300
                };

                var statusText = new TextBlock 
                { 
                    Text = "Ready to create",
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 5, 0, 0)
                };

                Action updateValue = () =>
                {
                    if (nullRadio.IsChecked == true)
                    {
                        onValueChanged(null);
                        statusText.Text = "Field will be null";
                    }
                    else if (typeCombo.SelectedItem is Type selectedType)
                    {
                        try
                        {
                            var instance = Activator.CreateInstance(selectedType);
                            onValueChanged(instance);
                            statusText.Text = $"Created {selectedType.Name} instance";
                            statusText.Foreground = System.Windows.Media.Brushes.Green;
                        }
                        catch (Exception ex)
                        {
                            onValueChanged(null);
                            statusText.Text = $"Error: {ex.Message}";
                            statusText.Foreground = System.Windows.Media.Brushes.Red;
                        }
                    }
                };

                nullRadio.Checked += (s, e) => { instancePanel.Visibility = Visibility.Collapsed; updateValue(); };
                createRadio.Checked += (s, e) => { instancePanel.Visibility = Visibility.Visible; updateValue(); };
                typeCombo.SelectionChanged += (s, e) => updateValue();

                instancePanel.Children.Add(typeCombo);
                instancePanel.Children.Add(statusText);
            }
            else
            {
                var statusText = new TextBlock 
                { 
                    Text = $"Will create instance of {type.Name}",
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 5, 0, 0)
                };

                Action updateValue = () =>
                {
                    if (nullRadio.IsChecked == true)
                    {
                        onValueChanged(null);
                        statusText.Text = "Field will be null";
                        statusText.Foreground = System.Windows.Media.Brushes.Black;
                    }
                    else
                    {
                        try
                        {
                            var instance = Activator.CreateInstance(type);
                            onValueChanged(instance);
                            statusText.Text = $"Created {type.Name} instance";
                            statusText.Foreground = System.Windows.Media.Brushes.Green;
                        }
                        catch (Exception ex)
                        {
                            onValueChanged(null);
                            statusText.Text = $"Error: {ex.Message}";
                            statusText.Foreground = System.Windows.Media.Brushes.Red;
                        }
                    }
                };

                nullRadio.Checked += (s, e) => { instancePanel.Visibility = Visibility.Collapsed; updateValue(); };
                createRadio.Checked += (s, e) => { instancePanel.Visibility = Visibility.Visible; updateValue(); };

                instancePanel.Children.Add(statusText);
            }

            stackPanel.Children.Add(instancePanel);

            // Initialize with null
            onValueChanged(null);

            return stackPanel;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FieldNameTextBox.Text))
            {
                MessageBox.Show("Please enter a field name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (FieldType == null)
            {
                MessageBox.Show("Please select a field type.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            FieldName = FieldNameTextBox.Text.Trim();
            
            // Ensure we have a valid default value
            if (DefaultValue == null && FieldType.IsValueType)
            {
                DefaultValue = Activator.CreateInstance(FieldType);
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private static byte[] StringToByteArray(string hex)
        {
            hex = hex.Replace(" ", "").Replace("-", "").Replace("\r", "").Replace("\n", "");
            if (string.IsNullOrEmpty(hex))
                return new byte[0];
                
            if (hex.Length % 2 != 0)
                hex = "0" + hex;

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
    }
}