using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;

namespace SchemeEditor
{
    public partial class AddListItemDialog : Window
    {
        public object Value { get; private set; }

        private Type itemType;
        private FrameworkElement valueEditor;
        private ControlFactory controlFactory;

        public AddListItemDialog(Type itemType)
        {
            InitializeComponent();
            this.itemType = itemType;
            this.controlFactory = new ControlFactory();

            Title = $"Add {itemType.Name} Item";
            CreateValueEditor();
        }

        private void CreateValueEditor()
        {
            // Reuse the same editor creation logic from AddDictionaryEntryDialog
            valueEditor = CreateEditorForType(itemType, null, (value) => Value = value);
            ValueEditorContainer.Children.Add(valueEditor);
        }

        private FrameworkElement CreateEditorForType(Type type, object currentValue, Action<object> onValueChanged)
        {
            if (type == typeof(string))
            {
                Value = "";
                var textBox = controlFactory.CreateTextBox(currentValue?.ToString() ?? "",
                    (text) => onValueChanged(text));
                return textBox;
            }
            else if (type == typeof(bool))
            {
                Value = false;
                var checkBox = controlFactory.CreateCheckBox((bool)(currentValue ?? false),
                    (isChecked) => onValueChanged(isChecked));
                return checkBox;
            }
            else if (type.IsEnum)
            {
                Value = Enum.GetValues(type).GetValue(0);
                var comboBox = controlFactory.CreateEnumComboBox(type, currentValue ?? Value,
                    (selected) => onValueChanged(selected));
                return comboBox;
            }
            else if (type.IsPrimitive || type == typeof(decimal))
            {
                if (currentValue == null)
                    currentValue = Activator.CreateInstance(type);
                Value = currentValue;

                var textBox = controlFactory.CreateNumericTextBox(type, currentValue,
                    (value) => {
                        Value = value;
                        onValueChanged(value);
                    });
                return textBox;
            }
            else if (type == typeof(byte[]))
            {
                Value = new byte[0];
                return CreateByteArrayEditor(onValueChanged);
            }
            else if (type.IsArray)
            {
                var elementType = type.GetElementType();
                Value = Array.CreateInstance(elementType, 0);
                return CreateArrayEditor(type, onValueChanged);
            }
            else if (type.IsClass || type.IsInterface)
            {
                Value = null; // Classes can be null
                return CreateComplexTypeEditor(type, onValueChanged);
            }
            else
            {
                // For other value types, create proper default
                Value = type.IsValueType ? Activator.CreateInstance(type) : null;

                var textBox = new TextBox { Text = Value?.ToString() ?? "" };
                textBox.TextChanged += (s, e) =>
                {
                    try
                    {
                        var value = Convert.ChangeType(textBox.Text, type);
                        Value = value;
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

            // Initialize with empty byte array
            Value = new byte[0];

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
                            bytes = string.IsNullOrEmpty(text) ? new byte[0] : StringToByteArray(text);
                            break;
                        case "Text":
                            bytes = System.Text.Encoding.UTF8.GetBytes(text);
                            break;
                        case "Base64":
                            bytes = string.IsNullOrEmpty(text) ? new byte[0] : Convert.FromBase64String(text);
                            break;
                    }

                    Value = bytes;
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
                        var values = textBox.Text.Split(new[] { ",", "\n", "\r" }, 
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
                var createButton = new Button
                {
                    Content = "Create Empty Array",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 5, 10, 5)
                };

                createButton.Click += (s, e) =>
                {
                    var array = Array.CreateInstance(elementType, 0);
                    onValueChanged(array);
                    createButton.Content = "Empty Array Created ✓";
                    createButton.IsEnabled = false;
                };

                stackPanel.Children.Add(createButton);
            }

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
            if (Value == null && !itemType.IsClass)
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