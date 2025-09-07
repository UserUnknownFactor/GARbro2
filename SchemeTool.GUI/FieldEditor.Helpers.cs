// FieldEditor.Helpers.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SchemeEditor
{
    public partial class FieldEditor
    {
        private object ConvertToType(string value, Type targetType)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
        
            try
            {
                if (targetType == typeof(string))
                    return value;
                
                if (targetType == typeof(bool))
                {
                    // Handle various bool representations
                    value = value.ToLower();
                    if (value == "true" || value == "1" || value == "yes" || value == "y")
                        return true;
                    if (value == "false" || value == "0" || value == "no" || value == "n")
                        return false;
                    return bool.Parse(value);
                }
                
                if (targetType == typeof(int))
                    return int.Parse(value);
                if (targetType == typeof(uint))
                    return uint.Parse(value);
                if (targetType == typeof(long))
                    return long.Parse(value);
                if (targetType == typeof(ulong))
                    return ulong.Parse(value);
                if (targetType == typeof(short))
                    return short.Parse(value);
                if (targetType == typeof(ushort))
                    return ushort.Parse(value);
                if (targetType == typeof(byte))
                    return byte.Parse(value);
                if (targetType == typeof(sbyte))
                    return sbyte.Parse(value);
                if (targetType == typeof(float))
                    return float.Parse(value);
                if (targetType == typeof(double))
                    return double.Parse(value);
                if (targetType == typeof(decimal))
                    return decimal.Parse(value);
                    
                // Fallback to Convert.ChangeType for other types
                return Convert.ChangeType(value, targetType);
            }
            catch
            {
                return null;
            }
        }

        private object DeepCloneValue(object source)
        {
            if (source == null) return null;

            var type = source.GetType();

            if (type.IsValueType || type == typeof(string))
                return source;

            if (type.IsArray)
            {
                var array = source as Array;
                var cloned = Array.CreateInstance(type.GetElementType(), array.Length);
                for (int i = 0; i < array.Length; i++)
                    cloned.SetValue(DeepCloneValue(array.GetValue(i)), i);
                return cloned;
            }

            if (source is System.Collections.IDictionary dict)
            {
                var dictType = source.GetType();
                System.Collections.IDictionary clonedDict;

                if (dictType.IsGenericType)
                    clonedDict = Activator.CreateInstance(dictType) as System.Collections.IDictionary;
                else
                    clonedDict = new System.Collections.Hashtable();

                foreach (System.Collections.DictionaryEntry entry in dict)
                    clonedDict.Add(entry.Key, DeepCloneValue(entry.Value));
                return clonedDict;
            }

            if (source is System.Collections.IList list)
            {
                var listType = source.GetType();
                System.Collections.IList clonedList;

                if (listType.IsGenericType)
                    clonedList = Activator.CreateInstance(listType) as System.Collections.IList;
                else
                    clonedList = new System.Collections.ArrayList();

                foreach (var item in list)
                    clonedList.Add(DeepCloneValue(item));
                return clonedList;
            }

            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(source);
                return Newtonsoft.Json.JsonConvert.DeserializeObject(json, type);
            }
            catch
            {
                try
                {
                    var cloned = Activator.CreateInstance(type);

                    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (!field.IsInitOnly && !field.IsLiteral)
                            field.SetValue(cloned, DeepCloneValue(field.GetValue(source)));
                    }

                    foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
                            prop.SetValue(cloned, DeepCloneValue(prop.GetValue(source)));
                    }

                    return cloned;
                }
                catch
                {
                    return source;
                }
            }
        }

        private object CloneObject(object source)
        {
            if (source == null) return null;

            var type = source.GetType();

            if (type.IsValueType || type == typeof(string))
                return source;

            if (type.IsArray)
            {
                var array = source as Array;
                var cloned = Array.CreateInstance(type.GetElementType(), array.Length);
                Array.Copy(array, cloned, array.Length);
                return cloned;
            }

            try
            {
                var cloned = Activator.CreateInstance(type);

                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    field.SetValue(cloned, field.GetValue(source));

                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.CanRead && prop.CanWrite)
                        prop.SetValue(cloned, prop.GetValue(source));
                }

                return cloned;
            }
            catch
            {
                return Activator.CreateInstance(type);
            }
        }

        private FrameworkElement CreateListItemEditor(Type itemType, object itemValue, int index,
            System.Collections.IList workingList, string fieldName,
            Dictionary<string, object> fieldValues, int indentLevel, Border parentContainer)
        {
            Type actualType = itemType;
            if (itemType == typeof(object) && itemValue != null)
            {
                actualType = itemValue.GetType();
            }

            if (actualType == typeof(string))
            {
                return controlFactory.CreateTextBox(itemValue?.ToString() ?? "", (text) =>
                {
                    workingList[index] = text;
                    fieldValues[fieldName] = workingList;

                    if (parentContainer != null)
                        MarkFieldAsEdited(parentContainer);
                });
            }
            else if (actualType.IsPrimitive || actualType == typeof(decimal))
            {
                if (actualType == typeof(bool))
                {
                    return controlFactory.CreateCheckBox((bool)(itemValue ?? false), (isChecked) =>
                    {
                        workingList[index] = isChecked;
                        fieldValues[fieldName] = workingList;
                        if (parentContainer != null)
                        {
                            MarkFieldAsEdited(parentContainer);
                        }
                    });
                }
                else
                {
                    return controlFactory.CreateNumericTextBox(actualType, itemValue, (newValue) =>
                    {
                        workingList[index] = newValue;
                        fieldValues[fieldName] = workingList;
                        if (parentContainer != null)
                        {
                            MarkFieldAsEdited(parentContainer);
                        }
                    });
                }
            }
            else if (actualType.IsEnum)
            {
                return controlFactory.CreateEnumComboBox(actualType, itemValue, (selected) =>
                {
                    workingList[index] = selected;
                    fieldValues[fieldName] = workingList;
                    if (parentContainer != null)
                    {
                        MarkFieldAsEdited(parentContainer);
                    }
                });
            }
            else if (actualType == typeof(byte[]))
            {
                return CreateInlineByteArrayEditor($"item_{index}", itemValue as byte[],
                    (name, bytes) =>
                    {
                        workingList[index] = bytes;
                        fieldValues[fieldName] = workingList;
                        if (parentContainer != null)
                        {
                            MarkFieldAsEdited(parentContainer);
                        }
                    }, parentContainer);
            }
            else if (actualType.IsClass && !actualType.IsAbstract)
            {
                return CreateExpandableListItemEditor(actualType, itemValue, index,
                    workingList, fieldName, fieldValues, indentLevel, parentContainer);
            }
            else if (actualType.IsInterface || actualType.IsAbstract)
            {
                return CreateExpandableListItemEditor(itemType, itemValue, index,
                    workingList, fieldName, fieldValues, indentLevel, parentContainer);
            }
            else
            {
                return new TextBlock
                {
                    Text = itemValue?.ToString() ?? "[null]",
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
        }

        private FrameworkElement CreateExpandableListItemEditor(Type itemType, object itemValue,
            int index, System.Collections.IList workingList, string fieldName,
            Dictionary<string, object> fieldValues, int indentLevel, Border parentContainer)
        {
            var itemExpander = new Expander
            {
                Header = $"{itemType.Name} [{itemValue?.GetType().Name ?? "null"}]"
            };

            var itemContent = new StackPanel();

            if (itemValue != null)
            {
                var fieldLoader = new FieldLoader(mainWindow);
                var originalValues = mainWindow.GetType()
                    .GetField("originalValues", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(mainWindow) as Dictionary<FrameworkElement, object>;

                void UpdateItemValue()
                {
                    fieldValues[fieldName] = workingList;

                    if (parentContainer != null)
                        MarkFieldAsEdited(parentContainer);
                }

                var properties = itemValue.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var prop in properties.Where(p => p.CanRead && p.CanWrite))
                {
                    var propEditor = CreateInlineFieldEditor(
                        prop.Name,
                        prop.PropertyType,
                        prop.GetValue(itemValue),
                        (name, newValue) =>
                        {
                            prop.SetValue(itemValue, newValue);
                            UpdateItemValue();
                        },
                        indentLevel + 1,
                        originalValues,
                        parentContainer
                    );
                    itemContent.Children.Add(propEditor);
                }

                var fields = itemValue.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
                foreach (var field in fields)
                {
                    var fieldEditor = CreateInlineFieldEditor(
                        field.Name,
                        field.FieldType,
                        field.GetValue(itemValue),
                        (name, newValue) =>
                        {
                            field.SetValue(itemValue, newValue);
                            UpdateItemValue();
                        },
                        indentLevel + 1,
                        originalValues,
                        parentContainer
                    );
                    itemContent.Children.Add(fieldEditor);
                }
            }
            else
            {
                var createButton = new Button
                {
                    Content = "Create Instance",
                    Margin = new Thickness(20, 5, 0, 5),
                    HorizontalAlignment = HorizontalAlignment.Left
                };

                createButton.Click += (s, e) =>
                {
                    try
                    {
                        var newInstance = Activator.CreateInstance(itemType);
                        workingList[index] = newInstance;
                        fieldValues[fieldName] = workingList;

                        if (parentContainer != null)
                            MarkFieldAsEdited(parentContainer);

                        var current = itemExpander.Parent as FrameworkElement;
                        while (current != null)
                        {
                            if (current is Expander parentExpander && parentExpander.Tag is Action refreshAction)
                            {
                                refreshAction();
                                break;
                            }
                            current = current.Parent as FrameworkElement;
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error creating instance: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                itemContent.Children.Add(createButton);
            }

            itemExpander.Content = itemContent;
            return itemExpander;
        }

        private Expander CreateDictionaryEntryEditor(System.Collections.DictionaryEntry dictEntry,
            Type keyType, Type valueType, System.Collections.IDictionary workingDict,
            string fieldName, Dictionary<string, object> fieldValues, int indentLevel,
            Action refreshCallback, Border parentContainer)
        {
            var entryExpander = new Expander
            {
                Header = $"[{dictEntry.Key}]",
                Margin = new Thickness(0, 2, 0, 2)
            };

            var entryContent = new StackPanel();

            object currentKey = dictEntry.Key;
            object currentValue = dictEntry.Value;

            var keyGrid = new Grid { Margin = new Thickness(10, 2, 0, 2) };
            keyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            keyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var keyLabel = new TextBlock { Text = "Key:", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(keyLabel, 0);
            keyGrid.Children.Add(keyLabel);

            var keyTextBox = new TextBox
            {
                Text = dictEntry.Key.ToString(),
                IsReadOnly = false,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(keyTextBox, 1);
            keyGrid.Children.Add(keyTextBox);

            entryContent.Children.Add(keyGrid);

            var originalValues = mainWindow.GetType()
                .GetField("originalValues", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainWindow) as Dictionary<FrameworkElement, object>;

            var valueEditor = CreateInlineFieldEditor(
                "Value",
                valueType,
                currentValue,
                (name, newValue) =>
                {
                    workingDict[currentKey] = newValue;
                    currentValue = newValue;

                    fieldValues[fieldName] = workingDict;

                    if (parentContainer != null)
                        MarkFieldAsEdited(parentContainer);
                },
                1,
                originalValues,
                parentContainer
            );
            entryContent.Children.Add(valueEditor);

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(10, 5, 0, 5)
            };

            var changeKeyButton = new Button
            {
                Content = "Change Key",
                Margin = new Thickness(0, 0, 5, 0),
                Padding = new Thickness(5, 2, 5, 2)
            };

            changeKeyButton.Click += (s, e) =>
            {
                var newKeyText = keyTextBox.Text;
                if (newKeyText != null && newKeyText != currentKey.ToString())
                {
                    object newKey = newKeyText;
                    if (keyType != typeof(string))
                    {
                        try
                        {
                            newKey = Convert.ChangeType(newKeyText, keyType);
                        }
                        catch
                        {
                            MessageBox.Show($"Invalid key format for type {keyType.Name}", "Invalid Key",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }

                    if (!workingDict.Contains(newKey))
                    {
                        workingDict.Remove(currentKey);
                        workingDict.Add(newKey, currentValue);
                        currentKey = newKey;
                        entryExpander.Header = $"[{newKey}]";

                        fieldValues[fieldName] = workingDict;

                        if (parentContainer != null)
                            MarkFieldAsEdited(parentContainer);
                    }
                    else
                    {
                        MessageBox.Show("A key with this value already exists.", "Duplicate Key",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        keyTextBox.Text = currentKey.ToString();
                    }
                }
            };

            var removeBtn = new Button
            {
                Content = "Remove Entry",
                Margin = new Thickness(0, 0, 0, 0),
                Padding = new Thickness(5, 2, 5, 2)
            };

            removeBtn.Click += (s, e) =>
            {
                workingDict.Remove(currentKey);
                fieldValues[fieldName] = workingDict;

                if (parentContainer != null)
                    MarkFieldAsEdited(parentContainer);

                refreshCallback();
            };

            buttonsPanel.Children.Add(changeKeyButton);
            buttonsPanel.Children.Add(removeBtn);
            entryContent.Children.Add(buttonsPanel);

            entryExpander.Content = entryContent;
            return entryExpander;
        }

        private FrameworkElement CreateInlineByteArrayEditor(string fieldName, byte[] value,
            Action<string, object> onValueChanged, Border parentContainer = null)
        {
            var byteArrayPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var textBox = new TextBox
            {
                Text = value != null ? $"[{value.Length} bytes]" : "[null]",
                IsReadOnly = true,
                Width = 100
            };

            var editButton = new Button
            {
                Content = "Edit",
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(5, 2, 5, 2)
            };

            editButton.Click += (s, e) =>
            {
                var dialog = new ByteArrayEditDialog(value);
                if (dialog.ShowDialog() == true)
                {
                    var newBytes = dialog.ByteArray;
                    onValueChanged(fieldName, newBytes);
                    textBox.Text = $"[{newBytes.Length} bytes]";
                    value = newBytes;
                    if (parentContainer != null)
                        MarkFieldAsEdited(parentContainer);
                }
            };

            var loadButton = new Button
            {
                Content = "Load",
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(5, 2, 5, 2)
            };
            loadButton.Click += (s, e) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog();
                if (dialog.ShowDialog() == true)
                {
                    var bytes = System.IO.File.ReadAllBytes(dialog.FileName);
                    onValueChanged(fieldName, bytes);
                    textBox.Text = $"[{bytes.Length} bytes]";
                }
            };

            var saveButton = new Button
            {
                Content = "Save",
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(5, 2, 5, 2)
            };
            saveButton.Click += (s, e) =>
            {
                if (value != null)
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog { FileName = $"{fieldName}.dat" };
                    if (dialog.ShowDialog() == true)
                    {
                        System.IO.File.WriteAllBytes(dialog.FileName, value);
                        MessageBox.Show($"Saved {value.Length} bytes", "Save Successful",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    MessageBox.Show("No data to save", "Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            byteArrayPanel.Children.Add(textBox);
            byteArrayPanel.Children.Add(editButton);
            byteArrayPanel.Children.Add(loadButton);
            byteArrayPanel.Children.Add(saveButton);
            return byteArrayPanel;
        }

        public FrameworkElement CreateInlineFieldEditor(string fieldName, Type fieldType,
            object value, Action<string, object> onValueChanged, int indentLevel,
            Dictionary<FrameworkElement, object> originalValues, Border parentContainer = null)
        {
            var grid = new Grid { Margin = new Thickness(indentLevel * 20, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = controlFactory.CreateLabel(fieldName + ":");
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            FrameworkElement editor = null;

            if (value is System.Collections.IDictionary && !(value is string))
            {
                editor = CreateDictionaryEditor(fieldName, value.GetType(), value, indentLevel, parentContainer);
            }
            else if (value is System.Collections.IList && !(value is byte[]))
            {
                editor = CreateListEditor(fieldName, value.GetType(), value, indentLevel, parentContainer);
            }
            else if (value != null && value.GetType().IsGenericType &&
                value.GetType().GetGenericTypeDefinition() == typeof(HashSet<>))
            {
                editor = CreateHashSetEditor(fieldName, value.GetType(), value, indentLevel, parentContainer);
            }
            else if (fieldType.IsInterface || fieldType.IsAbstract)
            {
                editor = CreateInlineInterfaceEditor(fieldName, fieldType, value, onValueChanged, indentLevel, parentContainer);
            }
            else if (fieldType == typeof(string))
            {
                editor = controlFactory.CreateTextBox(value?.ToString() ?? "",
                    (text) => {
                        onValueChanged(fieldName, text);
                        if (parentContainer != null)
                            MarkFieldAsEdited(parentContainer);
                    });
            }
            else if (fieldType == typeof(bool))
            {
                editor = controlFactory.CreateCheckBox((bool)(value ?? false),
                    (isChecked) => {
                        onValueChanged(fieldName, isChecked);
                        if (parentContainer != null)
                            MarkFieldAsEdited(parentContainer);
                    });
            }
            else if (fieldType.IsEnum)
            {
                editor = controlFactory.CreateEnumComboBox(fieldType, value,
                    (selected) => {
                        onValueChanged(fieldName, selected);
                        if (parentContainer != null)
                            MarkFieldAsEdited(parentContainer);
                    });
            }
            else if (fieldType == typeof(byte[]))
            {
                editor = CreateInlineByteArrayEditor(fieldName, value as byte[], onValueChanged, parentContainer);
            }
            else if (fieldType.IsPrimitive || fieldType == typeof(decimal))
            {
                editor = controlFactory.CreateNumericTextBox(fieldType, value,
                    (newValue) => {
                        onValueChanged(fieldName, newValue);
                        if (parentContainer != null)
                            MarkFieldAsEdited(parentContainer);
                    });
            }
            else if (fieldType.IsArray && fieldType != typeof(byte[]))
            {
                editor = CreateInlineArrayEditor(fieldName, fieldType, value as Array, onValueChanged, indentLevel, parentContainer);
            }
            else if (value == null && fieldType.IsClass && fieldType != typeof(string))
            {
                editor = CreateInlineNullEditor(fieldName, fieldType, onValueChanged, grid, indentLevel, parentContainer);
            }
            else if (fieldType.IsClass && fieldType != typeof(string))
            {
                editor = CreateComplexTypeEditor(fieldName, fieldType, value, indentLevel, parentContainer);
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

        private FrameworkElement CreateInlineInterfaceEditor(string fieldName, Type interfaceType,
            object value, Action<string, object> onValueChanged, int indentLevel, Border parentContainer = null)
        {
            var mainPanel = new StackPanel();

            var compatibleTypes = TypeHelper.GetDerivedTypes(interfaceType)
                .Where(t => !t.IsAbstract && !t.IsInterface && t.IsPublic)
                .OrderBy(t => t.Name)
                .ToList();

            if (compatibleTypes.Count == 0)
            {
                return new TextBlock
                {
                    Text = "No compatible implementations found",
                    FontStyle = FontStyles.Italic,
                    Foreground = Brushes.Gray
                };
            }

            var selectionPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            var typeCombo = new ComboBox
            {
                ItemsSource = compatibleTypes,
                DisplayMemberPath = "Name",
                MinWidth = 150,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (value != null)
                typeCombo.SelectedItem = value.GetType();
            else if (compatibleTypes.Count > 0)
            {
                var defaultType = compatibleTypes.FirstOrDefault(t => t.Name == "NoCrypt") ?? compatibleTypes[0];
                typeCombo.SelectedItem = defaultType;
            }

            var createButton = new Button
            {
                Content = value != null ? "Change Type" : "Create",
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(10, 2, 10, 2),
                VerticalAlignment = VerticalAlignment.Center
            };

            selectionPanel.Children.Add(typeCombo);
            selectionPanel.Children.Add(createButton);
            mainPanel.Children.Add(selectionPanel);

            var fieldsPanel = new StackPanel
            {
                Margin = new Thickness(10, 5, 0, 0),
                Visibility = value != null ? Visibility.Visible : Visibility.Collapsed
            };
            mainPanel.Children.Add(fieldsPanel);

            if (value != null)
                LoadInlineFields(fieldsPanel, value, fieldName, onValueChanged, indentLevel + 1, parentContainer);

            createButton.Click += (s, e) =>
            {
                if (typeCombo.SelectedItem is Type selectedType)
                {
                    try
                    {
                        object newInstance = null;

                        var parameterlessConstructor = selectedType.GetConstructor(Type.EmptyTypes);
                        if (parameterlessConstructor != null)
                            newInstance = Activator.CreateInstance(selectedType);
                        else
                        {
                            var constructors = selectedType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                            if (constructors.Length == 0)
                            {
                                MessageBox.Show($"{selectedType.Name} has no public constructors.",
                                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                return;
                            }

                            var paramDialog = new ConstructorParametersDialog(selectedType);
                            paramDialog.Owner = Window.GetWindow(mainPanel);

                            if (paramDialog.ShowDialog() == true && paramDialog.CreatedInstance != null)
                                newInstance = paramDialog.CreatedInstance;
                            else
                                return;
                        }

                        if (newInstance != null)
                        {
                            value = newInstance;
                            onValueChanged(fieldName, newInstance);

                            if (parentContainer != null)
                                MarkFieldAsEdited(parentContainer);

                            fieldsPanel.Children.Clear();
                            LoadInlineFields(fieldsPanel, value, fieldName, onValueChanged, indentLevel + 1, parentContainer);
                            fieldsPanel.Visibility = Visibility.Visible;

                            createButton.Content = "Change Type";
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error creating {selectedType.Name}: {ex.Message}\n\nDetails: {ex.InnerException?.Message}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            };

            typeCombo.SelectionChanged += (s, e) =>
            {
                if (typeCombo.SelectedItem is Type selectedType)
                {
                    var constructors = selectedType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                    if (constructors.Length == 0)
                    {
                        createButton.ToolTip = "No public constructors";
                        createButton.IsEnabled = false;
                    }
                    else if (constructors.Any(c => c.GetParameters().Length == 0))
                    {
                        createButton.ToolTip = "Has default constructor";
                        createButton.IsEnabled = true;
                    }
                    else
                    {
                        var simplestCtor = constructors.OrderBy(c => c.GetParameters().Length).First();
                        var parameters = simplestCtor.GetParameters();
                        var paramText = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                        createButton.ToolTip = $"Constructor: ({paramText})";
                        createButton.IsEnabled = true;
                    }
                }
            };

            return mainPanel;
        }

        private void LoadInlineFields(StackPanel container, object instance, string parentFieldName,
            Action<string, object> parentOnValueChanged, int indentLevel, Border parentContainer = null)
        {
            if (instance == null) return;

            var type = instance.GetType();

            var typeHeader = new TextBlock
            {
                Text = $"{type.Name} class fields:",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            };
            container.Children.Add(typeHeader);

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                .OrderBy(p => p.Name);

            foreach (var prop in properties)
            {
                try
                {
                    var propValue = prop.GetValue(instance);

                    var editor = CreateInlineFieldEditor(
                        prop.Name,
                        prop.PropertyType,
                        propValue,
                        (name, newValue) =>
                        {
                            prop.SetValue(instance, newValue);
                            parentOnValueChanged(parentFieldName, instance);

                            if (parentContainer != null)
                                MarkFieldAsEdited(parentContainer);
                        },
                        indentLevel,
                        new Dictionary<FrameworkElement, object>(),
                        parentContainer
                    );
                    container.Children.Add(editor);
                }
                catch { }
            }

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly && !f.IsLiteral)
                .OrderBy(f => f.Name);

            foreach (var field in fields)
            {
                try
                {
                    var fieldValue = field.GetValue(instance);
                    var editor = CreateInlineFieldEditor(
                        field.Name,
                        field.FieldType,
                        fieldValue,
                        (name, newValue) =>
                        {
                            field.SetValue(instance, newValue);
                            parentOnValueChanged(parentFieldName, instance);

                            if (parentContainer != null)
                                MarkFieldAsEdited(parentContainer);
                        },
                        indentLevel,
                        new Dictionary<FrameworkElement, object>(),
                        parentContainer
                    );
                    container.Children.Add(editor);
                }
                catch { }
            }

            if (container.Children.Count == 1)
            {
                container.Children.Add(new TextBlock
                {
                    Text = "No editable fields",
                    FontStyle = FontStyles.Italic,
                    Foreground = Brushes.Gray
                });
            }
        }

        private FrameworkElement CreateInlineArrayEditor(string fieldName, Type arrayType, 
            Array array, Action<string, object> onValueChanged, int indentLevel, Border parentContainer = null)
        {
            var arrayExpander = new Expander
            {
                Header = $"{arrayType.GetElementType().Name}[{array?.Length ?? 0}]"
            };

            var arrayPanel = new StackPanel();

            if (array != null)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    var index = i;
                    var itemGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var indexLabel = new TextBlock
                    {
                        Text = $"[{index}]:",
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(indexLabel, 0);
                    itemGrid.Children.Add(indexLabel);

                    var elementType = arrayType.GetElementType();
                    var itemValue = array.GetValue(index);

                    FrameworkElement itemEditor = null;
                    if (elementType.IsPrimitive || elementType == typeof(string) || elementType == typeof(decimal))
                    {
                        if (elementType == typeof(string))
                        {
                            itemEditor = controlFactory.CreateTextBox(itemValue?.ToString() ?? "", (text) =>
                            {
                                array.SetValue(text, index);
                                onValueChanged(fieldName, array);
                                if (parentContainer != null)
                                    MarkFieldAsEdited(parentContainer);
                            });
                        }
                        else
                        {
                            itemEditor = controlFactory.CreateNumericTextBox(elementType, itemValue, (newValue) =>
                            {
                                array.SetValue(newValue, index);
                                onValueChanged(fieldName, array);
                                if (parentContainer != null)
                                    MarkFieldAsEdited(parentContainer);
                            });
                        }
                    }
                    else
                    {
                        itemEditor = CreateComplexTypeEditor($"{fieldName}[{index}]", elementType, itemValue, indentLevel + 1, null);
                    }

                    Grid.SetColumn(itemEditor, 1);
                    itemGrid.Children.Add(itemEditor);
                    arrayPanel.Children.Add(itemGrid);
                }

                var editButton = new Button
                {
                    Content = "Edit Array",
                    Margin = new Thickness(5, 5, 0, 0),
                    Padding = new Thickness(5, 2, 5, 2)
                };

                editButton.Click += (s, e) =>
                {
                    var dialog = new ArrayEditDialog(arrayType, array);
                    if (dialog.ShowDialog() == true)
                    {
                        onValueChanged(fieldName, dialog.ResultArray);
                        array = dialog.ResultArray;
                        if (parentContainer != null)
                            MarkFieldAsEdited(parentContainer);
                    }
                };
                arrayPanel.Children.Add(editButton);
            }

            arrayExpander.Content = arrayPanel;
            return arrayExpander;
        }

        private FrameworkElement CreateInlineNullEditor(string fieldName, Type fieldType,
            Action<string, object> onValueChanged, Grid parentGrid, int indentLevel, Border parentContainer = null)
        {
            if (fieldType.IsInterface || fieldType.IsAbstract)
            {
                var interfacePanel = new StackPanel();

                var nullText = new TextBlock
                {
                    Text = $"[null {(fieldType.IsInterface ? "interface" : "abstract")}]",
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                interfacePanel.Children.Add(nullText);

                var compatibleTypes = TypeHelper.GetDerivedTypes(fieldType)
                    .Where(t => !t.IsAbstract && !t.IsInterface)
                    .OrderBy(t => t.Name)
                    .ToList();

                if (compatibleTypes.Count > 0)
                {
                    var selectionPanel = new StackPanel { Orientation = Orientation.Horizontal };

                    var typeCombo = new ComboBox
                    {
                        ItemsSource = compatibleTypes,
                        DisplayMemberPath = "Name",
                        MinWidth = 120,
                        VerticalAlignment = VerticalAlignment.Center,
                        SelectedIndex = 0
                    };

                    var createButton = new Button
                    {
                        Content = "Create",
                        Margin = new Thickness(5, 0, 0, 0),
                        Padding = new Thickness(8, 2, 8, 2)
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
                                    paramDialog.Owner = Window.GetWindow(interfacePanel);

                                    if (paramDialog.ShowDialog() == true && paramDialog.CreatedInstance != null)
                                    {
                                        newInstance = paramDialog.CreatedInstance;
                                    }
                                }

                                if (newInstance != null)
                                {
                                    onValueChanged(fieldName, newInstance);
                                    if (parentContainer != null)
                                        MarkFieldAsEdited(parentContainer);

                                    var parent = parentGrid.Parent as Panel;
                                    if (parent != null)
                                    {
                                        var index = parent.Children.IndexOf(parentGrid);
                                        parent.Children.RemoveAt(index);
                                        var originalValues = mainWindow.GetType()
                                            .GetField("originalValues", BindingFlags.NonPublic | BindingFlags.Instance)
                                            ?.GetValue(mainWindow) as Dictionary<FrameworkElement, object>;
                                        var newEditor = CreateInlineFieldEditor(fieldName, fieldType, newInstance, 
                                            onValueChanged, indentLevel, originalValues, parentContainer);
                                        parent.Children.Insert(index, newEditor);
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

                    selectionPanel.Children.Add(typeCombo);
                    selectionPanel.Children.Add(createButton);
                    interfacePanel.Children.Add(selectionPanel);
                }

                return interfacePanel;
            }

            var nullPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var nullTextOriginal = new TextBlock
            {
                Text = "[null]",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            nullPanel.Children.Add(nullTextOriginal);

            var createButtonOriginal = new Button
            {
                Content = "Create",
                Padding = new Thickness(5, 2, 5, 2)
            };

            createButtonOriginal.Click += (s, e) =>
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
                        paramDialog.Owner = Window.GetWindow(nullPanel);

                        if (paramDialog.ShowDialog() == true && paramDialog.CreatedInstance != null)
                        {
                            newInstance = paramDialog.CreatedInstance;
                        }
                    }

                    if (newInstance != null)
                    {
                        onValueChanged(fieldName, newInstance);

                        var parent = parentGrid.Parent as Panel;
                        if (parent != null)
                        {
                            var index = parent.Children.IndexOf(parentGrid);
                            parent.Children.RemoveAt(index);
                            var originalValues = mainWindow.GetType()
                                .GetField("originalValues", BindingFlags.NonPublic | BindingFlags.Instance)
                                ?.GetValue(mainWindow) as Dictionary<FrameworkElement, object>;
                            var newEditor = CreateInlineFieldEditor(fieldName, fieldType, newInstance, 
                                onValueChanged, indentLevel, originalValues, parentContainer);
                            parent.Children.Insert(index, newEditor);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error creating instance: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            nullPanel.Children.Add(createButtonOriginal);
            return nullPanel;
        }

        public FrameworkElement CreateFieldEditor(string fieldName, Type fieldType,
            object value, Dictionary<string, object> fieldValues,
            Dictionary<FrameworkElement, object> originalValues, int indentLevel)
        {
            var border = controlFactory.CreateFieldContainer(indentLevel);
            var grid = controlFactory.CreateFieldGrid();

            originalValues[border] = value;

            var label = controlFactory.CreateLabel(fieldName + ":");
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            FrameworkElement editor = CreateEditorForType(fieldName, fieldType, value,
                fieldValues, originalValues, indentLevel, border);

            if (editor != null)
            {
                Grid.SetColumn(editor, 1);
                grid.Children.Add(editor);
            }

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(5, 0, 0, 0)
            };

            var removeButton = new Button
            {
                Content = "X",
                Width = 25,
                Height = 25,
                ToolTip = "Remove this field (disabled in this context)",
                IsEnabled = false
            };

            buttonPanel.Children.Add(removeButton);
            Grid.SetColumn(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            border.Child = grid;

            if (value != null)
                fieldValues[fieldName] = value;

            return border;
        }

        public FrameworkElement CreateEnumerableEditor(string fieldName, Type enumType, object value, int indentLevel, Border container)
        {
            var expander = new Expander
            {
                Header = $"{enumType.Name} [{(value as System.Collections.IEnumerable)?.Cast<object>().Count() ?? 0} items]"
            };

            var itemsPanel = new StackPanel();

            if (value != null)
            {
                var items = (value as System.Collections.IEnumerable).Cast<object>().ToList();
                for (int i = 0; i < items.Count; i++)
                {
                    var itemContainer = controlFactory.CreateItemContainer();
                    var itemText = new TextBlock
                    {
                        Text = $"[{i}]: {items[i]?.ToString() ?? "[null]"}",
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    itemContainer.Child = itemText;
                    itemsPanel.Children.Add(itemContainer);
                }
            }

            expander.Content = itemsPanel;
            return expander;
        }
    }
}