using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using System.Windows.Data;
using System.Reflection;
using System.Diagnostics;

using GameRes;
using GameRes.Utility;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;

namespace SchemeEditor
{
    public class FieldEditor
    {
        private readonly MainWindow mainWindow;
        private readonly ControlFactory controlFactory;

        public FieldEditor(MainWindow window)
        {
            mainWindow = window;
            controlFactory = new ControlFactory();
        }

        public void MarkFieldAsEdited(Border container)
        {
            container.BorderBrush = Brushes.Orange;
            container.BorderThickness = new Thickness(2);

            // Mark main window as having changes
            var setHasChangesMethod = mainWindow.GetType()
                .GetMethod("SetHasChanges", BindingFlags.Public | BindingFlags.Instance);
            setHasChangesMethod?.Invoke(mainWindow, new object[] { true });
        }

        private void MarkFieldAsConfirmed(Border container)
        {
            container.BorderBrush = Brushes.Green;
            container.BorderThickness = new Thickness(2);
            ChangeTextColor(container, Brushes.DarkBlue);
        }

        private void MarkFieldAsNormal(Border container)
        {
            container.BorderBrush = Brushes.LightGray;
            container.BorderThickness = new Thickness(1);
            ChangeTextColor(container, Brushes.Black);
        }

        private void ChangeTextColor(DependencyObject parent, Brush color)
        {
            if (parent == null) return;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock textBlock && !textBlock.Name.StartsWith("label"))
                    textBlock.Foreground = color;
                else if (child is TextBox textBox)
                    textBox.Foreground = color;
                else if (child is ComboBox comboBox)
                    comboBox.Foreground = color;
                else if (child is CheckBox checkBox)
                    checkBox.Foreground = color;
 
                ChangeTextColor(child, color);
            }
        }

        public void AddFieldEditor(StackPanel container, string fieldName, Type fieldType, 
            object value, Dictionary<string, object> fieldValues, 
            Dictionary<FrameworkElement, object> originalValues, int indentLevel = 0)
        {
            var border = controlFactory.CreateFieldContainer(indentLevel);
            var grid = controlFactory.CreateFieldGrid();

            originalValues[border] = value;

            // Field name label
            var label = controlFactory.CreateLabel(fieldName + ":");
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            // Field value editor
            FrameworkElement editor = CreateEditorForType(fieldName, fieldType, value, 
                fieldValues, originalValues, indentLevel, border);

            if (editor != null)
            {
                Grid.SetColumn(editor, 1);
                grid.Children.Add(editor);
            }

            // Action buttons
            var buttonPanel = CreateActionButtons(fieldName, fieldType, fieldValues, originalValues, 
                border, container);
            Grid.SetColumn(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            border.Child = grid;
            container.Children.Add(border);

            if (value != null)
                fieldValues[fieldName] = value;
        }

        private void SafeAddToContainer(Panel container, UIElement element, int? index = null)
        {
            ErrorHandler.SafeExecute(() =>
            {
                if (index.HasValue && index.Value >= 0 && index.Value < container.Children.Count)
                    container.Children.Insert(index.Value, element);
                else
                    container.Children.Add(element);
            }, "SafeAddToContainer");
        }

        private StackPanel CreateActionButtons(string fieldName, Type fieldType,
            Dictionary<string, object> fieldValues,
            Dictionary<FrameworkElement, object> originalValues,
            Border fieldContainer, StackPanel parentContainer)
        {
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(5, 0, 0, 0)
            };

            // Confirm button
            var confirmButton = new Button
            {
                Content = "✓",
                Width = 25,
                Height = 25,
                ToolTip = "Confirm changes and save",
                Margin = new Thickness(0, 0, 0, 2)
            };
            confirmButton.Click += (s, e) =>
            {
                MarkFieldAsConfirmed(fieldContainer);

                if (fieldValues.ContainsKey(fieldName))
                {
                    // Get the current scheme to update
                    var selectedSchemeField = mainWindow.GetType()
                        .GetField("selectedScheme", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (selectedSchemeField != null)
                    {
                        var selectedScheme = selectedSchemeField.GetValue(mainWindow);
                        if (selectedScheme != null)
                        {
                            // Get current file path
                            var currentFilePathField = mainWindow.GetType()
                                .GetField("currentFilePath", BindingFlags.NonPublic | BindingFlags.Instance);
                            var currentFilePath = currentFilePathField?.GetValue(mainWindow) as string;

                            // Get current database
                            var currentDatabaseField = mainWindow.GetType()
                                .GetField("currentDatabase", BindingFlags.NonPublic | BindingFlags.Instance);
                            var currentDatabase = currentDatabaseField?.GetValue(mainWindow) as SchemeDataBase;

                            if (!string.IsNullOrEmpty(currentFilePath) && currentDatabase != null)
                            {
                                // Apply changes first - this applies fieldValues to the scheme
                                var applyMethod = mainWindow.GetType()
                                    .GetMethod("ApplyChanges", BindingFlags.Public | BindingFlags.Instance);
                                applyMethod?.Invoke(mainWindow, null);

                                // After applying, update the scheme in the database explicitly
                                var kvp = (KeyValuePair<string, ResourceScheme>)selectedScheme;
                                currentDatabase.SchemeMap[kvp.Key] = kvp.Value;

                                // Save to file
                                FileOperations.SaveScheme(mainWindow, currentDatabase, currentFilePath,
                                    (path) => currentFilePathField.SetValue(mainWindow, path));

                                // Update status
                                var statusText = mainWindow.FindName("StatusText") as TextBlock;
                                if (statusText != null)
                                {
                                    statusText.Text = $"Saved changes to {fieldName}";
                                }

                                // Clear the has changes flag after successful save
                                var setHasChangesMethod = mainWindow.GetType()
                                    .GetMethod("SetHasChanges", BindingFlags.Public | BindingFlags.Instance);
                                setHasChangesMethod?.Invoke(mainWindow, new object[] { false });
                            }
                            else
                            {
                                // If no file is loaded, prompt to save as
                                var result = MessageBox.Show("No file is currently loaded. Would you like to save as a new file?",
                                    "Save As", MessageBoxButton.YesNo, MessageBoxImage.Question);

                                if (result == MessageBoxResult.Yes)
                                {
                                    // Apply changes first
                                    var applyMethod = mainWindow.GetType()
                                        .GetMethod("ApplyChanges", BindingFlags.Public | BindingFlags.Instance);
                                    applyMethod?.Invoke(mainWindow, null);

                                    // Call Save As
                                    FileOperations.SaveSchemeAs(mainWindow, currentDatabase,
                                        (path) => {
                                            currentFilePathField?.SetValue(mainWindow, path);
                                            // After save as, clear the has changes flag
                                            var setHasChangesMethod = mainWindow.GetType()
                                                .GetMethod("SetHasChanges", BindingFlags.Public | BindingFlags.Instance);
                                            setHasChangesMethod?.Invoke(mainWindow, new object[] { false });
                                        });
                                }
                            }
                        }
                    }
                }
            };

            var resetButton = new Button
            {
                Content = "↺",
                Width = 25,
                Height = 25,
                ToolTip = "Reset to original value from backup",
                Margin = new Thickness(0, 0, 0, 2)
            };
            resetButton.Click += (s, e) =>
            {
                var backupDatabaseField = mainWindow.GetType()
                    .GetField("backupDatabase", BindingFlags.NonPublic | BindingFlags.Instance);
                var backupDatabase = backupDatabaseField?.GetValue(mainWindow) as SchemeDataBase;

                if (backupDatabase != null)
                {
                    var selectedSchemeField = mainWindow.GetType()
                        .GetField("selectedScheme", BindingFlags.NonPublic | BindingFlags.Instance);
                    var selectedSchemeKvp = selectedSchemeField?.GetValue(mainWindow);

                    if (selectedSchemeKvp != null)
                    {
                        var kvp = (KeyValuePair<string, ResourceScheme>)selectedSchemeKvp;
                        var schemeName = kvp.Key;

                        // Find the backup scheme
                        if (backupDatabase.SchemeMap.ContainsKey(schemeName))
                        {
                            var backupScheme = backupDatabase.SchemeMap[schemeName];

                            // Find the specific field value in the backup scheme
                            var backupType = backupScheme.GetType();
                            object backupValue = null;
                            bool foundField = false;

                            // Try to find as property
                            var prop = backupType.GetProperty(fieldName);
                            if (prop != null && prop.CanRead)
                            {
                                backupValue = prop.GetValue(backupScheme);
                                foundField = true;
                            }
                            else
                            {
                                // Try to find as field
                                var field = backupType.GetField(fieldName);
                                if (field != null)
                                {
                                    backupValue = field.GetValue(backupScheme);
                                    foundField = true;
                                }
                            }

                            if (foundField)
                            {
                                // Update the field value
                                fieldValues[fieldName] = backupValue;
                                originalValues[fieldContainer] = backupValue;

                                MarkFieldAsNormal(fieldContainer);

                                // Find the editor control in the grid and update it
                                var grid = fieldContainer.Child as Grid;
                                if (grid != null && grid.Children.Count > 1)
                                {
                                    var editor = grid.Children[1] as FrameworkElement;

                                    // Update the editor control based on its type
                                    if (editor is TextBox textBox)
                                    {
                                        textBox.Text = backupValue?.ToString() ?? "";
                                    }
                                    else if (editor is CheckBox checkBox)
                                    {
                                        checkBox.IsChecked = backupValue as bool? ?? false;
                                    }
                                    else if (editor is ComboBox comboBox)
                                    {
                                        comboBox.SelectedItem = backupValue;
                                    }
                                    else
                                    {
                                        // For complex types, we need to reload the entire field
                                        var index = parentContainer.Children.IndexOf(fieldContainer);
                                        if (index >= 0)
                                        {
                                            parentContainer.Children.RemoveAt(index);

                                            var indentLevel = (int)(fieldContainer.Margin.Left / 20);

                                            var newBorder = controlFactory.CreateFieldContainer(indentLevel);
                                            originalValues[newBorder] = backupValue;

                                            var newGrid = controlFactory.CreateFieldGrid();

                                            // Re-create the entire field editor
                                            var label = controlFactory.CreateLabel(fieldName + ":");
                                            Grid.SetColumn(label, 0);
                                            newGrid.Children.Add(label);

                                            var newEditor = CreateEditorForType(fieldName,
                                                fieldType,
                                                backupValue, fieldValues, originalValues, indentLevel, newBorder);

                                            if (newEditor != null)
                                            {
                                                Grid.SetColumn(newEditor, 1);
                                                newGrid.Children.Add(newEditor);
                                            }

                                            var newButtonPanel = CreateActionButtons(fieldName, fieldType, fieldValues,
                                                originalValues, newBorder, parentContainer);
                                            Grid.SetColumn(newButtonPanel, 2);
                                            newGrid.Children.Add(newButtonPanel);

                                            newBorder.Child = newGrid;
                                            parentContainer.Children.Insert(index, newBorder);
                                        }
                                    }
                                }

                                var statusText = mainWindow.FindName("StatusText") as TextBlock;
                                if (statusText != null)
                                    statusText.Text = $"Restored {fieldName} from backup";
                            }
                            else
                            {
                                MessageBox.Show($"Could not find field '{fieldName}' in backup.", 
                                    "Field Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                        else
                        {
                            MessageBox.Show($"Backup scheme '{schemeName}' not found.", 
                                "Backup Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
                else
                {
                    MessageBox.Show("No backup available. Please load a file first.", 
                        "No Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            var removeButton = new Button
            {
                Content = "X",
                Width = 25,
                Height = 25,
                ToolTip = "Remove this field",
                Margin = new Thickness(0, 0, 0, 0)
            };
            removeButton.Click += (s, e) =>
            {
                var result = MessageBox.Show($"Are you sure you want to remove the field '{fieldName}'?", 
                    "Confirm Remove", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Remove from UI
                    parentContainer.Children.Remove(fieldContainer);

                    // Remove from tracking dictionaries
                    fieldValues.Remove(fieldName);
                    originalValues.Remove(fieldContainer);

                    var parentBorder = parentContainer.Parent as Border;
                    if (parentBorder != null)
                        MarkFieldAsEdited(parentBorder);

                    // Mark window as having changes
                    var setHasChangesMethod = mainWindow.GetType()
                        .GetMethod("SetHasChanges", BindingFlags.Public | BindingFlags.Instance);
                    setHasChangesMethod?.Invoke(mainWindow, new object[] { true });

                    var statusText = mainWindow.FindName("StatusText") as TextBlock;
                    if (statusText != null)
                        statusText.Text = $"Removed field: {fieldName}";
                }
            };

            buttonPanel.Children.Add(confirmButton);
            buttonPanel.Children.Add(resetButton);
            buttonPanel.Children.Add(removeButton);

            return buttonPanel;
        }

        private FrameworkElement CreateEditorForType(string fieldName, Type fieldType,
            object value, Dictionary<string, object> fieldValues,
            Dictionary<FrameworkElement, object> originalValues, int indentLevel,
            Border container)
        {
            if (value == null)
                return CreateNullEditor(fieldName, fieldType, fieldValues, originalValues, indentLevel, container);

            if (value is System.Collections.IDictionary && !(value is string))
            {
                return CreateDictionaryEditor(fieldName, value.GetType(), value, indentLevel, container);
            }
            else if (value is System.Collections.IList && !(value is byte[]))
            {
                return CreateListEditor(fieldName, value.GetType(), value, indentLevel, container);
            }
            else if (value.GetType().IsGenericType)
            {
                var genericDef = value.GetType().GetGenericTypeDefinition();
                if (genericDef == typeof(HashSet<>))
                {
                    return CreateHashSetEditor(fieldName, value.GetType(), value, indentLevel, container);
                }
            }

            // Handle interfaces and abstract classes before checking for simple types
            if (fieldType.IsInterface || fieldType.IsAbstract)
            {
                return CreateInterfaceEditor(fieldName, fieldType, value, fieldValues, container, indentLevel);
            }

            // Handle arrays before primitives (since byte[] is special)
            if (fieldType.IsArray)
            {
                if (fieldType == typeof(byte[]))
                    return CreateByteArrayEditor(fieldName, value as byte[], fieldValues, container);
                else
                    return CreateArrayEditor(fieldName, fieldType, value, indentLevel, container);
            }

            // Simple/primitive types
            if (fieldType == typeof(string))
            {
                return controlFactory.CreateTextBox(value?.ToString() ?? "", (text) =>
                {
                    fieldValues[fieldName] = text;
                    MarkFieldAsEdited(container);
                });
            }
            else if (fieldType == typeof(bool))
            {
                return controlFactory.CreateCheckBox((bool)(value ?? false), (isChecked) =>
                {
                    fieldValues[fieldName] = isChecked;
                    MarkFieldAsEdited(container);
                });
            }
            else if (fieldType.IsEnum)
            {
                return controlFactory.CreateEnumComboBox(fieldType, value, (selected) =>
                {
                    fieldValues[fieldName] = selected;
                    MarkFieldAsEdited(container);
                });
            }
            else if (fieldType.IsPrimitive || fieldType == typeof(decimal))
            {
                return controlFactory.CreateNumericTextBox(fieldType, value, (newValue) =>
                {
                    fieldValues[fieldName] = newValue;
                    MarkFieldAsEdited(container);
                });
            }
            // Handle complex classes (this should catch CxScheme and similar types)
            else if (fieldType.IsClass && fieldType != typeof(string) && fieldType != typeof(object))
                return CreateComplexTypeEditor(fieldName, fieldType, value, indentLevel, container);

            // Default fallback
            return new TextBlock
            {
                Text = $"[Unsupported type: {fieldType.Name}] {value?.ToString() ?? "[null]"}",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Red
            };
        }

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

            // Get all compatible types
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

            // Create type selection combobox
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

            // Create instance button
            var createButton = new Button
            {
                Content = "Create Instance",
                Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(10, 5, 10, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
            typeSelectionPanel.Children.Add(createButton);

            stackPanel.Children.Add(typeSelectionPanel);

            // Status/info panel
            var statusPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var statusText = new TextBlock
            {
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 5, 0, 0)
            };

            // Current instance display
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

            // Update display based on current value
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

                    // Show key properties if available
                    var properties = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.CanRead && (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string)))
                        //.Take(20) // Limit to avoid clutter
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
                                    FontFamily = new System.Windows.Media.FontFamily("Consolas")
                                };
                                instanceDetailsPanel.Children.Add(propText);
                            }
                            catch { }
                        }
                    }

                    // Add edit button for complex configuration
                    var editButton = new Button
                    {
                        Content = "Edit Properties",
                        Margin = new Thickness(0, 10, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(10, 5, 10, 5)
                    };
                    editButton.Click += (s, e) =>
                    {
                        // Create a popup window for editing the instance
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

                        // Refresh display after editing
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

                                // Try parameterless constructor first
                                var parameterlessConstructor = selectedType.GetConstructor(Type.EmptyTypes);
                                if (parameterlessConstructor != null)
                                {
                                    newInstance = Activator.CreateInstance(selectedType);
                                }
                                else
                                {
                                    // Show parameter dialog
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

                                    // Reload the parent to show the new instance
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

            // Handle regular classes
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

                        // Try parameterless constructor first
                        var parameterlessConstructor = fieldType.GetConstructor(Type.EmptyTypes);
                        if (parameterlessConstructor != null)
                        {
                            newInstance = Activator.CreateInstance(fieldType);
                        }
                        else
                        {
                            // Show parameter dialog for constructors with parameters
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

                            // Reload the parent to show the new instance
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

            // Default null display for value types and other nullable types
            return new TextBlock
            {
                Text = "[null]",
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private object CreateInstance(Type type)
        {
            // For collection interfaces, create concrete implementations
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

        public FrameworkElement CreateFieldEditor(string fieldName, Type fieldType,
            object value, Dictionary<string, object> fieldValues,
            Dictionary<FrameworkElement, object> originalValues, int indentLevel)
        {
            var border = controlFactory.CreateFieldContainer(indentLevel);
            var grid = controlFactory.CreateFieldGrid();

            originalValues[border] = value;

            // Field name label
            var label = controlFactory.CreateLabel(fieldName + ":");
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            // Field value editor
            FrameworkElement editor = CreateEditorForType(fieldName, fieldType, value,
                fieldValues, originalValues, indentLevel, border);

            if (editor != null)
            {
                Grid.SetColumn(editor, 1);
                grid.Children.Add(editor);
            }

            // Action buttons - simplified
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
                        // For complex types in arrays
                        valueEditor = CreateComplexTypeEditor($"{fieldName}[{index}]", elementType, itemValue, indentLevel + 1, container);
                    }

                    Grid.SetColumn(valueEditor, 1);
                    itemPanel.Children.Add(valueEditor);

                    itemsPanel.Children.Add(itemPanel);
                }
            }

            stackPanel.Children.Add(itemsPanel);
            expander.Content = stackPanel;

            // Store the array value
            var fieldValues2 = mainWindow.GetType()
                .GetField("currentFieldValues", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainWindow) as Dictionary<string, object>;
            if (fieldValues2 != null)
                fieldValues2[fieldName] = array;

            return expander;
        }

        private FrameworkElement CreateByteArrayEditor(string fieldName, byte[] value,
            Dictionary<string, object> fieldValues, Border container)
        {
            var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var textBox = new TextBox
            {
                Text = value != null ? $"[{value.Length} bytes]" : "[null]",
                IsReadOnly = true,
                Width = 150,
                VerticalAlignment = VerticalAlignment.Center
            };

            var loadButton = new Button
            {
                Content = "Load File",
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(10, 2, 10, 2)
            };

            loadButton.Click += (s, e) =>
            {
                var dialog = new OpenFileDialog
                {
                    Title = $"Load binary data for {fieldName}"
                };

                if (dialog.ShowDialog() == true)
                {
                    var bytes = File.ReadAllBytes(dialog.FileName);
                    fieldValues[fieldName] = bytes;
                    textBox.Text = $"[{bytes.Length} bytes]";
                    MarkFieldAsEdited(container);
                }
            };

            var saveButton = new Button
            {
                Content = "Save",
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(10, 2, 10, 2)
            };

            saveButton.Click += (s, e) =>
            {
                var bytes = fieldValues.ContainsKey(fieldName) ? 
                    fieldValues[fieldName] as byte[] : value;
                if (bytes != null)
                {
                    var dialog = new SaveFileDialog
                    {
                        Title = $"Save binary data from {fieldName}",
                        FileName = $"{fieldName}.dat"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        File.WriteAllBytes(dialog.FileName, bytes);
                        MessageBox.Show($"Saved {bytes.Length} bytes to {Path.GetFileName(dialog.FileName)}",
                            "Save Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    MessageBox.Show("No data to save", "Warning", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            stackPanel.Children.Add(textBox);
            stackPanel.Children.Add(loadButton);
            stackPanel.Children.Add(saveButton);
            return stackPanel;
        }

        private FrameworkElement CreateComplexTypeEditor(string fieldName, Type type,
            object value, int indentLevel, Border container)
        {
            var mainPanel = new StackPanel();

            // Header showing the type
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

            // Fields panel
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

                // Pass container through to LoadComplexTypeFields
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

            // Check actual value type for collections first
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

            // Get all compatible types
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

            // Type selection panel
            var selectionPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            var typeCombo = new ComboBox
            {
                ItemsSource = compatibleTypes,
                DisplayMemberPath = "Name",
                MinWidth = 150,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Set current selection if we have a value
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

            // Fields panel - in it we'll show the object's fields inline
            var fieldsPanel = new StackPanel
            {
                Margin = new Thickness(10, 5, 0, 0),
                Visibility = value != null ? Visibility.Visible : Visibility.Collapsed
            };
            mainPanel.Children.Add(fieldsPanel);

            if (value != null)
                LoadInlineFields(fieldsPanel, value, fieldName, onValueChanged, indentLevel + 1);

            createButton.Click += (s, e) =>
            {
                if (typeCombo.SelectedItem is Type selectedType)
                {
                    try
                    {
                        object newInstance = null;

                        // Check if type has parameterless constructor
                        var parameterlessConstructor = selectedType.GetConstructor(Type.EmptyTypes);
                        if (parameterlessConstructor != null)
                            newInstance = Activator.CreateInstance(selectedType);
                        else
                        {
                            // Show constructor parameters dialog
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
                                return; // User cancelled
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
                    // Show constructor info in tooltip
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

            // Show type info
            var typeHeader = new TextBlock
            {
                Text = type.ToString() + $" class fields:",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            };
            container.Children.Add(typeHeader);

            // Create a local dictionary to track field values for this instance
            var localFieldValues = new Dictionary<string, object>();

            // Load properties
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
                        },
                        indentLevel,
                        new Dictionary<FrameworkElement, object>(),
                        parentContainer
                    );
                    container.Children.Add(editor);
                }
                catch { }
            }

            // Load fields
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
                        },
                        indentLevel,
                        new Dictionary<FrameworkElement, object>(),
                        parentContainer
                    );
                    container.Children.Add(editor);
                }
                catch { }
            }

            if (container.Children.Count == 1) // Only type header
            {
                container.Children.Add(new TextBlock
                {
                    Text = "No editable fields",
                    FontStyle = FontStyles.Italic,
                    Foreground = Brushes.Gray
                });
            }
        }

        private bool HasUsableConstructor(Type type)
        {
            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

            if (constructors.Any(c => c.GetParameters().Length == 0))
                return true;

            return constructors.Any();
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
            Grid.SetColumn(editButton, 1);

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
                var dialog = new OpenFileDialog();
                if (dialog.ShowDialog() == true)
                {
                    var bytes = File.ReadAllBytes(dialog.FileName);
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
                    var dialog = new SaveFileDialog { FileName = $"{fieldName}.dat" };
                    if (dialog.ShowDialog() == true)
                    {
                        File.WriteAllBytes(dialog.FileName, value);
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

                    var editButton = new Button
                    {
                        Content = "Edit",
                        Margin = new Thickness(5, 0, 0, 0),
                        Padding = new Thickness(5, 2, 5, 2)
                    };
                    Grid.SetColumn(editButton, 1);

                    editButton.Click += (s, e) =>
                    {
                        var dialog = new ArrayEditDialog(arrayType, array as Array);
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
            }

            arrayExpander.Content = arrayPanel;
            return arrayExpander;
        }

        private FrameworkElement CreateInlineNullEditor(string fieldName, Type fieldType,
            Action<string, object> onValueChanged, Grid parentGrid, int indentLevel, Border parentContainer = null)
        {
            // Special handling for interfaces and abstract classes
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

                                // Check for parameterless constructor
                                var parameterlessConstructor = selectedType.GetConstructor(Type.EmptyTypes);
                                if (parameterlessConstructor != null)
                                {
                                    newInstance = Activator.CreateInstance(selectedType);
                                }
                                else
                                {
                                    // Show constructor dialog
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

                                    // Refresh the editor
                                    var parent = parentGrid.Parent as Panel;
                                    if (parent != null)
                                    {
                                        var index = parent.Children.IndexOf(parentGrid);
                                        parent.Children.RemoveAt(index);
                                        var originalValues = mainWindow.GetType()
                                            .GetField("originalValues", BindingFlags.NonPublic | BindingFlags.Instance)
                                            ?.GetValue(mainWindow) as Dictionary<FrameworkElement, object>;
                                        var newEditor = CreateInlineFieldEditor(fieldName, fieldType, newInstance, 
                                            onValueChanged, indentLevel, originalValues);
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

            // Original null editor code for regular classes
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

                    // Check for parameterless constructor
                    var parameterlessConstructor = fieldType.GetConstructor(Type.EmptyTypes);
                    if (parameterlessConstructor != null)
                    {
                        newInstance = Activator.CreateInstance(fieldType);
                    }
                    else
                    {
                        // Show constructor dialog
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

                        // Refresh the editor
                        var parent = parentGrid.Parent as Panel;
                        if (parent != null)
                        {
                            var index = parent.Children.IndexOf(parentGrid);
                            parent.Children.RemoveAt(index);
                            var originalValues = mainWindow.GetType()
                                .GetField("originalValues", BindingFlags.NonPublic | BindingFlags.Instance)
                                ?.GetValue(mainWindow) as Dictionary<FrameworkElement, object>;
                            var newEditor = CreateInlineFieldEditor(fieldName, fieldType, newInstance, 
                                onValueChanged, indentLevel, originalValues);
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

        public FrameworkElement CreateListEditor(string fieldName, Type listType, object value, int indentLevel, Border container)
        {
            var expander = new Expander
            {
                Header = $"List [{(value as System.Collections.IList)?.Count ?? 0} items]"
            };

            var stackPanel = new StackPanel();

            var addButton = new Button
            {
                Content = "Add Item",
                Margin = new Thickness(0, 5, 0, 5),
                HorizontalAlignment = HorizontalAlignment.Left
            };

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

            // Work directly with the original list if it exists
            System.Collections.IList workingList = null;
            bool isArray = false;
            Type arrayElementType = null;

            if (value != null)
            {
                if (value is Array sourceArray)
                {
                    // Arrays need special handling - convert to List for editing
                    isArray = true;
                    arrayElementType = sourceArray.GetType().GetElementType();
                    var listTypeForEditing = typeof(List<>).MakeGenericType(arrayElementType);
                    workingList = Activator.CreateInstance(listTypeForEditing) as System.Collections.IList;

                    foreach (var item in sourceArray)
                        workingList.Add(item);
                }
                else if (value is System.Collections.IList sourceList)
                {
                    if (listType.IsGenericType)
                        workingList = Activator.CreateInstance(listType) as System.Collections.IList;
                    else
                        workingList = new System.Collections.ArrayList();

                    foreach (var item in sourceList)
                        workingList.Add(item);
                }
            }

            var fieldValues = mainWindow.GetType()
                .GetField("currentFieldValues", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainWindow) as Dictionary<string, object>;
            fieldValues[fieldName] = workingList;

            // Store reference to the parent container
            expander.Tag = container;

            void RefreshList()
            {
                itemsPanel.Children.Clear();
                expander.Header = $"List [{workingList.Count} items]";

                for (int i = 0; i < workingList.Count; i++)
                {
                    var index = i;
                    var itemValue = workingList[index];

                    var itemContainer = controlFactory.CreateItemContainer();
                    var itemPanel = controlFactory.CreateItemGrid();

                    // Index label
                    var indexLabel = new TextBlock
                    {
                        Text = $"[{index}]",
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 0, 5, 0),
                        FontWeight = FontWeights.Bold
                    };
                    Grid.SetColumn(indexLabel, 0);
                    itemPanel.Children.Add(indexLabel);

                    FrameworkElement itemEditor = CreateListItemEditor(itemType, itemValue, index,
                        workingList, fieldName, fieldValues, indentLevel, container);
                    Grid.SetColumn(itemEditor, 1);
                    itemPanel.Children.Add(itemEditor);

                    var removeBtn = new Button
                    {
                        Content = "X",
                        Width = 20,
                        Height = 20,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(5, 0, 0, 0)
                    };

                    var currentIndex = index;
                    removeBtn.Click += (s, e) =>
                    {
                        if (currentIndex < workingList.Count)
                            workingList.RemoveAt(currentIndex);
                        fieldValues[fieldName] = workingList;
                        RefreshList();

                        if (container != null)
                            MarkFieldAsEdited(container);
                    };

                    Grid.SetColumn(removeBtn, 2);
                    itemPanel.Children.Add(removeBtn);

                    itemContainer.Child = itemPanel;
                    itemsPanel.Children.Add(itemContainer);
                }
            }

            addButton.Click += (s, e) =>
            {
                if (workingList.Count > 0 && itemType.IsClass && itemType != typeof(string))
                {
                    try
                    {
                        var firstItem = workingList[0];
                        var clonedItem = CloneObject(firstItem);
                        if (!workingList.IsFixedSize)
                        {
                            workingList.Add(clonedItem);
                            if (container != null)
                                MarkFieldAsEdited(container);
                        }
                        RefreshList();
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Error adding item: {ex.Message}");
                        ShowAddItemDialog();
                    }
                }
                else
                    ShowAddItemDialog();
            };

            void ShowAddItemDialog()
            {
                var dialog = new AddListItemDialog(itemType);
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        workingList.Add(dialog.Value);

                        if (isArray)
                        {
                            var array = Array.CreateInstance(arrayElementType, workingList.Count);
                            workingList.CopyTo(array, 0);
                            fieldValues[fieldName] = array;
                        }
                        else
                            fieldValues[fieldName] = workingList;

                        if (container != null)
                            MarkFieldAsEdited(container);

                        RefreshList();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error adding item: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }

            RefreshList();

            stackPanel.Children.Add(addButton);
            stackPanel.Children.Add(itemsPanel);
            expander.Content = stackPanel;

            return expander;
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

                    // Mark as edited when item value changes
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
                    });
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

                    // Mark parent container as edited when nested values change
                    if (parentContainer != null)
                        MarkFieldAsEdited(parentContainer);
                }

                // Properties
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
                        originalValues
                    );
                    itemContent.Children.Add(propEditor);
                }

                // Fields
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
                        originalValues
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

                        // Find the parent list expander and refresh
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

        public FrameworkElement CreateDictionaryEditor(string fieldName, Type dictType, object value, int indentLevel, Border container)
        {
            var expander = new Expander
            {
                Header = $"Dictionary [{(value as System.Collections.IDictionary)?.Count ?? 0} items]"
            };

            var stackPanel = new StackPanel();

            var addButton = new Button
            {
                Content = "Add Entry",
                Margin = new Thickness(0, 5, 0, 5),
                HorizontalAlignment = HorizontalAlignment.Left
            };

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

            // Don't create a copy, use the original reference if it's not null
            System.Collections.IDictionary workingDict = null;
            if (value != null && value is System.Collections.IDictionary originalDict)
            {
                // Create a new dictionary instance (COPY)
                if (dictType.IsGenericType)
                {
                    var genericArgs = dictType.GetGenericArguments();
                    var keyTypeI = genericArgs[0];
                    var valueTypeI = genericArgs[1];
                    var concreteDictType = dictType.IsInterface ?
                        typeof(Dictionary<,>).MakeGenericType(keyTypeI, valueTypeI) : dictType;
                    workingDict = Activator.CreateInstance(concreteDictType) as System.Collections.IDictionary;

                    // Copy all entries
                    foreach (System.Collections.DictionaryEntry entry in originalDict)
                    {
                        workingDict.Add(entry.Key, entry.Value);
                    }
                }
                else
                {
                    workingDict = new System.Collections.Hashtable();
                    foreach (System.Collections.DictionaryEntry entry in originalDict)
                    {
                        workingDict.Add(entry.Key, entry.Value);
                    }
                }
            }

            var fieldValues = mainWindow.GetType()
                .GetField("currentFieldValues", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainWindow) as Dictionary<string, object>;

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
                        entries.Add(entry);
                }

                foreach (var dictEntry in entries)
                {
                    var entryExpander = CreateDictionaryEntryEditor(dictEntry, keyType, valueType,
                        workingDict, fieldName, fieldValues, indentLevel, RefreshDictionary, container);
                    itemsPanel.Children.Add(entryExpander);
                }
            }

            addButton.Click += (s, e) =>
            {
                var dialog = new AddDictionaryEntryDialog(keyType, valueType);
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        if (!workingDict.Contains(dialog.Key))
                        {
                            workingDict.Add(dialog.Key, dialog.Value);
                            fieldValues[fieldName] = workingDict;
                            if (container != null)
                                MarkFieldAsEdited(container);

                            RefreshDictionary();
                        }
                        else
                        {
                            MessageBox.Show("A key with this value already exists.", "Duplicate Key",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error adding entry: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            };

            RefreshDictionary();

            stackPanel.Children.Add(addButton);
            stackPanel.Children.Add(itemsPanel);
            expander.Content = stackPanel;

            return expander;
        }

        private System.Collections.IDictionary CreateWorkingDictionary(Type dictType, object value, 
            Type keyType, Type valueType)
        {
            System.Collections.IDictionary workingDict = null;

            if (value != null)
            {
                var sourceDict = value as System.Collections.IDictionary;
                if (sourceDict != null)
                {
                    if (dictType.IsGenericType || (value.GetType().IsGenericType))
                    {
                        var dictGenericDef = dictType.IsGenericType ? dictType : value.GetType();
                        if (dictGenericDef.IsInterface)
                        {
                            var concreteDictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                            workingDict = Activator.CreateInstance(concreteDictType) as System.Collections.IDictionary;
                        }
                        else
                            workingDict = Activator.CreateInstance(dictGenericDef) as System.Collections.IDictionary;
                    }
                    else
                        workingDict = new System.Collections.Hashtable();

                    foreach (System.Collections.DictionaryEntry entry in sourceDict)
                        workingDict.Add(entry.Key, entry.Value);
                }
            }
            else
            {
                if (dictType.IsGenericType)
                {
                    if (dictType.IsInterface)
                    {
                        var concreteDictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                        workingDict = Activator.CreateInstance(concreteDictType) as System.Collections.IDictionary;
                    }
                    else
                        workingDict = Activator.CreateInstance(dictType) as System.Collections.IDictionary;
                }
                else
                    workingDict = new System.Collections.Hashtable();
            }

            return workingDict;
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

            // Key display
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
                dictEntry.Value,
                    (name, newValue) =>
                    {
                        workingDict[currentKey] = newValue;
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
                        var value = workingDict[currentKey];
                        workingDict.Remove(currentKey);
                        workingDict.Add(newKey, value);
                        currentKey = newKey;
                        entryExpander.Header = $"[{newKey}]";
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

        public FrameworkElement CreateHashSetEditor(string fieldName, Type setType, object value, int indentLevel, Border container)
        {
            var expander = new Expander
            {
                Header = $"HashSet [{(value as System.Collections.IEnumerable)?.Cast<object>().Count() ?? 0} items]"
            };

            var stackPanel = new StackPanel();

            var addButton = new Button
            {
                Content = "Add Item",
                Margin = new Thickness(0, 5, 0, 5),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var itemsPanel = new StackPanel();

            Type itemType = setType.GetGenericArguments()[0];

            var workingSet = value != null ? Activator.CreateInstance(setType, value) : Activator.CreateInstance(setType);

            var fieldValues = mainWindow.GetType()
                .GetField("currentFieldValues", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(mainWindow) as Dictionary<string, object>;
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
                    itemPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    itemPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // Create appropriate editor based on item type
                    FrameworkElement itemEditor = null;
                    if (itemType == typeof(string))
                    {
                        var textBox = new TextBox
                        {
                            Text = item?.ToString() ?? "",
                            VerticalAlignment = VerticalAlignment.Center,
                            IsReadOnly = true // HashSet items can't be edited in place: delete and recreate them instead
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

                    Grid.SetColumn(itemEditor, 0);
                    itemPanel.Children.Add(itemEditor);

                    var removeBtn = new Button
                    {
                        Content = "X",
                        Width = 20,
                        Height = 20,
                        Margin = new Thickness(5, 0, 0, 0)
                    };
                    removeBtn.Click += (s, e) =>
                    {
                        var removeMethod = setType.GetMethod("Remove");
                        removeMethod.Invoke(workingSet, new[] { item });
                        if (container != null)
                            MarkFieldAsEdited(container);
                        RefreshSet();
                    };
                    Grid.SetColumn(removeBtn, 1);
                    itemPanel.Children.Add(removeBtn);

                    itemContainer.Child = itemPanel;
                    itemsPanel.Children.Add(itemContainer);
                }
            }

            addButton.Click += (s, e) =>
            {

                // Use first item as template if available
                var items = (workingSet as System.Collections.IEnumerable).Cast<object>().ToList();
                if (items.Count > 0 && itemType.IsClass && itemType != typeof(string))
                {
                    try
                    {
                        var clonedItem = CloneObject(items[0]);
                        var addMethod = setType.GetMethod("Add");
                        addMethod.Invoke(workingSet, new[] { clonedItem });
                        RefreshSet();
                        if (container != null)
                            MarkFieldAsEdited(container);
                        return;
                    }
                    catch { }
                }

                var dialog = new AddListItemDialog(itemType);
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        var addMethod = setType.GetMethod("Add");
                        var result = addMethod.Invoke(workingSet, new[] { dialog.Value });
                        if (result is bool added && !added)
                        {
                            MessageBox.Show("Item already exists in the set.", "Duplicate Item",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }

                        if (container != null)
                            MarkFieldAsEdited(container);
                        RefreshSet();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error adding item: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            };

            RefreshSet();

            stackPanel.Children.Add(addButton);
            stackPanel.Children.Add(itemsPanel);
            expander.Content = stackPanel;

            return expander;
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

                // Copy fields
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    field.SetValue(cloned, field.GetValue(source));

                // Copy properties
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.CanRead && prop.CanWrite)
                        prop.SetValue(cloned, prop.GetValue(source));
                }

                return cloned;
            }
            catch
            {
                // If cloning fails, create new instance
                return Activator.CreateInstance(type);
            }
        }
    }
}