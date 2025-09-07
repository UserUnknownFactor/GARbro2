// FieldEditor.CollectionEditors.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Reflection;

namespace SchemeEditor
{
    public partial class FieldEditor
    {
        public FrameworkElement CreateListEditor (string fieldName, Type listType, object value, int indentLevel, Border container)
        {
            var expander = new Expander
            {
                Header = $"List [{(value as System.Collections.IList)?.Count ?? 0} items]"
            };

            var stackPanel = new StackPanel();

            // Button panel for list operations
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness (0, 5, 0, 5) };

            var addButton = new Button
            {
                Content = "Add Item",
                Margin = new Thickness (0, 0, 5, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var importCsvButton = new Button
            {
                Content = "Import CSV",
                Margin = new Thickness (0, 0, 5, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            buttonPanel.Children.Add (addButton);
            buttonPanel.Children.Add (importCsvButton);

            var itemsPanel = new StackPanel();

            Type itemType = typeof(object);
            if (listType.IsGenericType)
            {
                var genericArgs = listType.GetGenericArguments();
                if (genericArgs.Length > 0)
                    itemType = genericArgs[0];
            }
            else if (value != null && value is System.Collections.IList list && list.Count > 0)
            {
                itemType = list[0]?.GetType() ?? typeof(object);
            }

            System.Collections.IList workingList = null;
            bool isArray = false;
            Type arrayElementType = null;

            if (value != null)
            {
                if (value is Array sourceArray)
                {
                    isArray = true;
                    arrayElementType = sourceArray.GetType().GetElementType();
                    var listTypeForEditing = typeof(List<>).MakeGenericType (arrayElementType);
                    workingList = Activator.CreateInstance (listTypeForEditing) as System.Collections.IList;

                    foreach (var item in sourceArray)
                        workingList.Add (DeepCloneValue (item));
                }
                else if (value is System.Collections.IList sourceList)
                {
                    if (listType.IsGenericType)
                        workingList = Activator.CreateInstance (listType) as System.Collections.IList;
                    else
                        workingList = new System.Collections.ArrayList();

                    foreach (var item in sourceList)
                        workingList.Add (DeepCloneValue (item));
                }
            }
            else
            {
                if (listType.IsGenericType)
                    workingList = Activator.CreateInstance (listType) as System.Collections.IList;
                else
                    workingList = new System.Collections.ArrayList();
            }

            var fieldValues = mainWindow.GetType()
                .GetField ("currentFieldValues", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue (mainWindow) as Dictionary<string, object>;

            if (isArray && arrayElementType != null)
            {
                var array = Array.CreateInstance (arrayElementType, workingList.Count);
                workingList.CopyTo (array, 0);
                fieldValues[fieldName] = array;
            }
            else
                fieldValues[fieldName] = workingList;

            expander.Tag = container;

            void RefreshList()
            {
                itemsPanel.Children.Clear();
                expander.Header = $"List [{workingList.Count} items]";

                if (isArray && arrayElementType != null)
                {
                    var array = Array.CreateInstance (arrayElementType, workingList.Count);
                    workingList.CopyTo (array, 0);
                    fieldValues[fieldName] = array;
                }
                else
                    fieldValues[fieldName] = workingList;

                for (int i = 0; i < workingList.Count; i++)
                {
                    var index = i;
                    var itemValue = workingList[index];

                    var itemContainer = controlFactory.CreateItemContainer();
                    var itemPanel = controlFactory.CreateItemGrid();

                    var indexLabel = new TextBlock
                    {
                        Text = $"[{index}]",
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness (0, 0, 5, 0),
                        FontWeight = FontWeights.Bold
                    };
                    Grid.SetColumn (indexLabel, 0);
                    itemPanel.Children.Add (indexLabel);

                    FrameworkElement itemEditor = CreateListItemEditor (itemType, itemValue, index,
                        workingList, fieldName, fieldValues, indentLevel, container);
                    Grid.SetColumn (itemEditor, 1);
                    itemPanel.Children.Add (itemEditor);

                    var removeBtn = new Button
                    {
                        Content = "X",
                        Width = 20,
                        Height = 20,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness (5, 0, 0, 0)
                    };

                    var currentIndex = index;
                    removeBtn.Click += (s, e) =>
                    {
                        if (currentIndex < workingList.Count)
                            workingList.RemoveAt (currentIndex);

                        RefreshList();

                        if (container != null)
                            MarkFieldAsEdited (container);
                    };

                    Grid.SetColumn (removeBtn, 2);
                    itemPanel.Children.Add (removeBtn);

                    itemContainer.Child = itemPanel;
                    itemsPanel.Children.Add (itemContainer);
                }
            }

            // CSV Import handler
            importCsvButton.Click += (s, e) =>
            {
                bool imported = false;

                if (itemType == typeof(string))
                {
                    var importedList = CsvImporter.ImportStringList ($"Import CSV for {fieldName}");
                    if (importedList != null)
                    {
                        foreach (var item in importedList)
                            workingList.Add (item);
                        imported = true;
                    }
                }
                else if (itemType == typeof(int))
                {
                    var importedList = CsvImporter.ImportIntList ($"Import CSV for {fieldName}");
                    if (importedList != null)
                    {
                        foreach (var item in importedList)
                            workingList.Add (item);
                        imported = true;
                    }
                }
                else if (itemType == typeof(uint))
                {
                    var importedList = CsvImporter.ImportUIntList ($"Import CSV for {fieldName}");
                    if (importedList != null)
                    {
                        foreach (var item in importedList)
                            workingList.Add (item);
                        imported = true;
                    }
                }
                else if (itemType == typeof(long))
                {
                    var importedList = CsvImporter.ImportLongList ($"Import CSV for {fieldName}");
                    if (importedList != null)
                    {
                        foreach (var item in importedList)
                            workingList.Add (item);
                        imported = true;
                    }
                }
                else if (itemType.IsPrimitive || itemType == typeof(decimal))
                {
                    // Generic numeric import
                    var stringList = CsvImporter.ImportStringList ($"Import CSV for {fieldName}");
                    if (stringList != null)
                    {
                        foreach (var str in stringList)
                        {
                            var converted = ConvertToType (str, itemType);
                            if (converted != null)
                            {
                                workingList.Add (converted);
                            }
                        }
                        imported = true;
                    }
                }
                else
                {
                    MessageBox.Show ($"CSV import is not supported for {itemType.Name} type", 
                        "Import Not Supported", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                if (imported)
                {
                    RefreshList();
                    if (container != null)
                        MarkFieldAsEdited (container);
                }
            };

            addButton.Click += (s, e) =>
            {
                if (workingList.Count > 0 && itemType.IsClass && itemType != typeof(string))
                {
                    try
                    {
                        var firstItem = workingList[0];
                        var clonedItem = DeepCloneValue (firstItem);
                        if (!workingList.IsFixedSize)
                        {
                            workingList.Add (clonedItem);
                            if (container != null)
                                MarkFieldAsEdited (container);
                        }
                        RefreshList();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine ($"Error adding item: {ex.Message}");
                        ShowAddItemDialog();
                    }
                }
                else
                    ShowAddItemDialog();
            };

            void ShowAddItemDialog()
            {
                var dialog = new AddListItemDialog (itemType);
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        workingList.Add (dialog.Value);
                        RefreshList();

                        if (container != null)
                            MarkFieldAsEdited (container);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show ($"Error adding item: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }

                        RefreshList();

            stackPanel.Children.Add (buttonPanel);
            stackPanel.Children.Add (itemsPanel);
            expander.Content = stackPanel;

            return expander;
        }

        public FrameworkElement CreateDictionaryEditor (string fieldName, Type dictType, object value, int indentLevel, Border container)
        {
            var expander = new Expander
            {
                Header = $"Dictionary [{(value as System.Collections.IDictionary)?.Count ?? 0} items]"
            };

            var stackPanel = new StackPanel();

            // Button panel for dictionary operations
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness (0, 5, 0, 5) };

            var addButton = new Button
            {
                Content = "Add Entry",
                Margin = new Thickness (0, 0, 5, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var importCsvButton = new Button
            {
                Content = "Import CSV",
                Margin = new Thickness (0, 0, 5, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var clearButton = new Button
            {
                Content = "Clear All",
                Margin = new Thickness (0, 0, 5, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            buttonPanel.Children.Add (addButton);
            buttonPanel.Children.Add (importCsvButton);
            buttonPanel.Children.Add (clearButton);

            var itemsPanel = new StackPanel();

            Type keyType = typeof(object);
            Type valueType = typeof(object);

            if (dictType.IsGenericType)
            {
                var genericArgs = dictType.GetGenericArguments();
                if (genericArgs.Length >= 2)
                {
                    keyType = genericArgs[0];
                    valueType = genericArgs[1];
                }
            }

            System.Collections.IDictionary workingDict = null;
            if (value != null && value is System.Collections.IDictionary originalDict)
            {
                if (dictType.IsGenericType)
                {
                    var genericArgs = dictType.GetGenericArguments();
                    var keyTypeI = genericArgs[0];
                    var valueTypeI = genericArgs[1];
                    var concreteDictType = dictType.IsInterface ?
                        typeof(Dictionary<,>).MakeGenericType (keyTypeI, valueTypeI) : dictType;
                    workingDict = Activator.CreateInstance (concreteDictType) as System.Collections.IDictionary;

                    foreach (System.Collections.DictionaryEntry entry in originalDict)
                    {
                        object clonedValue = DeepCloneValue (entry.Value);
                        workingDict.Add (entry.Key, clonedValue);
                    }
                }
                else
                {
                    workingDict = new System.Collections.Hashtable();
                    foreach (System.Collections.DictionaryEntry entry in originalDict)
                    {
                        object clonedValue = DeepCloneValue (entry.Value);
                        workingDict.Add (entry.Key, clonedValue);
                    }
                }
            }
            else
            {
                if (dictType.IsGenericType)
                {
                    var concreteDictType = dictType.IsInterface ?
                        typeof(Dictionary<,>).MakeGenericType (keyType, valueType) : dictType;
                    workingDict = Activator.CreateInstance (concreteDictType) as System.Collections.IDictionary;
                }
                else
                {
                    workingDict = new System.Collections.Hashtable();
                }
            }

            var fieldValues = mainWindow.GetType()
                .GetField ("currentFieldValues", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue (mainWindow) as Dictionary<string, object>;

            fieldValues[fieldName] = workingDict;

            expander.Tag = container;

            void RefreshDictionary()
            {
                itemsPanel.Children.Clear();
                expander.Header = $"Dictionary [{workingDict.Count} items]";

                fieldValues[fieldName] = workingDict;

                var entries = new List<System.Collections.DictionaryEntry>();
                var enumerator = workingDict.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    if (enumerator.Current is System.Collections.DictionaryEntry entry)
                        entries.Add (entry);
                }

                foreach (var dictEntry in entries)
                {
                    var entryExpander = CreateDictionaryEntryEditor (dictEntry, keyType, valueType,
                        workingDict, fieldName, fieldValues, indentLevel, RefreshDictionary, container);
                    itemsPanel.Children.Add (entryExpander);
                }
            }

            // CSV Import handler
            importCsvButton.Click += (s, e) =>
            {
                bool imported = false;

                // Check if both key and value types are simple types that can be imported from CSV
                var supportedTypes = new[] { 
                    typeof(string), typeof(int), typeof(uint), typeof(long), typeof(ulong),
                    typeof(short), typeof(ushort), typeof(byte), typeof(sbyte),
                    typeof(float), typeof(double), typeof(decimal), typeof(bool)
                };

                if (supportedTypes.Contains (keyType) && supportedTypes.Contains (valueType))
                {
                    try
                    {
                        var dialog = new Microsoft.Win32.OpenFileDialog
                        {
                            Title = $"Import CSV for {fieldName}",
                            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                            FilterIndex = 1
                        };

                        if (dialog.ShowDialog() == true)
                        {
                            var lines = System.IO.File.ReadAllLines (dialog.FileName);
                            int successCount = 0;
                            int failCount = 0;

                            foreach (var line in lines)
                            {
                                var values = CsvImporter.ParseCsvLine (line);
                                if (values.Count >= 2)
                                {
                                    try
                                    {
                                        object key = ConvertToType (values[0].Trim(), keyType);
                                        object val = ConvertToType (values[1].Trim(), valueType);

                                        if (key != null && val != null)
                                        {
                                            workingDict[key] = val;
                                            successCount++;
                                        }
                                        else
                                        {
                                            failCount++;
                                        }
                                    }
                                    catch
                                    {
                                        failCount++;
                                    }
                                }
                            }

                            imported = true;

                            // Show import summary
                            var message = $"Import completed:\n{successCount} entries imported successfully";
                            if (failCount > 0)
                                message += $"\n{failCount} entries failed to import";

                            MessageBox.Show (message, "Import Complete", 
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show ($"Error importing CSV: {ex.Message}", "Import Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show ($"CSV import is not supported for Dictionary<{keyType.Name}, {valueType.Name}>.\n" +
                        "Only simple types (string, numeric, bool) are supported.", 
                        "Import Not Supported", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                if (imported)
                {
                    RefreshDictionary();
                    if (container != null)
                        MarkFieldAsEdited (container);
                }
            };

            clearButton.Click += (s, e) =>
            {
                var result = MessageBox.Show ("Are you sure you want to clear all entries?", 
                    "Confirm Clear", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    workingDict.Clear();
                    RefreshDictionary();
                    if (container != null)
                        MarkFieldAsEdited (container);
                }
            };

            addButton.Click += (s, e) =>
            {
                var dialog = new AddDictionaryEntryDialog (keyType, valueType);
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        if (!workingDict.Contains (dialog.Key))
                        {
                            workingDict.Add (dialog.Key, dialog.Value);
                            fieldValues[fieldName] = workingDict;
                            if (container != null)
                                MarkFieldAsEdited (container);

                            RefreshDictionary();
                        }
                        else
                        {
                            MessageBox.Show ("A key with this value already exists.", "Duplicate Key",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show ($"Error adding entry: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            };

            RefreshDictionary();

            stackPanel.Children.Add (buttonPanel);
            stackPanel.Children.Add (itemsPanel);
            expander.Content = stackPanel;

            return expander;
        }

        public FrameworkElement CreateHashSetEditor (string fieldName, Type setType, object value, int indentLevel, Border container)
        {
            var expander = new Expander
            {
                Header = $"HashSet [{(value as System.Collections.IEnumerable)?.Cast<object>().Count() ?? 0} items]"
            };

            var stackPanel = new StackPanel();

            // Button panel for HashSet operations
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness (0, 5, 0, 5) };

            var addButton = new Button
            {
                Content = "Add Item",
                Margin = new Thickness (0, 0, 5, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var importCsvButton = new Button
            {
                Content = "Import CSV",
                Margin = new Thickness (0, 0, 5, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var clearButton = new Button
            {
                Content = "Clear All",
                Margin = new Thickness (0, 0, 5, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            buttonPanel.Children.Add (addButton);
            buttonPanel.Children.Add (importCsvButton);
            buttonPanel.Children.Add (clearButton);

            var itemsPanel = new StackPanel();

            Type itemType = setType.GetGenericArguments()[0];

            var workingSet = value != null ? Activator.CreateInstance (setType, value) : Activator.CreateInstance (setType);

            var fieldValues = mainWindow.GetType()
                .GetField ("currentFieldValues", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue (mainWindow) as Dictionary<string, object>;
            fieldValues[fieldName] = workingSet;

            expander.Tag = container;

            void RefreshSet()
            {
                itemsPanel.Children.Clear();
                var items = (workingSet as System.Collections.IEnumerable).Cast<object>().ToList();
                expander.Header = $"HashSet [{items.Count} items]";

                foreach (var item in items)
                {
                    var itemContainer = controlFactory.CreateItemContainer();
                    var itemPanel = new Grid();
                    itemPanel.ColumnDefinitions.Add (new ColumnDefinition { Width = new GridLength (1, GridUnitType.Star) });
                    itemPanel.ColumnDefinitions.Add (new ColumnDefinition { Width = GridLength.Auto });

                    FrameworkElement itemEditor = null;
                    if (itemType == typeof(string))
                    {
                        var textBox = new TextBox
                        {
                            Text = item?.ToString() ?? "",
                            VerticalAlignment = VerticalAlignment.Center,
                            IsReadOnly = true
                        };
                        itemEditor = textBox;
                    }
                    else if (itemType.IsPrimitive || itemType == typeof(decimal))
                    {
                        var textBox = new TextBox
                        {
                            Text = item?.ToString() ?? "",
                            VerticalAlignment = VerticalAlignment.Center,
                            IsReadOnly = true
                        };
                        itemEditor = textBox;
                    }
                    else if (itemType.IsEnum)
                    {
                        var textBlock = new TextBlock
                        {
                            Text = item?.ToString() ?? "[null]",
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        itemEditor = textBlock;
                    }
                    else
                    {
                        var textBlock = new TextBlock
                        {
                            Text = item?.ToString() ?? "[null]",
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        itemEditor = textBlock;
                    }

                    Grid.SetColumn (itemEditor, 0);
                    itemPanel.Children.Add (itemEditor);

                    var removeBtn = new Button
                    {
                        Content = "X",
                        Width = 20,
                        Height = 20,
                        Margin = new Thickness (5, 0, 0, 0)
                    };
                    removeBtn.Click += (s, e) =>
                    {
                        var removeMethod = setType.GetMethod ("Remove");
                        removeMethod.Invoke (workingSet, new[] { item });
                        if (container != null)
                            MarkFieldAsEdited (container);
                        RefreshSet();
                    };
                    Grid.SetColumn (removeBtn, 1);
                    itemPanel.Children.Add (removeBtn);

                    itemContainer.Child = itemPanel;
                    itemsPanel.Children.Add (itemContainer);
                }
            }

            // CSV Import handler
            importCsvButton.Click += (s, e) =>
            {
                bool imported = false;

                if (itemType == typeof(string))
                {
                    var importedList = CsvImporter.ImportStringList ($"Import CSV for {fieldName}");
                    if (importedList != null)
                    {
                        var addMethod = setType.GetMethod ("Add");
                        foreach (var item in importedList)
                        {
                            addMethod.Invoke (workingSet, new[] { item });
                        }
                        imported = true;
                    }
                }
                else if (itemType == typeof(int))
                {
                    var importedList = CsvImporter.ImportIntList ($"Import CSV for {fieldName}");
                    if (importedList != null)
                    {
                        var addMethod = setType.GetMethod ("Add");
                        foreach (var item in importedList)
                        {
                            addMethod.Invoke (workingSet, new object[] { item });
                        }
                        imported = true;
                    }
                }
                else if (itemType == typeof(uint))
                {
                    var importedList = CsvImporter.ImportUIntList ($"Import CSV for {fieldName}");
                    if (importedList != null)
                    {
                        var addMethod = setType.GetMethod ("Add");
                        foreach (var item in importedList)
                        {
                            addMethod.Invoke (workingSet, new object[] { item });
                        }
                        imported = true;
                    }
                }
                else if (itemType.IsPrimitive || itemType == typeof(decimal))
                {
                    // Generic numeric import
                    var stringList = CsvImporter.ImportStringList ($"Import CSV for {fieldName}");
                    if (stringList != null)
                    {
                        var addMethod = setType.GetMethod ("Add");
                        foreach (var str in stringList)
                        {
                            var converted = ConvertToType (str, itemType);
                            if (converted != null)
                            {
                                addMethod.Invoke (workingSet, new[] { converted });
                            }
                        }
                        imported = true;
                    }
                }
                else
                {
                    MessageBox.Show ($"CSV import is not supported for HashSet<{itemType.Name}>", 
                        "Import Not Supported", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                if (imported)
                {
                    RefreshSet();
                    if (container != null)
                        MarkFieldAsEdited (container);
                }
            };

            clearButton.Click += (s, e) =>
            {
                var result = MessageBox.Show ("Are you sure you want to clear all items?", 
                    "Confirm Clear", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var clearMethod = setType.GetMethod ("Clear");
                    clearMethod.Invoke (workingSet, null);
                    RefreshSet();
                    if (container != null)
                        MarkFieldAsEdited (container);
                }
            };

            addButton.Click += (s, e) =>
            {
                var items = (workingSet as System.Collections.IEnumerable).Cast<object>().ToList();
                if (items.Count > 0 && itemType.IsClass && itemType != typeof(string))
                {
                    try
                    {
                        var clonedItem = CloneObject (items[0]);
                        var addMethod = setType.GetMethod ("Add");
                        addMethod.Invoke (workingSet, new[] { clonedItem });
                        RefreshSet();
                        if (container != null)
                            MarkFieldAsEdited (container);
                        return;
                    }
                    catch { }
                }

                var dialog = new AddListItemDialog (itemType);
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        var addMethod = setType.GetMethod ("Add");
                        var result = addMethod.Invoke (workingSet, new[] { dialog.Value });
                        if (result is bool added && !added)
                        {
                            MessageBox.Show ("Item already exists in the set.", "Duplicate Item",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }

                        if (container != null)
                            MarkFieldAsEdited (container);
                        RefreshSet();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show ($"Error adding item: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            };

            RefreshSet();

            stackPanel.Children.Add (buttonPanel);
            stackPanel.Children.Add (itemsPanel);
            expander.Content = stackPanel;

            return expander;
        }

        private bool ImportFromMultipleCsvFiles (System.Collections.IDictionary workingDict, 
            Type keyType, Type valueType, string fieldName)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select CSV files for {fieldName}",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FilterIndex = 1,
                Multiselect = true  // Enable multiple file selection
            };

            if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
            {
                int successCount = 0;
                int failCount = 0;
                var errors = new List<string>();

                foreach (var filePath in dialog.FileNames)
                {
                    try
                    {
                        // Use filename (without extension) as the key
                        var fileName = System.IO.Path.GetFileNameWithoutExtension (filePath);

                        // Create the nested collection for this file
                        object nestedCollection = null;

                        if (valueType.IsGenericType)
                        {
                            var valueGenericDef = valueType.GetGenericTypeDefinition();
                            var valueGenericArgs = valueType.GetGenericArguments();

                            if (valueGenericDef == typeof(Dictionary<,>) || 
                                (valueType.IsInterface && valueType.GetGenericTypeDefinition() == typeof(IDictionary<,>)))
                            {
                                // Value is a Dictionary
                                var innerKeyType = valueGenericArgs[0];
                                var innerValueType = valueGenericArgs[1];

                                nestedCollection = ImportDictionaryFromCsv (filePath, innerKeyType, innerValueType, valueType);
                            }
                            else if (valueGenericDef == typeof(List<>) || 
                                (valueType.IsInterface && valueType.GetGenericTypeDefinition() == typeof(IList<>)))
                            {
                                // Value is a List
                                var innerItemType = valueGenericArgs[0];
                                nestedCollection = ImportListFromCsv (filePath, innerItemType, valueType);
                            }
                            else if (valueGenericDef == typeof(HashSet<>) || 
                                (valueType.IsInterface && valueType.GetGenericTypeDefinition() == typeof(ISet<>)))
                            {
                                // Value is a HashSet
                                var innerItemType = valueGenericArgs[0];
                                nestedCollection = ImportHashSetFromCsv (filePath, innerItemType, valueType);
                            }
                        }

                        if (nestedCollection != null)
                        {
                            workingDict[fileName] = nestedCollection;
                            successCount++;
                        }
                        else
                        {
                            failCount++;
                            errors.Add ($"{fileName}: Failed to parse content");
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        var fileName = System.IO.Path.GetFileNameWithoutExtension (filePath);
                        errors.Add ($"{fileName}: {ex.Message}");
                    }
                }

                // Show import summary
                var message = $"Import completed:\n{successCount} files imported successfully";
                if (failCount > 0)
                {
                    message += $"\n{failCount} files failed to import";
                    if (errors.Count > 0 && errors.Count <= 5)
                    {
                        message += "\n\nErrors:\n" + string.Join ("\n", errors.Take (5));
                    }
                    else if (errors.Count > 5)
                    {
                        message += $"\n\nFirst 5 errors:\n" + string.Join ("\n", errors.Take (5));
                        message += $"\n... and {errors.Count - 5} more errors";
                    }
                }

                MessageBox.Show (message, "Import Complete", 
                    MessageBoxButton.OK, 
                    failCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                return successCount > 0;
            }

            return false;
        }

        private object ImportDictionaryFromCsv (string filePath, Type keyType, Type valueType, Type dictionaryType)
        {
            var lines = System.IO.File.ReadAllLines (filePath);

            // Create instance of the dictionary
            System.Collections.IDictionary dict;
            if (dictionaryType.IsInterface)
            {
                var concreteDictType = typeof(Dictionary<,>).MakeGenericType (keyType, valueType);
                dict = Activator.CreateInstance (concreteDictType) as System.Collections.IDictionary;
            }
            else
            {
                dict = Activator.CreateInstance (dictionaryType) as System.Collections.IDictionary;
            }

            // Check if the value type is also a collection (for nested structures)
            bool isNestedCollection = valueType.IsGenericType && 
                (valueType.GetGenericTypeDefinition() == typeof(List<>) ||
                 valueType.GetGenericTypeDefinition() == typeof(HashSet<>) ||
                 valueType.GetInterfaces().Any (i => i.IsGenericType && 
                    (i.GetGenericTypeDefinition() == typeof(IList<>) || 
                     i.GetGenericTypeDefinition() == typeof(ISet<>))));

            if (isNestedCollection)
            {
                // Handle Dictionary<TKey, List<TValue>> or Dictionary<TKey, HashSet<TValue>>
                var valueGenericArgs = valueType.GetGenericArguments();
                var innerItemType = valueGenericArgs[0];

                // Group by first column (key) and collect all values for each key
                var groupedData = new Dictionary<string, List<string>>();

                foreach (var line in lines)
                {
                    var values = CsvImporter.ParseCsvLine (line);
                    if (values.Count >= 2)
                    {
                        var keyStr = values[0].Trim();
                        var valueStr = values[1].Trim();

                        if (!groupedData.ContainsKey (keyStr))
                            groupedData[keyStr] = new List<string>();

                        groupedData[keyStr].Add (valueStr);

                        // If there are more columns, add them too
                        for (int i = 2; i < values.Count; i++)
                        {
                            if (!string.IsNullOrWhiteSpace (values[i]))
                                groupedData[keyStr].Add (values[i].Trim());
                        }
                    }
                }

                // Convert grouped data to final dictionary
                foreach (var kvp in groupedData)
                {
                    var key = ConvertToType (kvp.Key, keyType);
                    if (key != null)
                    {
                        object collection;
                        if (valueType.GetGenericTypeDefinition() == typeof(List<>) || 
                            (valueType.IsInterface && valueType.GetGenericTypeDefinition() == typeof(IList<>)))
                        {
                            var listType = valueType.IsInterface ? 
                                typeof(List<>).MakeGenericType (innerItemType) : valueType;
                            collection = Activator.CreateInstance (listType);
                            var list = collection as System.Collections.IList;

                            foreach (var valueStr in kvp.Value)
                            {
                                var convertedValue = ConvertToType (valueStr, innerItemType);
                                if (convertedValue != null)
                                    list.Add (convertedValue);
                            }
                        }
                        else // HashSet
                        {
                            var setType = valueType.IsInterface ? 
                                typeof(HashSet<>).MakeGenericType (innerItemType) : valueType;
                            collection = Activator.CreateInstance (setType);
                            var addMethod = setType.GetMethod ("Add");

                            foreach (var valueStr in kvp.Value)
                            {
                                var convertedValue = ConvertToType (valueStr, innerItemType);
                                if (convertedValue != null)
                                    addMethod.Invoke (collection, new[] { convertedValue });
                            }
                        }

                        dict[key] = collection;
                    }
                }
            }
            else
            {
                // Simple Dictionary<TKey, TValue>
                foreach (var line in lines)
                {
                    var values = CsvImporter.ParseCsvLine (line);
                    if (values.Count >= 2)
                    {
                        var key = ConvertToType (values[0].Trim(), keyType);
                        var value = ConvertToType (values[1].Trim(), valueType);

                        if (key != null && value != null)
                        {
                            dict[key] = value;
                        }
                    }
                }
            }

            return dict;
        }

        private object ImportListFromCsv (string filePath, Type itemType, Type listType)
        {
            var lines = System.IO.File.ReadAllLines (filePath);

            // Create instance of the list
            System.Collections.IList list;
            if (listType.IsInterface)
            {
                var concreteListType = typeof(List<>).MakeGenericType (itemType);
                list = Activator.CreateInstance (concreteListType) as System.Collections.IList;
            }
            else
            {
                list = Activator.CreateInstance (listType) as System.Collections.IList;
            }

            // Import all values from the CSV
            foreach (var line in lines)
            {
                var values = CsvImporter.ParseCsvLine (line);

                // Add all non-empty values from each line
                foreach (var valueStr in values)
                {
                    if (!string.IsNullOrWhiteSpace (valueStr))
                    {
                        var convertedValue = ConvertToType (valueStr.Trim(), itemType);
                        if (convertedValue != null)
                        {
                            list.Add (convertedValue);
                        }
                    }
                }
            }

            return list;
        }

        private object ImportHashSetFromCsv (string filePath, Type itemType, Type setType)
        {
            var lines = System.IO.File.ReadAllLines (filePath);

            // Create instance of the HashSet
            object set;
            if (setType.IsInterface)
            {
                var concreteSetType = typeof(HashSet<>).MakeGenericType (itemType);
                set = Activator.CreateInstance (concreteSetType);
            }
            else
            {
                set = Activator.CreateInstance (setType);
            }

            var addMethod = set.GetType().GetMethod ("Add");

            // Import all unique values from the CSV
            foreach (var line in lines)
            {
                var values = CsvImporter.ParseCsvLine (line);

                // Add all non-empty values from each line
                foreach (var valueStr in values)
                {
                    if (!string.IsNullOrWhiteSpace (valueStr))
                    {
                        var convertedValue = ConvertToType (valueStr.Trim(), itemType);
                        if (convertedValue != null)
                        {
                            addMethod.Invoke (set, new[] { convertedValue });
                        }
                    }
                }
            }

            return set;
        }

        private bool ImportFromSingleCsvFile (System.Collections.IDictionary workingDict, 
            Type keyType, Type valueType, string fieldName)
        {
            // Original single-file import logic
            var supportedTypes = new[] { 
                typeof(string), typeof(int), typeof(uint), typeof(long), typeof(ulong),
                typeof(short), typeof(ushort), typeof(byte), typeof(sbyte),
                typeof(float), typeof(double), typeof(decimal), typeof(bool)
            };

            if (supportedTypes.Contains (keyType) && supportedTypes.Contains (valueType))
            {
                try
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = $"Import CSV for {fieldName}",
                        Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                        FilterIndex = 1
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        var lines = System.IO.File.ReadAllLines (dialog.FileName);
                        int successCount = 0;
                        int failCount = 0;

                        foreach (var line in lines)
                        {
                            var values = CsvImporter.ParseCsvLine (line);
                            if (values.Count >= 2)
                            {
                                try
                                {
                                    object key = ConvertToType (values[0].Trim(), keyType);
                                    object val = ConvertToType (values[1].Trim(), valueType);

                                    if (key != null && val != null)
                                    {
                                        workingDict[key] = val;
                                        successCount++;
                                    }
                                    else
                                    {
                                        failCount++;
                                    }
                                }
                                catch
                                {
                                    failCount++;
                                }
                            }
                        }

                        var message = $"Import completed:\n{successCount} entries imported successfully";
                        if (failCount > 0)
                            message += $"\n{failCount} entries failed to import";

                        MessageBox.Show (message, "Import Complete", 
                            MessageBoxButton.OK, MessageBoxImage.Information);

                        return successCount > 0;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show ($"Error importing CSV: {ex.Message}", "Import Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show ($"CSV import is not supported for Dictionary<{keyType.Name}, {valueType.Name}>.\n" +
                    "Only simple types (string, numeric, bool) are supported for single file import.", 
                    "Import Not Supported", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return false;
        }
    }
}