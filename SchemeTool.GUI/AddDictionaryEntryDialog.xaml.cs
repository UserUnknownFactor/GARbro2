using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Collections.Generic;

namespace SchemeEditor
{
    public partial class AddDictionaryEntryDialog : Window
    {
        public object Key { get; private set; }
        public object Value { get; private set; }

        private Type keyType;
        private Type valueType;
        private FrameworkElement keyEditor;
        private FrameworkElement valueEditor;
        private ControlFactory controlFactory;

        public AddDictionaryEntryDialog(Type keyType, Type valueType)
        {
            InitializeComponent();
            this.keyType = keyType;
            this.valueType = valueType;
            this.controlFactory = new ControlFactory();

            Title = $"Add {keyType.Name} → {valueType.Name} Entry";

            CreateKeyEditor();
            CreateValueEditor();
        }

        private void CreateKeyEditor()
        {
            keyEditor = CreateEditorForType(keyType, null, (value) => Key = value);
            KeyEditorContainer.Children.Add(keyEditor);
        }

        private void CreateValueEditor()
        {
            valueEditor = CreateEditorForType(valueType, null, (value) => Value = value);
            ValueEditorContainer.Children.Add(valueEditor);
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
                var comboBox = controlFactory.CreateEnumComboBox(type, currentValue,
                    (selected) => onValueChanged(selected));
                return comboBox;
            }
            else if (type.IsPrimitive || type == typeof(decimal))
            {
                var textBox = controlFactory.CreateNumericTextBox(type, currentValue,
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
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                return CreateListEditor(type, onValueChanged);
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                return CreateDictionaryEditor(type, onValueChanged);
            }
            else if (type.IsClass || type.IsInterface)
            {
                return CreateComplexTypeEditor(type, onValueChanged);
            }
            else
            {
                var textBox = new TextBox { Text = "" };
                textBox.TextChanged += (s, e) =>
                {
                    try
                    {
                        var value = Convert.ChangeType(textBox.Text, type);
                        onValueChanged(value);
                    }
                    catch { }
                };
                return textBox;
            }
        }

        private FrameworkElement CreateByteArrayEditor(Action<object> onValueChanged)
        {
            var stackPanel = new StackPanel();
            
            stackPanel.Children.Add(new TextBlock 
            { 
                Text = "Enter data as hex (e.g., FF 00 1A) or text:",
                Margin = new Thickness(0, 0, 0, 5)
            });

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
                    
                    onValueChanged(bytes);
                }
                catch { }
            };

            textBox.TextChanged += (s, e) => updateValue();
            modeCombo.SelectionChanged += (s, e) => updateValue();

            stackPanel.Children.Add(textBox);
            stackPanel.Children.Add(modeCombo);

            return stackPanel;
        }

        private FrameworkElement CreateArrayEditor(Type arrayType, Action<object> onValueChanged)
        {
            var elementType = arrayType.GetElementType();
            var stackPanel = new StackPanel();

            stackPanel.Children.Add(new TextBlock 
            { 
                Text = $"Array of {elementType.Name}",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            });

            if (elementType.IsPrimitive || elementType == typeof(string) || elementType == typeof(decimal))
            {
                stackPanel.Children.Add(new TextBlock 
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

                textBox.TextChanged += (s, e) =>
                {
                    try
                    {
                        var values = textBox.Text.Split(new[] { ",", "\n", "\r"}, 
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
                    catch { }
                };

                stackPanel.Children.Add(textBox);
            }
            else
            {
                // For complex element types
                var sizePanel = new StackPanel { Orientation = Orientation.Horizontal };
                sizePanel.Children.Add(new TextBlock 
                { 
                    Text = "Array Size: ",
                    VerticalAlignment = VerticalAlignment.Center 
                });

                var sizeTextBox = new TextBox 
                { 
                    Text = "0", 
                    Width = 50 
                };

                var createButton = new Button
                {
                    Content = "Create Empty Array",
                    Margin = new Thickness(10, 0, 0, 0)
                };

                createButton.Click += (s, e) =>
                {
                    if (int.TryParse(sizeTextBox.Text, out int size) && size >= 0)
                    {
                        var array = Array.CreateInstance(elementType, size);
                        onValueChanged(array);
                        createButton.Content = $"Array[{size}] Created ✓";
                    }
                };

                sizePanel.Children.Add(sizeTextBox);
                sizePanel.Children.Add(createButton);
                stackPanel.Children.Add(sizePanel);
            }

            return stackPanel;
        }

        private FrameworkElement CreateListEditor(Type listType, Action<object> onValueChanged)
        {
            var elementType = listType.GetGenericArguments()[0];
            var stackPanel = new StackPanel();

            stackPanel.Children.Add(new TextBlock 
            { 
                Text = $"List<{elementType.Name}>",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var createButton = new Button
            {
                Content = "Create Empty List",
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 5, 10, 5)
            };

            createButton.Click += (s, e) =>
            {
                var list = Activator.CreateInstance(listType);
                onValueChanged(list);
                createButton.Content = "List Created ✓";
                createButton.IsEnabled = false;
            };

            stackPanel.Children.Add(createButton);
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
                Text = $"Dictionary<{keyType.Name}, {valueType.Name}>",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var createButton = new Button
            {
                Content = "Create Empty Dictionary",
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 5, 10, 5)
            };

            createButton.Click += (s, e) =>
            {
                var dict = Activator.CreateInstance(dictType);
                onValueChanged(dict);
                createButton.Content = "Dictionary Created ✓";
                createButton.IsEnabled = false;
            };

            stackPanel.Children.Add(createButton);
            return stackPanel;
        }

        private FrameworkElement CreateComplexTypeEditor(Type type, Action<object> onValueChanged)
        {
            var stackPanel = new StackPanel();

            if (type.IsInterface || type.IsAbstract)
            {
                stackPanel.Children.Add(new TextBlock 
                { 
                    Text = "Select Implementation:",
                    Margin = new Thickness(0, 0, 0, 5)
                });

                var types = TypeHelper.GetDerivedTypes(type);
                var typeCombo = new ComboBox
                {
                    ItemsSource = types,
                    DisplayMemberPath = "Name",
                    SelectedIndex = types.Length > 0 ? 0 : -1,
                    MaxWidth = 300
                };

                var createButton = new Button
                {
                    Content = "Create Instance",
                    Margin = new Thickness(0, 5, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 5, 10, 5)
                };

                createButton.Click += (s, e) =>
                {
                    if (typeCombo.SelectedItem is Type selectedType)
                    {
                        try
                        {
                            var instance = Activator.CreateInstance(selectedType);
                            onValueChanged(instance);
                            createButton.Content = "Instance Created ✓";
                            createButton.IsEnabled = false;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Error creating instance: {ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                };

                stackPanel.Children.Add(typeCombo);
                stackPanel.Children.Add(createButton);
            }
            else
            {
                stackPanel.Children.Add(new TextBlock 
                { 
                    Text = $"Type: {type.Name}",
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 5)
                });

                var createButton = new Button
                {
                    Content = "Create New Instance",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 5, 10, 5)
                };

                createButton.Click += (s, e) =>
                {
                    try
                    {
                        var instance = Activator.CreateInstance(type);
                        onValueChanged(instance);
                        createButton.Content = "Instance Created ✓";
                        createButton.IsEnabled = false;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error creating instance: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                stackPanel.Children.Add(createButton);
            }

            return stackPanel;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // Validate key
            if (Key == null && !keyType.IsClass)
            {
                MessageBox.Show("Please provide a key value.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate value
            if (Value == null && !valueType.IsClass)
            {
                MessageBox.Show("Please provide a value.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
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
            if (hex.Length % 2 != 0)
                hex = "0" + hex; // Pad with leading zero

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
    }
}