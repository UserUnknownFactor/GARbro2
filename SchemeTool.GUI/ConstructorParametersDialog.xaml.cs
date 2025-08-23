using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace SchemeEditor
{
    public partial class ConstructorParametersDialog : Window
    {
        public object CreatedInstance { get; private set; }
        
        private Type targetType;
        private ConstructorInfo selectedConstructor;
        private List<FrameworkElement> parameterEditors = new List<FrameworkElement>();
        private ControlFactory controlFactory;

        public ConstructorParametersDialog(Type type)
        {
            InitializeComponent();
            targetType = type;
            controlFactory = new ControlFactory();
            
            Title = $"Create {type.Name}";
            HeaderText.Text = $"Creating instance of {type.Name}";
            
            SetupConstructorOptions();
        }

        private void SetupConstructorOptions()
        {
            var constructors = targetType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Where(c => c.GetParameters().Length <= 5) // Limit to reasonable parameter count
                .OrderBy(c => c.GetParameters().Length)
                .ToArray();

            if (constructors.Length == 0)
            {
                HeaderText.Text = $"No suitable constructors found for {targetType.Name}";
                return;
            }

            // If there's a parameterless constructor, prefer it
            var parameterlessConstructor = constructors.FirstOrDefault(c => c.GetParameters().Length == 0);
            if (parameterlessConstructor != null)
            {
                selectedConstructor = parameterlessConstructor;
                HeaderText.Text = $"Creating {targetType.Name} with default constructor";
                return;
            }

            // Show constructor options
            if (constructors.Length == 1)
            {
                selectedConstructor = constructors[0];
                SetupParameterEditors();
            }
            else
            {
                // Multiple constructors - let user choose
                HeaderText.Text = $"Select constructor for {targetType.Name}:";
                
                var constructorCombo = new ComboBox
                {
                    Margin = new Thickness(0, 0, 0, 10)
                };

                foreach (var ctor in constructors)
                {
                    var parameters = ctor.GetParameters();
                    var paramText = parameters.Length == 0 ? 
                        "Default constructor" : 
                        string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    
                    constructorCombo.Items.Add(new ComboBoxItem 
                    { 
                        Content = paramText, 
                        Tag = ctor 
                    });
                }

                constructorCombo.SelectedIndex = 0;
                constructorCombo.SelectionChanged += (s, e) =>
                {
                    if (constructorCombo.SelectedItem is ComboBoxItem item && item.Tag is ConstructorInfo ctor)
                    {
                        selectedConstructor = ctor;
                        SetupParameterEditors();
                    }
                };

                ParametersPanel.Children.Add(constructorCombo);
                
                // Setup initial parameters
                selectedConstructor = constructors[0];
                SetupParameterEditors();
            }
        }

        private void SetupParameterEditors()
        {
            // Clear existing parameter editors (but keep constructor selector if present)
            var toRemove = ParametersPanel.Children.OfType<FrameworkElement>()
                .Where(e => !(e is ComboBox))
                .ToList();
            
            foreach (var element in toRemove)
                ParametersPanel.Children.Remove(element);
            
            parameterEditors.Clear();

            if (selectedConstructor == null) return;

            var parameters = selectedConstructor.GetParameters();
            
            if (parameters.Length == 0)
            {
                HeaderText.Text = $"Creating {targetType.Name} with default constructor";
                return;
            }

            HeaderText.Text = $"Enter parameters for {targetType.Name}:";

            foreach (var param in parameters)
            {
                var paramPanel = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
                
                var label = new TextBlock 
                { 
                    Text = $"{param.Name} ({param.ParameterType.Name}):",
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 2)
                };
                paramPanel.Children.Add(label);

                FrameworkElement editor = CreateParameterEditor(param);
                paramPanel.Children.Add(editor);
                parameterEditors.Add(editor);

                ParametersPanel.Children.Add(paramPanel);
            }
        }

        private FrameworkElement CreateParameterEditor(ParameterInfo parameter)
        {
            var paramType = parameter.ParameterType;

            // Handle byte arrays with ByteArrayEditDialog
            if (paramType == typeof(byte[]))
            {
                var stackPanel = new StackPanel();
                stackPanel.Children.Add(new TextBlock
                {
                    Text = "Enter hex values (e.g., FF 00 AA) or leave empty for empty array:",
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 0, 0, 2)
                });

                var textBox = new TextBox
                {
                    Text = GetDefaultByteArrayValue(parameter.Name),
                    Height = 50,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true
                };
                stackPanel.Children.Add(textBox);
                return stackPanel;
            }
            // Handle other arrays
            else if (paramType.IsArray)
            {
                var elementType = paramType.GetElementType();
                var stackPanel = new StackPanel();

                if (elementType.IsPrimitive || elementType == typeof(string))
                {
                    // Primitive arrays (int[], uint[], string[], etc.)
                    stackPanel.Children.Add(new TextBlock
                    {
                        Text = $"Enter comma-separated {elementType.Name} values:",
                        FontStyle = FontStyles.Italic,
                        Margin = new Thickness(0, 0, 0, 2)
                    });

                    var textBox = new TextBox
                    {
                        Text = GetDefaultArrayValues(elementType, parameter.Name),
                        Height = 50,
                        TextWrapping = TextWrapping.Wrap,
                        AcceptsReturn = true
                    };
                    stackPanel.Children.Add(textBox);
                    return stackPanel;
                }
                else
                {
                    // Complex type arrays - provide creation options
                    stackPanel.Children.Add(new TextBlock
                    {
                        Text = $"Array of {elementType.Name}:",
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 5)
                    });

                    var optionsPanel = new StackPanel { Orientation = Orientation.Horizontal };

                    var emptyRadio = new RadioButton
                    {
                        Content = "Empty array",
                        IsChecked = true,
                        Margin = new Thickness(0, 0, 10, 0)
                    };

                    var sizeRadio = new RadioButton
                    {
                        Content = "With size:",
                        Margin = new Thickness(0, 0, 5, 0)
                    };

                    var sizeTextBox = new TextBox
                    {
                        Text = "0",
                        Width = 50,
                        IsEnabled = false,
                        Margin = new Thickness(0, 0, 10, 0)
                    };

                    sizeRadio.Checked += (s, e) => sizeTextBox.IsEnabled = true;
                    emptyRadio.Checked += (s, e) => sizeTextBox.IsEnabled = false;

                    optionsPanel.Children.Add(emptyRadio);
                    optionsPanel.Children.Add(sizeRadio);
                    optionsPanel.Children.Add(sizeTextBox);

                    stackPanel.Children.Add(optionsPanel);
                    stackPanel.Tag = new ArrayParameterInfo { ElementType = elementType, SizeTextBox = sizeTextBox, EmptyRadio = emptyRadio };

                    return stackPanel;
                }
            }
            // Handle simple primitive types
            else if (paramType == typeof(string))
            {
                var textBox = new TextBox { Text = parameter.Name.Contains("file") ? "filelist.txt" : "" };
                return textBox;
            }
            else if (paramType == typeof(bool))
            {
                var checkBox = new CheckBox { IsChecked = false };
                return checkBox;
            }
            else if (paramType.IsEnum)
            {
                var comboBox = new ComboBox
                {
                    ItemsSource = Enum.GetValues(paramType),
                    SelectedIndex = 0
                };
                return comboBox;
            }
            else if (paramType.IsPrimitive || paramType == typeof(decimal))
            {
                var textBox = new TextBox { Text = GetDefaultPrimitiveValue(paramType, parameter.Name) };
                return textBox;
            }
            // Handle complex object types
            else if (paramType.IsClass)
            {
                return CreateComplexParameterEditor(parameter);
            }

            // Default for unknown types
            var defaultTextBox = new TextBox { Text = "0" };
            return defaultTextBox;
        }

        // Helper class to store array parameter info
        private class ArrayParameterInfo
        {
            public Type ElementType { get; set; }
            public TextBox SizeTextBox { get; set; }
            public RadioButton EmptyRadio { get; set; }
        }

        private string GetDefaultArrayValues(Type elementType, string paramName)
        {
            if (elementType == typeof(uint))
                return "0, 1, 2, 3";
            else if (elementType == typeof(int))
                return "0, 1, 2, 3";
            else if (elementType == typeof(string))
                return "item1, item2, item3";
            else
                return "";
        }

        private string GetDefaultPrimitiveValue(Type type, string paramName)
        {
            if (paramName != null)
                paramName = paramName.ToLower();

            if (type == typeof(uint))
                return GetDefaultUintValue(paramName);
            else if (type == typeof(byte))
                return GetDefaultByteValue(paramName);
            else if (type == typeof(ulong))
                return "0";
            else if (type == typeof(int))
                return "0";
            else if (type == typeof(long))
                return "0";
            else if (type == typeof(short))
                return "0";
            else if (type == typeof(ushort))
                return "0";
            else if (type == typeof(float))
                return "0.0";
            else if (type == typeof(double))
                return "0.0";
            else if (type == typeof(decimal))
                return "0.0";
            else
                return "0";
        }

        private FrameworkElement CreateComplexParameterEditor(ParameterInfo parameter)
        {
            var paramType = parameter.ParameterType;
            var mainPanel = new StackPanel();

            // Store the parameter info so we know what type to expect
            mainPanel.Tag = new ComplexParameterInfo { Parameter = parameter, Instance = null };

            mainPanel.Children.Add(new TextBlock
            {
                Text = $"Parameter: {parameter.Name} ({paramType.Name})",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            });

            // For interfaces/abstract classes, show type selector
            if (paramType.IsInterface || paramType.IsAbstract)
            {
                var compatibleTypes = TypeHelper.GetDerivedTypes(paramType)
                    .Where(t => !t.IsAbstract && !t.IsInterface)
                    .OrderBy(t => t.Name)
                    .ToList();

                if (compatibleTypes.Count > 0)
                {
                    var typePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };

                    typePanel.Children.Add(new TextBlock
                    {
                        Text = "Type: ",
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    var typeCombo = new ComboBox
                    {
                        ItemsSource = compatibleTypes,
                        DisplayMemberPath = "Name",
                        SelectedIndex = 0,
                        MinWidth = 150
                    };
                    typePanel.Children.Add(typeCombo);

                    var createButton = new Button
                    {
                        Content = "Create",
                        Margin = new Thickness(5, 0, 0, 0),
                        Padding = new Thickness(10, 2, 10, 2)
                    };

                    var fieldsPanel = new StackPanel
                    {
                        Margin = new Thickness(10, 5, 0, 0),
                        Visibility = Visibility.Collapsed
                    };

                    createButton.Click += (s, e) =>
                    {
                        if (typeCombo.SelectedItem is Type selectedType)
                        {
                            try
                            {
                                var instance = Activator.CreateInstance(selectedType);

                                // Show fields inline
                                fieldsPanel.Children.Clear();
                                LoadParameterFields(fieldsPanel, instance);
                                fieldsPanel.Visibility = Visibility.Visible;

                                createButton.Content = "Recreate";

                                // Update the stored instance
                                var info = mainPanel.Tag as ComplexParameterInfo;
                                if (info != null)
                                {
                                    info.Instance = instance;
                                }
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Error creating {selectedType.Name}: {ex.Message}",
                                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    };

                    typePanel.Children.Add(createButton);
                    mainPanel.Children.Add(typePanel);
                    mainPanel.Children.Add(fieldsPanel);
                }
            }
            else
            {
                // For concrete classes
                var createButton = new Button
                {
                    Content = $"Create {paramType.Name}",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 5, 10, 5)
                };

                var fieldsPanel = new StackPanel
                {
                    Margin = new Thickness(10, 5, 0, 0),
                    Visibility = Visibility.Collapsed
                };

                createButton.Click += (s, e) =>
                {
                    try
                    {
                        var instance = Activator.CreateInstance(paramType);

                        // Show fields inline
                        fieldsPanel.Children.Clear();
                        LoadParameterFields(fieldsPanel, instance);
                        fieldsPanel.Visibility = Visibility.Visible;

                        createButton.Content = "Recreate";

                        // Update the stored instance
                        var info = mainPanel.Tag as ComplexParameterInfo;
                        if (info != null)
                        {
                            info.Instance = instance;
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error creating {paramType.Name}: {ex.Message}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                mainPanel.Children.Add(createButton);
                mainPanel.Children.Add(fieldsPanel);
            }

            return mainPanel;
        }

        private class ComplexParameterInfo
        {
            public ParameterInfo Parameter { get; set; }
            public object Instance { get; set; }
        }

        private void LoadParameterFields(StackPanel container, object instance)
        {
            if (instance == null) return;

            var type = instance.GetType();
            var controlFactory = new ControlFactory();

            // Properties
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite)
                .OrderBy(p => p.Name);

            foreach (var prop in properties)
            {
                try
                {
                    var value = prop.GetValue(instance);
                    var editor = CreateFieldEditorForParameter(prop.Name, prop.PropertyType, value,
                        (newValue) => prop.SetValue(instance, newValue));
                    container.Children.Add(editor);
                }
                catch { }
            }

            // Fields
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(f => f.Name);

            foreach (var field in fields)
            {
                try
                {
                    var value = field.GetValue(instance);
                    var editor = CreateFieldEditorForParameter(field.Name, field.FieldType, value,
                        (newValue) => field.SetValue(instance, newValue));
                    container.Children.Add(editor);
                }
                catch { }
            }
        }

        private FrameworkElement CreateFieldEditorForParameter(string fieldName, Type fieldType,
    object value, Action<object> onValueChanged)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = fieldName + ":",
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            FrameworkElement editor = null;

            // Handle byte arrays specially - use ByteArrayEditDialog
            if (fieldType == typeof(byte[]))
            {
                var editorPanel = new Grid();
                editorPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                editorPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var textBox = new TextBox
                {
                    Text = value != null ? $"[{((byte[])value).Length} bytes]" : "[null]",
                    IsReadOnly = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0)
                };
                Grid.SetColumn(textBox, 0);

                var editButton = new Button
                {
                    Content = "Edit",
                    Padding = new Thickness(10, 2, 10, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(editButton, 1);

                editButton.Click += (s, e) =>
                {
                    var dialog = new ByteArrayEditDialog(value as byte[] ?? new byte[0]);
                    if (dialog.ShowDialog() == true)
                    {
                        var newBytes = dialog.ByteArray;
                        onValueChanged(newBytes);
                        textBox.Text = $"[{newBytes.Length} bytes]";
                        value = newBytes;
                    }
                };

                editorPanel.Children.Add(textBox);
                editorPanel.Children.Add(editButton);
                editor = editorPanel;
            }
            // Handle other arrays (not byte[])
            else if (fieldType.IsArray)
            {
                var elementType = fieldType.GetElementType();
                var editorPanel = new Grid();
                editorPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                editorPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var textBox = new TextBox
                {
                    Text = value != null ? $"[{(value as Array).Length} {elementType.Name}s]" : "[null]",
                    IsReadOnly = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0)
                };
                Grid.SetColumn(textBox, 0);

                var editButton = new Button
                {
                    Content = "Edit",
                    Padding = new Thickness(10, 2, 10, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(editButton, 1);

                editButton.Click += (s, e) =>
                {
                    var dialog = new ArrayEditDialog(fieldType, value as Array);
                    if (dialog.ShowDialog() == true)
                    {
                        onValueChanged(dialog.ResultArray);
                        textBox.Text = $"[{dialog.ResultArray.Length} {elementType.Name}s]";
                        value = dialog.ResultArray; // Update local reference
                    }
                };

                editorPanel.Children.Add(textBox);
                editorPanel.Children.Add(editButton);
                editor = editorPanel;
            }
            // Handle strings
            else if (fieldType == typeof(string))
            {
                var textBox = new TextBox
                {
                    Text = value?.ToString() ?? "",
                    VerticalAlignment = VerticalAlignment.Center
                };
                textBox.TextChanged += (s, e) => onValueChanged(textBox.Text);
                editor = textBox;
            }
            // Handle booleans
            else if (fieldType == typeof(bool))
            {
                var checkBox = new CheckBox
                {
                    IsChecked = (bool)(value ?? false),
                    VerticalAlignment = VerticalAlignment.Center
                };
                checkBox.Checked += (s, e) => onValueChanged(true);
                checkBox.Unchecked += (s, e) => onValueChanged(false);
                editor = checkBox;
            }
            // Handle enums
            else if (fieldType.IsEnum)
            {
                var comboBox = new ComboBox
                {
                    ItemsSource = Enum.GetValues(fieldType),
                    SelectedItem = value ?? Enum.GetValues(fieldType).GetValue(0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                comboBox.SelectionChanged += (s, e) =>
                {
                    if (comboBox.SelectedItem != null)
                        onValueChanged(comboBox.SelectedItem);
                };
                editor = comboBox;
            }
            // Handle primitives
            else if (fieldType.IsPrimitive || fieldType == typeof(decimal))
            {
                var textBox = new TextBox
                {
                    Text = value?.ToString() ?? (fieldType == typeof(string) ? "" : "0"),
                    VerticalAlignment = VerticalAlignment.Center
                };

                textBox.TextChanged += (s, e) =>
                {
                    try
                    {
                        if (fieldType == typeof(string))
                        {
                            onValueChanged(textBox.Text);
                        }
                        else
                        {
                            var convertedValue = Convert.ChangeType(textBox.Text, fieldType);
                            onValueChanged(convertedValue);
                        }
                    }
                    catch { }
                };
                editor = textBox;
            }
            // Handle collections
            else if (fieldType.IsGenericType)
            {
                var genericDef = fieldType.GetGenericTypeDefinition();
                if (genericDef == typeof(List<>) || genericDef == typeof(Dictionary<,>) || genericDef == typeof(HashSet<>))
                {
                    var editorPanel = new Grid();
                    editorPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    editorPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var textBox = new TextBox
                    {
                        Text = value != null ? $"[{fieldType.Name}]" : "[null]",
                        IsReadOnly = true,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 5, 0)
                    };
                    Grid.SetColumn(textBox, 0);

                    var createButton = new Button
                    {
                        Content = value == null ? "Create" : "Edit",
                        Padding = new Thickness(10, 2, 10, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(createButton, 1);

                    createButton.Click += (s, e) =>
                    {
                        if (value == null)
                        {
                            try
                            {
                                var newInstance = Activator.CreateInstance(fieldType);
                                onValueChanged(newInstance);
                                value = newInstance;
                                textBox.Text = $"[{fieldType.Name}]";
                                createButton.Content = "Edit";
                            }
                            catch
                            {
                                MessageBox.Show($"Cannot create instance of {fieldType.Name}", "Error",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        else
                        {
                            // TODO: Show edit dialog for collections
                            MessageBox.Show("Collection editing not yet implemented", "Info",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    };

                    editorPanel.Children.Add(textBox);
                    editorPanel.Children.Add(createButton);
                    editor = editorPanel;
                }
                else
                {
                    editor = new TextBlock
                    {
                        Text = value?.ToString() ?? "[null]",
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
            }
            // Handle other complex types
            else if (fieldType.IsClass)
            {
                var editorPanel = new Grid();
                editorPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                editorPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var textBox = new TextBox
                {
                    Text = value != null ? $"[{fieldType.Name}]" : "[null]",
                    IsReadOnly = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0)
                };
                Grid.SetColumn(textBox, 0);

                var createButton = new Button
                {
                    Content = value == null ? "Create" : "Edit",
                    Padding = new Thickness(10, 2, 10, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(createButton, 1);

                createButton.Click += (s, e) =>
                {
                    if (value == null)
                    {
                        try
                        {
                            var newInstance = Activator.CreateInstance(fieldType);
                            onValueChanged(newInstance);
                            value = newInstance;
                            textBox.Text = $"[{fieldType.Name}]";
                            createButton.Content = "Edit";
                        }
                        catch
                        {
                            MessageBox.Show($"Cannot create instance of {fieldType.Name}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        // TODO: Show edit dialog for complex type
                        MessageBox.Show("Complex type editing not yet implemented", "Info",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                };

                editorPanel.Children.Add(textBox);
                editorPanel.Children.Add(createButton);
                editor = editorPanel;
            }
            else
            {
                editor = new TextBlock
                {
                    Text = value?.ToString() ?? "[null]",
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            if (editor != null)
            {
                Grid.SetColumn(editor, 1);
                grid.Children.Add(editor);
            }

            return grid;
        }

        private bool HasUsableConstructor(Type type)
        {
            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            
            // Has parameterless constructor
            if (constructors.Any(c => c.GetParameters().Length == 0))
                return true;
            
            // Has constructor with reasonable parameter count (5 or less)
            return constructors.Any(c => c.GetParameters().Length <= 5);
        }

        private string GetDefaultUintValue(string paramName)
        {
            if (paramName != null)
            {
                paramName = paramName.ToLower();
                if (paramName.Contains("key")) return "0";
                if (paramName.Contains("seed")) return "0";
                if (paramName.Contains("xor")) return "0x00";
            }
            return "0";
        }

        private string GetDefaultByteValue(string paramName)
        {
            if (paramName != null)
            {
                paramName = paramName.ToLower();
                if (paramName.Contains("key")) return "0";
                if (paramName.Contains("xor")) return "0";
            }
            return "0";
        }

        private string GetDefaultByteArrayValue(string paramName)
        {
            paramName = paramName.ToLower();
            if (paramName.Contains("key")) return "00";
            return "00";
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (selectedConstructor == null)
                {
                    MessageBox.Show("No constructor selected.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var parameters = selectedConstructor.GetParameters();
                var paramValues = new object[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    var editor = parameterEditors[i];

                    if (editor is TextBox textBox)
                    {
                        paramValues[i] = ConvertTextBoxValue(textBox.Text, paramType);
                    }
                    else if (editor is CheckBox checkBox)
                    {
                        paramValues[i] = checkBox.IsChecked ?? false;
                    }
                    else if (editor is ComboBox comboBox)
                    {
                        paramValues[i] = comboBox.SelectedItem;
                    }
                    else if (editor is StackPanel stackPanel)
                    {
                        // Check if this is an array parameter with special handling
                        if (stackPanel.Tag is ArrayParameterInfo arrayInfo)
                        {
                            if (arrayInfo.EmptyRadio.IsChecked == true)
                            {
                                paramValues[i] = Array.CreateInstance(arrayInfo.ElementType, 0);
                            }
                            else
                            {
                                int size = int.Parse(arrayInfo.SizeTextBox.Text);
                                paramValues[i] = Array.CreateInstance(arrayInfo.ElementType, size);
                            }
                        }
                        // Check if this is a complex parameter editor
                        else if (stackPanel.Tag is ComplexParameterInfo complexInfo)
                        {
                            paramValues[i] = complexInfo.Instance;
                        }
                        else
                        {
                            var innerTextBox = stackPanel.Children.OfType<TextBox>().FirstOrDefault();
                            if (innerTextBox != null)
                            {
                                paramValues[i] = ConvertTextBoxValue(innerTextBox.Text, paramType);
                            }
                            else
                            {
                                paramValues[i] = null;
                            }
                        }
                    }
                }

                CreatedInstance = selectedConstructor.Invoke(paramValues);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating instance: {ex.Message}\n\nDetails: {ex.InnerException?.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private object HandleComplexParameterValue(StackPanel stackPanel, Type paramType)
        {
            // Find the radio buttons to determine if null or create was selected
            var radioButtons = stackPanel.Children.OfType<StackPanel>()
                .SelectMany(sp => sp.Children.OfType<RadioButton>())
                .ToList();
        
            var nullRadio = radioButtons.FirstOrDefault(rb => rb.Tag?.ToString() == "null");
            var createRadio = radioButtons.FirstOrDefault(rb => rb.Tag?.ToString() == "create");
        
            if (nullRadio?.IsChecked == true)
            {
                return null;
            }
            else if (createRadio?.IsChecked == true)
            {
                try
                {
                    if (paramType.IsInterface || paramType.IsAbstract)
                    {
                        // Find the type selector ComboBox
                        var typeCombo = stackPanel.Children.OfType<StackPanel>()
                            .Skip(1) // Skip the radio button panel
                            .SelectMany(sp => sp.Children.OfType<ComboBox>())
                            .FirstOrDefault(cb => cb.Tag?.ToString() == "typeSelector");
        
                        if (typeCombo?.SelectedItem is Type selectedType)
                        {
                            return Activator.CreateInstance(selectedType);
                        }
                    }
                    else
                    {
                        // For concrete classes, try to create directly
                        return Activator.CreateInstance(paramType);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error creating {paramType.Name}: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        
            return null;
        }

        private object ConvertTextBoxValue(string text, Type targetType)
        {
            if (targetType == typeof(byte[]))
            {
                if (string.IsNullOrWhiteSpace(text))
                    return new byte[0];

                // Simple hex parsing for parameter dialog
                text = System.Text.RegularExpressions.Regex.Replace(text, @"[^0-9A-Fa-f]", " ");
                var hexValues = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                var byteList = new List<byte>();
                foreach (var hex in hexValues)
                {
                    for (int i = 0; i < hex.Length; i += 2)
                    {
                        var hexPair = hex.Substring(i, Math.Min(2, hex.Length - i));
                        if (hexPair.Length == 1)
                            hexPair = "0" + hexPair;
                        if (hexPair.Length == 2)
                            byteList.Add(Convert.ToByte(hexPair, 16));
                    }
                }

                return byteList.ToArray();
            }

            if (targetType.IsArray)
            {
                var elementType = targetType.GetElementType();

                if (string.IsNullOrWhiteSpace(text))
                {
                    return Array.CreateInstance(elementType, 0);
                }
                else if (elementType.IsPrimitive || elementType == typeof(string) || elementType == typeof(decimal))
                {
                    // Parse comma-separated values
                    var values = text.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
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
                    return array;
                }
            }

            // Handle non-array types
            if (string.IsNullOrWhiteSpace(text) && targetType != typeof(string))
            {
                if (targetType.IsValueType)
                    return Activator.CreateInstance(targetType);
                return null;
            }

            if (targetType == typeof(string))
                return text;
            else if (targetType == typeof(uint))
                return Convert.ToUInt32(text.StartsWith("0x") ? text.Substring(2) : text,
                    text.StartsWith("0x") ? 16 : 10);
            else if (targetType == typeof(byte))
                return Convert.ToByte(text, text.StartsWith("0x") ? 16 : 10);
            else if (targetType == typeof(ulong))
                return Convert.ToUInt64(text.StartsWith("0x") ? text.Substring(2) : text,
                    text.StartsWith("0x") ? 16 : 10);
            else if (targetType == typeof(int))
                return Convert.ToInt32(text);
            else if (targetType == typeof(long))
                return Convert.ToInt64(text);
            else if (targetType == typeof(short))
                return Convert.ToInt16(text);
            else if (targetType == typeof(ushort))
                return Convert.ToUInt16(text);
            else if (targetType == typeof(float))
                return Convert.ToSingle(text);
            else if (targetType == typeof(double))
                return Convert.ToDouble(text);
            else if (targetType == typeof(decimal))
                return Convert.ToDecimal(text);

            return Convert.ChangeType(text, targetType);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}