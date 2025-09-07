// FieldEditor.ComplexTypes.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Reflection;
using System.Windows.Media;
using GameRes;

namespace SchemeEditor
{
    public partial class FieldEditor
    {
        private FrameworkElement CreateInterfaceEditor(string fieldName, Type interfaceType,
            object value, Dictionary<string, object> fieldValues, Border container, int indentLevel)
        {
            var stackPanel = new StackPanel();
            var headerText = new TextBlock
            {
                Text = $"{(interfaceType.IsInterface ? "Interface" : "Abstract Class")}: {interfaceType.Name}",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            };
            stackPanel.Children.Add(headerText);

            var compatibleTypes = TypeHelper.GetDerivedTypes(interfaceType)
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .OrderBy(t => t.Name)
                .ToList();

            if (compatibleTypes.Count == 0)
            {
                stackPanel.Children.Add(new TextBlock
                {
                    Text = "No compatible implementations found",
                    FontStyle = FontStyles.Italic,
                    Foreground = Brushes.Gray
                });
                return stackPanel;
            }

            var typeSelectionPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            typeSelectionPanel.Children.Add(new TextBlock
            {
                Text = "Implementation: ",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });
            var typeCombo = new ComboBox
            {
                ItemsSource = compatibleTypes,
                DisplayMemberPath = "Name",
                MinWidth = 200,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (value != null)
                typeCombo.SelectedItem = value.GetType();
            else if (compatibleTypes.Count > 0)
                typeCombo.SelectedIndex = 0;

            typeSelectionPanel.Children.Add(typeCombo);

            var createButton = new Button
            {
                Content = "Create Instance",
                Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(10, 5, 10, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
            typeSelectionPanel.Children.Add(createButton);

            stackPanel.Children.Add(typeSelectionPanel);

            var statusPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var statusText = new TextBlock
            {
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 5, 0, 0)
            };

            var currentInstancePanel = new Border
            {
                BorderBrush = Brushes.LightBlue,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 5, 0, 0),
                Visibility = Visibility.Collapsed
            };

            var instanceDetailsPanel = new StackPanel();
            currentInstancePanel.Child = instanceDetailsPanel;

            statusPanel.Children.Add(statusText);
            statusPanel.Children.Add(currentInstancePanel);
            stackPanel.Children.Add(statusPanel);

            void UpdateDisplay()
            {
                if (value == null)
                {
                    statusText.Text = "No instance created (null)";
                    statusText.Foreground = Brushes.Gray;
                    currentInstancePanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    statusText.Text = $"Current instance: {value.GetType().Name}";
                    statusText.Foreground = Brushes.Green;

                    instanceDetailsPanel.Children.Clear();

                    var typeInfoText = new TextBlock
                    {
                        Text = $"Type: {value.GetType().FullName}",
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 5)
                    };
                    instanceDetailsPanel.Children.Add(typeInfoText);

                    var properties = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.CanRead && (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string)))
                        .ToList();

                    if (properties.Any())
                    {
                        var propsText = new TextBlock
                        {
                            Text = "Key properties:",
                            FontWeight = FontWeights.SemiBold,
                            Margin = new Thickness(0, 5, 0, 2)
                        };
                        instanceDetailsPanel.Children.Add(propsText);

                        foreach (var prop in properties)
                        {
                            try
                            {
                                var propValue = prop.GetValue(value);
                                var propText = new TextBlock
                                {
                                    Text = $"  {prop.Name}: {propValue?.ToString() ?? "null"}",
                                    Margin = new Thickness(10, 0, 0, 0),
                                    FontFamily = new FontFamily("Consolas")
                                };
                                instanceDetailsPanel.Children.Add(propText);
                            }
                            catch { }
                        }
                    }

                    var editButton = new Button
                    {
                        Content = "Edit Properties",
                        Margin = new Thickness(0, 10, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(10, 5, 10, 5)
                    };
                    editButton.Click += (s, e) =>
                    {
                        var editWindow = new Window
                        {
                            Title = $"Edit {value.GetType().Name}",
                            Width = 600,
                            Height = 400,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            Owner = Window.GetWindow(container)
                        };

                        var scrollViewer = new ScrollViewer
                        {
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            Margin = new Thickness(10)
                        };

                        var editPanel = new StackPanel();
                        var fieldLoader = new FieldLoader(mainWindow);
                        var editFieldValues = new Dictionary<string, object>();
                        var editOriginalValues = new Dictionary<FrameworkElement, object>();

                        fieldLoader.LoadComplexTypeFields(editPanel, fieldName, value, 0, editFieldValues, editOriginalValues, container);

                        scrollViewer.Content = editPanel;
                        editWindow.Content = scrollViewer;

                        editWindow.ShowDialog();

                        UpdateDisplay();
                        MarkFieldAsEdited(container);
                    };

                    instanceDetailsPanel.Children.Add(editButton);
                    currentInstancePanel.Visibility = Visibility.Visible;
                }
            }

            createButton.Click += (s, e) =>
            {
                if (typeCombo.SelectedItem is Type selectedType)
                {
                    try
                    {
                        var newInstance = Activator.CreateInstance(selectedType);
                        value = newInstance;
                        fieldValues[fieldName] = newInstance;
                        MarkFieldAsEdited(container);

                        UpdateDisplay();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error creating instance of {selectedType.Name}: {ex.Message}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            };

            typeCombo.SelectionChanged += (s, e) =>
            {
                createButton.IsEnabled = typeCombo.SelectedItem != null;
                if (typeCombo.SelectedItem is Type selectedType)
                {
                    createButton.Content = $"Create {selectedType.Name}";
                }
            };

            UpdateDisplay();

            return stackPanel;
        }

        private FrameworkElement CreateComplexTypeEditor(string fieldName, Type type,
            object value, int indentLevel, Border container)
        {
            var mainPanel = new StackPanel();

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };

            var typeText = new TextBlock
            {
                Text = $"{type.Name}",
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerPanel.Children.Add(typeText);

            if (value != null)
            {
                var statusText = new TextBlock
                {
                    Text = " [Instance]",
                    FontStyle = FontStyles.Italic,
                    Foreground = Brushes.Green,
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerPanel.Children.Add(statusText);
            }

            mainPanel.Children.Add(headerPanel);

            var fieldsPanel = new StackPanel
            {
                Margin = new Thickness(10, 5, 0, 0)
            };

            if (value != null)
            {
                var fieldLoader = new FieldLoader(mainWindow);
                var fieldValues = mainWindow.GetType()
                    .GetField("currentFieldValues", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(mainWindow) as Dictionary<string, object>;
                var originalValues = mainWindow.GetType()
                    .GetField("originalValues", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(mainWindow) as Dictionary<FrameworkElement, object>;

                if (fieldValues != null && !string.IsNullOrEmpty(fieldName))
                    fieldValues[fieldName] = value;

                fieldLoader.LoadComplexTypeFields(fieldsPanel, fieldName, value,
                    indentLevel + 1, fieldValues, originalValues, container);
            }
            else
            {
                var createButton = new Button
                {
                    Content = "Create Instance",
                    Margin = new Thickness(0, 5, 0, 5),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 5, 10, 5)
                };

                createButton.Click += (s, e) =>
                {
                    var instance = CreateInstance(type);
                    if (instance != null)
                    {
                        var fieldValues = mainWindow.GetType()
                            .GetField("currentFieldValues", BindingFlags.NonPublic | BindingFlags.Instance)
                            ?.GetValue(mainWindow) as Dictionary<string, object>;
                        fieldValues[fieldName] = instance;

                        fieldsPanel.Children.Clear();
                        var fieldLoader = new FieldLoader(mainWindow);
                        var originalValues = mainWindow.GetType()
                            .GetField("originalValues", BindingFlags.NonPublic | BindingFlags.Instance)
                            ?.GetValue(mainWindow) as Dictionary<FrameworkElement, object>;
                        fieldLoader.LoadComplexTypeFields(fieldsPanel, fieldName, instance,
                            indentLevel + 1, fieldValues, originalValues, container);

                        headerPanel.Children.Add(new TextBlock
                        {
                            Text = " [Instance]",
                            FontStyle = FontStyles.Italic,
                            Foreground = Brushes.DarkGreen,
                            VerticalAlignment = VerticalAlignment.Center
                        });

                        createButton.Visibility = Visibility.Collapsed;

                        MarkFieldAsEdited(container);
                    }
                };

                fieldsPanel.Children.Add(createButton);
            }

            mainPanel.Children.Add(fieldsPanel);
            return mainPanel;
        }

        private FrameworkElement CreateNullEditor(string fieldName, Type fieldType,
            Dictionary<string, object> fieldValues, Dictionary<FrameworkElement, object> originalValues,
            int indentLevel, Border container = null)
        {
            if (fieldType.IsInterface || fieldType.IsAbstract)
            {
                var interfacePanel = new StackPanel();

                var nullText = new TextBlock
                {
                    Text = $"[null {(fieldType.IsInterface ? "interface" : "abstract class")}]",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 10),
                    FontStyle = FontStyles.Italic
                };
                interfacePanel.Children.Add(nullText);

                var compatibleTypes = TypeHelper.GetDerivedTypes(fieldType)
                    .Where(t => !t.IsAbstract && !t.IsInterface && HasUsableConstructor(t))
                    .OrderBy(t => t.Name)
                    .ToList();

                if (compatibleTypes.Count > 0)
                {
                    var selectPanel = new StackPanel { Orientation = Orientation.Horizontal };

                    selectPanel.Children.Add(new TextBlock
                    {
                        Text = "Select type: ",
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 5, 0)
                    });

                    var typeCombo = new ComboBox
                    {
                        ItemsSource = compatibleTypes,
                        DisplayMemberPath = "Name",
                        MinWidth = 150,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var createButton = new Button
                    {
                        Content = "Create",
                        Margin = new Thickness(5, 0, 0, 0),
                        Padding = new Thickness(10, 2, 10, 2)
                    };

                    createButton.Click += (s, e) =>
                    {
                        if (typeCombo.SelectedItem is Type selectedType)
                        {
                            try
                            {
                                object newInstance = null;

                                var parameterlessConstructor = selectedType.GetConstructor(Type.EmptyTypes);
                                if (parameterlessConstructor != null)
                                {
                                    newInstance = Activator.CreateInstance(selectedType);
                                }
                                else
                                {
                                    var paramDialog = new ConstructorParametersDialog(selectedType);
                                    if (paramDialog.ShowDialog() == true)
                                    {
                                        newInstance = paramDialog.CreatedInstance;
                                    }
                                }

                                if (newInstance != null)
                                {
                                    fieldValues[fieldName] = newInstance;

                                    if (container != null)
                                        MarkFieldAsEdited(container);

                                    if (mainWindow.GetType().GetField("selectedScheme",
                                        BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(mainWindow)
                                        is KeyValuePair<string, ResourceScheme> scheme && scheme.Value != null)
                                    {
                                        var loadMethod = mainWindow.GetType().GetMethod("LoadSchemeFields",
                                            BindingFlags.NonPublic | BindingFlags.Instance);
                                        loadMethod?.Invoke(mainWindow, new object[] { scheme.Value });
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Error creating instance: {ex.Message}", "Error",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    };

                    selectPanel.Children.Add(typeCombo);
                    selectPanel.Children.Add(createButton);
                    interfacePanel.Children.Add(selectPanel);
                }
                else
                {
                    interfacePanel.Children.Add(new TextBlock
                    {
                        Text = "No compatible implementations found",
                        FontStyle = FontStyles.Italic,
                        Foreground = Brushes.Gray
                    });
                }

                return interfacePanel;
            }

            if (fieldType.IsClass && fieldType != typeof(string) && fieldType != typeof(object))
            {
                var classPanel = new StackPanel();

                var nullText = new TextBlock
                {
                    Text = $"[null {fieldType.Name}]",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 10),
                    FontStyle = FontStyles.Italic
                };
                classPanel.Children.Add(nullText);

                var createPanel = new StackPanel { Orientation = Orientation.Horizontal };

                var createButton = new Button
                {
                    Content = $"Create {fieldType.Name}",
                    Padding = new Thickness(10, 5, 10, 5)
                };

                createButton.Click += (s, e) =>
                {
                    try
                    {
                        object newInstance = null;

                        var parameterlessConstructor = fieldType.GetConstructor(Type.EmptyTypes);
                        if (parameterlessConstructor != null)
                        {
                            newInstance = Activator.CreateInstance(fieldType);
                        }
                        else
                        {
                            var paramDialog = new ConstructorParametersDialog(fieldType);
                            if (paramDialog.ShowDialog() == true)
                            {
                                newInstance = paramDialog.CreatedInstance;
                            }
                        }

                        if (newInstance != null)
                        {
                            fieldValues[fieldName] = newInstance;

                            if (container != null)
                                MarkFieldAsEdited(container);

                            if (mainWindow.GetType().GetField("selectedScheme",
                                BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(mainWindow)
                                is KeyValuePair<string, ResourceScheme> scheme && scheme.Value != null)
                            {
                                var loadMethod = mainWindow.GetType().GetMethod("LoadSchemeFields",
                                    BindingFlags.NonPublic | BindingFlags.Instance);
                                loadMethod?.Invoke(mainWindow, new object[] { scheme.Value });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error creating {fieldType.Name}: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                createPanel.Children.Add(createButton);
                classPanel.Children.Add(createPanel);

                return classPanel;
            }

            return new TextBlock
            {
                Text = "[null]",
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private bool HasUsableConstructor(Type type)
        {
            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

            if (constructors.Any(c => c.GetParameters().Length == 0))
                return true;

            return constructors.Any();
        }

        private object CreateInstance(Type type)
        {
            if (type.IsInterface && type.IsGenericType)
            {
                var genericDef = type.GetGenericTypeDefinition();
                var genericArgs = type.GetGenericArguments();

                if (genericDef == typeof(IList<>))
                {
                    var listType = typeof(List<>).MakeGenericType(genericArgs);
                    return Activator.CreateInstance(listType);
                }
                else if (genericDef == typeof(IDictionary<,>))
                {
                    var dictType = typeof(Dictionary<,>).MakeGenericType(genericArgs);
                    return Activator.CreateInstance(dictType);
                }
                else if (genericDef == typeof(ISet<>))
                {
                    var setType = typeof(HashSet<>).MakeGenericType(genericArgs);
                    return Activator.CreateInstance(setType);
                }
            }

            if (type.IsAbstract || type.IsInterface)
            {
                var types = TypeHelper.GetDerivedTypes(type);
                if (types.Length > 0)
                {
                    var dialog = new SelectTypeDialog(types);
                    if (dialog.ShowDialog() == true && dialog.SelectedType != null)
                    {
                        return Activator.CreateInstance(dialog.SelectedType);
                    }
                }
                return null;
            }

            return Activator.CreateInstance(type);
        }

        public FrameworkElement CreateArrayEditor(string fieldName, Type arrayType, object value, int indentLevel, Border container)
        {
            var elementType = arrayType.GetElementType();
            var array = value as Array;

            var expander = new Expander
            {
                Header = $"Array [{array?.Length ?? 0} items]"
            };

            var stackPanel = new StackPanel();
            var itemsPanel = new StackPanel();

            if (array != null)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    var index = i;
                    var itemPanel = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    itemPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                    itemPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var indexLabel = new TextBlock
                    {
                        Text = $"[{index}]",
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 5, 0)
                    };
                    Grid.SetColumn(indexLabel, 0);
                    itemPanel.Children.Add(indexLabel);

                    var itemValue = array.GetValue(index);
                    FrameworkElement valueEditor = null;

                    if (elementType == typeof(string))
                    {
                        valueEditor = controlFactory.CreateTextBox(itemValue?.ToString() ?? "", (text) =>
                        {
                            try
                            {
                                array.SetValue(text, index);
                                var fieldValues = mainWindow.GetType()
                                    .GetField("currentFieldValues", BindingFlags.NonPublic | BindingFlags.Instance)
                                    ?.GetValue(mainWindow) as Dictionary<string, object>;
                                if (fieldValues != null)
                                    fieldValues[fieldName] = array;
                                MarkFieldAsEdited(container);
                            }
                            catch { }
                        });
                    }
                    else if (elementType.IsPrimitive || elementType == typeof(decimal))
                    {
                        valueEditor = controlFactory.CreateNumericTextBox(elementType, itemValue, (newValue) =>
                        {
                            try
                            {
                                array.SetValue(newValue, index);
                                var fieldValues = mainWindow.GetType()
                                    .GetField("currentFieldValues", BindingFlags.NonPublic | BindingFlags.Instance)
                                    ?.GetValue(mainWindow) as Dictionary<string, object>;
                                if (fieldValues != null)
                                    fieldValues[fieldName] = array;
                                MarkFieldAsEdited(container);
                            }
                            catch { }
                        });
                    }
                    else if (elementType.IsEnum)
                    {
                        valueEditor = controlFactory.CreateEnumComboBox(elementType, itemValue, (selected) =>
                        {
                            try
                            {
                                array.SetValue(selected, index);
                                var fieldValues = mainWindow.GetType()
                                    .GetField("currentFieldValues", BindingFlags.NonPublic | BindingFlags.Instance)
                                    ?.GetValue(mainWindow) as Dictionary<string, object>;
                                if (fieldValues != null)
                                    fieldValues[fieldName] = array;
                                MarkFieldAsEdited(container);
                            }
                            catch { }
                        });
                    }
                    else
                    {
                        valueEditor = CreateComplexTypeEditor($"{fieldName}[{index}]", elementType, itemValue, indentLevel + 1, container);
                    }

                    Grid.SetColumn(valueEditor, 1);
                    itemPanel.Children.Add(valueEditor);

                    itemsPanel.Children.Add(itemPanel);
                }
            }

            stackPanel.Children.Add(itemsPanel);
            expander.Content = stackPanel;

            var fieldValues2 = mainWindow.GetType()
                .GetField("currentFieldValues", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainWindow) as Dictionary<string, object>;
            if (fieldValues2 != null)
                fieldValues2[fieldName] = array;

            return expander;
        }
    }
}